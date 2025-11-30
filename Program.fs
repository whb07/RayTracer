open System
open System.IO
open System.Threading.Tasks

// ============================================================================
// LOW-LEVEL MATH (Aggressively Inlined & Struct-based)
// ============================================================================

[<Struct>]
type Vec3 = 
    val X : float
    val Y : float
    val Z : float
    new (x, y, z) = { X = x; Y = y; Z = z }
    
    static member inline Zero = Vec3(0.0, 0.0, 0.0)
    
    static member inline (+) (a: Vec3, b: Vec3) = Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z)
    static member inline (-) (a: Vec3, b: Vec3) = Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z)
    static member inline (~-) (a: Vec3) = Vec3(-a.X, -a.Y, -a.Z)
    static member inline (*) (a: Vec3, b: Vec3) = Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z)
    static member inline (*) (a: Vec3, t: float) = Vec3(a.X * t, a.Y * t, a.Z * t)
    static member inline (*) (t: float, a: Vec3) = Vec3(a.X * t, a.Y * t, a.Z * t)
    static member inline (/) (a: Vec3, t: float) = 
        let inv = 1.0 / t
        Vec3(a.X * inv, a.Y * inv, a.Z * inv)

    member inline this.LengthSquared() = this.X * this.X + this.Y * this.Y + this.Z * this.Z
    member inline this.Length() = sqrt (this.LengthSquared())

module Vec3 =
    let inline dot (a: Vec3) (b: Vec3) = a.X * b.X + a.Y * b.Y + a.Z * b.Z
    
    let inline cross (a: Vec3) (b: Vec3) = 
        Vec3(a.Y * b.Z - a.Z * b.Y,
             a.Z * b.X - a.X * b.Z,
             a.X * b.Y - a.Y * b.X)
             
    let inline unitVector (v: Vec3) = v / v.Length()

    // Random vector generation helpers
    let rnd = new System.Threading.ThreadLocal<Random>(fun () -> Random())
    
    let inline random() = 
        Vec3(rnd.Value.NextDouble(), rnd.Value.NextDouble(), rnd.Value.NextDouble())

    let inline randomRange (min: float) (max: float) =
        Vec3(min + (max - min) * rnd.Value.NextDouble(),
             min + (max - min) * rnd.Value.NextDouble(),
             min + (max - min) * rnd.Value.NextDouble())

    let rec randomInUnitSphere() =
        let p = randomRange -1.0 1.0
        if p.LengthSquared() >= 1.0 then randomInUnitSphere() else p

    let inline randomUnitVector() = unitVector (randomInUnitSphere())

    let rec randomInUnitDisk() =
        let p = Vec3(rnd.Value.NextDouble() * 2.0 - 1.0, rnd.Value.NextDouble() * 2.0 - 1.0, 0.0)
        if p.LengthSquared() >= 1.0 then randomInUnitDisk() else p

    let inline reflect (v: Vec3) (n: Vec3) = v - 2.0 * dot v n * n

    let inline refract (uv: Vec3) (n: Vec3) (etaiOverEtat: float) =
        let cosTheta = min (dot -uv n) 1.0
        let rOutPerp = etaiOverEtat * (uv + cosTheta * n)
        let rOutParallel = -sqrt(abs(1.0 - rOutPerp.LengthSquared())) * n
        rOutPerp + rOutParallel

[<Struct>]
type Ray =
    val Origin : Vec3
    val Direction : Vec3
    new (origin, direction) = { Origin = origin; Direction = direction }
    member inline this.At(t: float) = this.Origin + t * this.Direction

// ============================================================================
// MATERIALS & HITS
// ============================================================================

type MaterialType =
    | Lambertian of albedo: Vec3
    | Metal of albedo: Vec3 * fuzz: float
    | Dielectric of ir: float

[<Struct>]
type Intersection =
    val P : Vec3
    val Normal : Vec3
    val Mat : MaterialType
    val T : float
    val FrontFace : bool
    new (p, normal, mat, t, frontFace) =
        { P = p; Normal = normal; Mat = mat; T = t; FrontFace = frontFace }

module Intersection =
    let inline setFaceNormal (r: Ray) (outwardNormal: Vec3) =
        let frontFace = Vec3.dot r.Direction outwardNormal < 0.0
        let normal = if frontFace then outwardNormal else -1.0 * outwardNormal
        frontFace, normal

// ============================================================================
// SCENE OBJECTS
// ============================================================================

// Using a simple Discriminated Union for shapes. 
// For max perf in complex scenes, arrays of structs (SoA) would be better, 
// but for a basic "complete" raytracer, this is sufficient and readable.
type Sphere = { Center: Vec3; Radius: float; Mat: MaterialType }

type Hittable = 
    | Sphere of Sphere
    | List of Hittable array

module Hittable = 
    let inline hitSphere (center: Vec3) (radius: float) (mat: MaterialType) (r: Ray) (tMin: float) (tMax: float) : Intersection option =
        let oc = r.Origin - center
        let a = Vec3.dot r.Direction r.Direction
        let halfB = Vec3.dot oc r.Direction
        let c = (Vec3.dot oc oc) - radius * radius
        let discriminant = halfB * halfB - a * c
        
        if discriminant < 0.0 then None
        else
            let sqrtd = sqrt discriminant
            let root1 = (-halfB - sqrtd) / a
            
            let rec checkRoot root =
                if root < tMin || root > tMax then
                    let root2 = (-halfB + sqrtd) / a
                    if root2 < tMin || root2 > tMax then None
                    else Some root2
                else Some root
            
            match checkRoot root1 with
            | None -> None
            | Some root ->
                let p = r.At(root)
                let outwardNormal = (p - center) / radius
                let frontFace, normal = Intersection.setFaceNormal r outwardNormal
                Some (Intersection(p, normal, mat, root, frontFace))

    let hit (world: Hittable array) (r: Ray) (tMin: float) (tMax: float) : Intersection option =
        let mutable hitAnything : Intersection option = None
        let mutable closestSoFar = tMax
        
        // Iterative loop is often faster than Seq/List recursion for this hot path
        for i = 0 to world.Length - 1 do
            match world.[i] with
            | Sphere s ->
                match hitSphere s.Center s.Radius s.Mat r tMin closestSoFar with
                | Some recd ->
                    closestSoFar <- recd.T
                    hitAnything <- Some recd
                | None -> ()
            | List _ -> () // Flattened for performance ideally, or handle recursively
            
        hitAnything

// ============================================================================
// CAMERA
// ============================================================================

type Camera(lookFrom: Vec3, lookAt: Vec3, vup: Vec3, vfov: float, aspectRatio: float, aperture: float, focusDist: float) =
    let theta = vfov * Math.PI / 180.0
    let h = tan (theta / 2.0)
    let viewportHeight = 2.0 * h
    let viewportWidth = aspectRatio * viewportHeight
    
    let w = Vec3.unitVector (lookFrom - lookAt)
    let u = Vec3.unitVector (Vec3.cross vup w)
    let v = Vec3.cross w u
    
    let origin = lookFrom
    let horizontal = focusDist * viewportWidth * u
    let vertical = focusDist * viewportHeight * v
    let lowerLeftCorner = origin - horizontal / 2.0 - vertical / 2.0 - focusDist * w
    let lensRadius = aperture / 2.0
    
    member this.GetRay(s: float, t: float) =
        let rd = lensRadius * Vec3.randomInUnitDisk()
        let offset = u * rd.X + v * rd.Y
        Ray(origin + offset, lowerLeftCorner + s * horizontal + t * vertical - origin - offset)

// ============================================================================
// TRACING & SHADING
// ============================================================================

module Tracer = 
    let inline schlick reflectance cosine =
        let r0 = (1.0 - reflectance) / (1.0 + reflectance)
        let r0 = r0 * r0
        r0 + (1.0 - r0) * (1.0 - cosine) ** 5.0

    let rec rayColor (r: Ray) (world: Hittable array) (depth: int) : Vec3 =
        if depth <= 0 then Vec3.Zero
        else
            match Hittable.hit world r 0.001 Double.PositiveInfinity with
            | Some recd ->
                match recd.Mat with
                | Lambertian albedo ->
                    let scatterDirection = recd.Normal + Vec3.randomUnitVector()
                    // Catch degenerate scatter direction
                    let scatterDirection = 
                        if scatterDirection.LengthSquared() < 1e-8 then recd.Normal else scatterDirection
                    let scattered = Ray(recd.P, scatterDirection)
                    albedo * rayColor scattered world (depth - 1)
                    
                | Metal (albedo, fuzz) ->
                    let reflected = Vec3.reflect (Vec3.unitVector r.Direction) recd.Normal
                    let scattered = Ray(recd.P, reflected + fuzz * Vec3.randomInUnitSphere())
                    if Vec3.dot scattered.Direction recd.Normal > 0.0 then
                        albedo * rayColor scattered world (depth - 1)
                    else
                        Vec3.Zero
                        
                | Dielectric ir ->
                    let attenuation = Vec3(1.0, 1.0, 1.0)
                    let refractionRatio = if recd.FrontFace then 1.0 / ir else ir
                    let unitDir = Vec3.unitVector r.Direction
                    
                    let cosTheta = min (Vec3.dot -unitDir recd.Normal) 1.0
                    let sinTheta = sqrt (1.0 - cosTheta * cosTheta)
                    
                    let cannotRefract = refractionRatio * sinTheta > 1.0
                    
                    let direction =
                        if cannotRefract || schlick ir cosTheta > (Vec3.random().X) then
                            Vec3.reflect unitDir recd.Normal
                        else
                            Vec3.refract unitDir recd.Normal refractionRatio
                            
                    let scattered = Ray(recd.P, direction)
                    attenuation * rayColor scattered world (depth - 1)
                    
            | None ->
                let unitDir = Vec3.unitVector r.Direction
                let t = 0.5 * (unitDir.Y + 1.0)
                (1.0 - t) * Vec3(1.0, 1.0, 1.0) + t * Vec3(0.5, 0.7, 1.0)

// ============================================================================
// SCENE GENERATION
// ============================================================================

let randomScene() =
    let rnd = Random()
    let mutable worldList = ResizeArray<Hittable>()
    
    // Ground
    worldList.Add(Sphere { Center = Vec3(0.0, -1000.0, 0.0); Radius = 1000.0; Mat = Lambertian(Vec3(0.5, 0.5, 0.5)) })
    
    for a = -11 to 11 do
        for b = -11 to 11 do
            let chooseMat = rnd.NextDouble()
            let center = Vec3(float a + 0.9 * rnd.NextDouble(), 0.2, float b + 0.9 * rnd.NextDouble())
            
            if (center - Vec3(4.0, 0.2, 0.0)).Length() > 0.9 then
                if chooseMat < 0.8 then
                    // Diffuse
                    let albedo = Vec3.random() * Vec3.random()
                    worldList.Add(Sphere { Center = center; Radius = 0.2; Mat = Lambertian(albedo) })
                elif chooseMat < 0.95 then
                    // Metal
                    let albedo = Vec3.randomRange 0.5 1.0
                    let fuzz = rnd.NextDouble() * 0.5
                    worldList.Add(Sphere { Center = center; Radius = 0.2; Mat = Metal(albedo, fuzz) })
                else
                    // Glass
                    worldList.Add(Sphere { Center = center; Radius = 0.2; Mat = Dielectric(1.5) })

    worldList.Add(Sphere { Center = Vec3(0.0, 1.0, 0.0); Radius = 1.0; Mat = Dielectric(1.5) })
    worldList.Add(Sphere { Center = Vec3(-4.0, 1.0, 0.0); Radius = 1.0; Mat = Lambertian(Vec3(0.4, 0.2, 0.1)) })
    worldList.Add(Sphere { Center = Vec3(4.0, 1.0, 0.0); Radius = 1.0; Mat = Metal(Vec3(0.7, 0.6, 0.5), 0.0) })
    
    worldList.ToArray()

// ============================================================================
// MAIN
// ============================================================================

[<EntryPoint>]
let main args =
    // 1. Parse Arguments
    let width, height =
        match args with
        | [| w; h |] -> 
            match Int32.TryParse w, Int32.TryParse h with
            | (true, width), (true, height) -> width, height
            | _ -> 400, 225
        | _ -> 400, 225 // Default 16:9 aspect

    let samplesPerPixel = 50 // Lower for speed in this demo, 100+ for quality
    let maxDepth = 20
    let aspectRatio = float width / float height
    
    printfn "Rendering %dx%d image with %d samples..." width height samplesPerPixel

    // 2. Setup World & Camera
    let world = randomScene()
    
    let lookFrom = Vec3(13.0, 2.0, 3.0)
    let lookAt = Vec3(0.0, 0.0, 0.0)
    let vup = Vec3(0.0, 1.0, 0.0)
    let distToFocus = 10.0
    let aperture = 0.1
    
    let cam = Camera(lookFrom, lookAt, vup, 20.0, aspectRatio, aperture, distToFocus)

    // 3. Render
    let pixels = Array.zeroCreate<Vec3> (width * height)
    
    let rndLocal = new System.Threading.ThreadLocal<Random>(fun () -> Random())

    // Parallelize over rows (Y)
    Parallel.For(0, height, fun j ->
        // Flip Y for image coordinates (top-down vs bottom-up)
        let scanline = height - 1 - j
        for i = 0 to width - 1 do
            let mutable pixelColor = Vec3.Zero
            for _ = 0 to samplesPerPixel - 1 do
                let u = (float i + rndLocal.Value.NextDouble()) / float (width - 1)
                let v = (float scanline + rndLocal.Value.NextDouble()) / float (height - 1)
                let r = cam.GetRay(u, v)
                pixelColor <- pixelColor + Tracer.rayColor r world maxDepth
            
            pixels.[j * width + i] <- pixelColor
    ) |> ignore

    // 4. Output
    let clamp (x: float) (min: float) (max: float) =
        if x < min then min
        elif x > max then max
        else x

    let fileName = "output.ppm"
    use writer = new StreamWriter(fileName)
    writer.WriteLine($"P3\n{width} {height}\n255")

    for j = 0 to height - 1 do
        for i = 0 to width - 1 do
            let pixelColor = pixels.[j * width + i]
            // Gamma correction (sqrt)
            let scale = 1.0 / float samplesPerPixel
            let r = sqrt (pixelColor.X * scale)
            let g = sqrt (pixelColor.Y * scale)
            let b = sqrt (pixelColor.Z * scale)

            let ir = int (256.0 * clamp r 0.0 0.999)
            let ig = int (256.0 * clamp g 0.0 0.999)
            let ib = int (256.0 * clamp b 0.0 0.999)

            writer.WriteLine($"{ir} {ig} {ib}")

    printfn "Done! Saved to: %s" (Path.GetFullPath(fileName))
    0

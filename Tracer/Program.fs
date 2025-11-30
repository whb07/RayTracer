open System
open System.IO
open System.Threading.Tasks

open Tracer.RayTracer

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

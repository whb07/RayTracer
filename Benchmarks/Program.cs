using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using System.IO;
using Tracer;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Engines;

namespace Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.ColdStart, RuntimeMoniker.NativeAot10_0)]
    [SimpleJob(RunStrategy.ColdStart, RuntimeMoniker.Net10_0, baseline: true)]
    public class RayTracerBenchmarks
    {
        private RayTracer.Hittable[] world;
        private RayTracer.Hittable[] emptyWorld;
        private RayTracer.Camera cam;

        [Params(250)]
        public int Width;

        [Params(100)]
        public int Spp;

        [Params(50)]
        public int MaxDepth;

        [GlobalSetup]
        public void Setup()
        {
            world = RayTracer.randomScene();
            emptyWorld = Array.Empty<RayTracer.Hittable>();
            cam = new RayTracer.Camera(
                new RayTracer.Vec3(13.0, 2.0, 3.0),
                new RayTracer.Vec3(0.0, 0.0, 0.0),
                new RayTracer.Vec3(0.0, 1.0, 0.0),
                20.0, 16.0 / 9.0, 0.1, 10.0);
        }

        [Benchmark(Baseline = true)]
        public RayTracer.Hittable[] SceneGeneration() => RayTracer.randomScene();

        [Benchmark]
        public RayTracer.Vec3 SingleRayTracing()
        {
            var ray = cam.GetRay(0.5, 0.5);
            return RayTracer.Tracer.rayColor(ray, world, MaxDepth);
        }

        [Benchmark]
        public bool RayIntersection()
        {
            var ray = new RayTracer.Ray(RayTracer.Vec3.Zero, new RayTracer.Vec3(0.0, 0.0, 1.0));
            var hitOpt = RayTracer.HittableModule.hit(world, ray, 0.0, double.PositiveInfinity);
            return hitOpt != null;
        }

        [Benchmark]
        public RayTracer.Ray CameraRayGeneration() => cam.GetRay(0.5, 0.5);

        [Benchmark]
        public int SmallRender_10x10_1sample()
        {
            return RenderImage(10, 10, 1, world, 10);
        }

        [Benchmark]
        public int MediumRender_50x50_4samples()
        {
            return RenderImage(50, 50, 4, world, 10);
        }

        [Benchmark]
        public int RealisticRandomSceneRender()
        {
            int height = (int)(Width * 9.0 / 16.0);
            if (height == 0) height = 1;
            return RenderImage(Width, height, Spp, world, MaxDepth);
        }

        [Benchmark]
        public int GradientRender()
        {
            int height = (int)(Width * 9.0 / 16.0);
            if (height == 0) height = 1;
            return RenderImage(Width, height, Spp, emptyWorld, MaxDepth);
        }

        private int RenderImage(int width, int height, int samples, RayTracer.Hittable[] hittables, int depth)
        {
            var cam = new RayTracer.Camera(
                new RayTracer.Vec3(13.0, 2.0, 3.0),
                new RayTracer.Vec3(0.0, 0.0, 0.0),
                new RayTracer.Vec3(0.0, 1.0, 0.0),
                20.0, (double)width / height, 0.1, 10.0);

            var pixels = new RayTracer.Vec3[width * height];
            var rnd = new Random();

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    var color = new RayTracer.Vec3(0.0, 0.0, 0.0);
                    for (int s = 0; s < samples; s++)
                    {
                        var u = (i + rnd.NextDouble()) / (width - 1);
                        var v = (j + rnd.NextDouble()) / (height - 1);
                        var ray = cam.GetRay(u, v);
                        color = color + RayTracer.Tracer.rayColor(ray, hittables, depth);
                    }
                    pixels[j * width + i] = color / samples;
                }
            }

            int checksum = 0;
            for (int k = 0; k < pixels.Length; k++)
            {
                var p = pixels[k];
                int r = (int)(256.0 * Math.Clamp(p.X, 0.0, 0.999));
                int g = (int)(256.0 * Math.Clamp(p.Y, 0.0, 0.999));
                int b = (int)(256.0 * Math.Clamp(p.Z, 0.0, 0.999));
                checksum ^= (r << 16) | (g << 8) | b;
            }
            return checksum;
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            Directory.CreateDirectory("Results");

            var config = ManualConfig.Create(DefaultConfig.Instance);
            config.ArtifactsPath = Path.Combine(Directory.GetCurrentDirectory(), "Results");

            var summary = BenchmarkRunner.Run<RayTracerBenchmarks>(config);
        }
    }
}
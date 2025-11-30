using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using System.IO;
using Tracer;

namespace Benchmarks
{
    [ThreadingDiagnoser]
    [MemoryDiagnoser]
    [RankColumn]
    public class RayTracerBenchmarks
    {
        private RayTracer.Hittable[] world;
        private RayTracer.Hittable[] emptyWorld;
        private RayTracer.Camera cam;

        [Params(50, 200, 400)]
        public int Width;

        [Params(1, 4, 16, 100)]
        public int Spp;

        [Params(10, 50)]
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
        public void RayIntersection()
        {
            var ray = new RayTracer.Ray(RayTracer.Vec3.Zero, new RayTracer.Vec3(0.0, 0.0, 1.0));
            RayTracer.HittableModule.hit(world, ray, 0.0, double.PositiveInfinity);
        }

        [Benchmark]
        public RayTracer.Ray CameraRayGeneration() => cam.GetRay(0.5, 0.5);

        [Benchmark]
        public void SmallRender_10x10_1sample()
        {
            RenderImage(10, 10, 1, world, 10);
        }

        [Benchmark]
        public void MediumRender_50x50_4samples()
        {
            RenderImage(50, 50, 4, world, 10);
        }

        [Benchmark]
        public void RealisticRandomSceneRender()
        {
            int height = (int)(Width * 9.0 / 16.0);
            RenderImage(Width, height, Spp, world, MaxDepth);
        }

        [Benchmark]
        public void GradientRender()
        {
            int height = (int)(Width * 9.0 / 16.0);
            RenderImage(Width, height, Spp, emptyWorld, MaxDepth);
        }

        private void RenderImage(int width, int height, int samples, RayTracer.Hittable[] hittables, int depth)
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
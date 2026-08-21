using System;
using System.Diagnostics;
using NUnit.Framework;
using Rubickanov.ACS.Runtime;

namespace Rubickanov.ACS.Tests
{
    /// <summary>
    /// Measurement harness, not a pass/fail guard. Marked <see cref="ExplicitAttribute"/> so it
    /// stays out of normal and CI runs — wall-clock assertions on shared hardware are flaky and
    /// there is no meaningful absolute threshold to pin. Run it by hand from the Test Runner
    /// before and after touching the injection path and compare the printed numbers.
    /// <para/>
    /// Baseline reference: the injector used to resolve aspects through an
    /// <c>Expression.Lambda.Compile()</c>'d delegate and now goes through
    /// <see cref="AspectResolver"/>'s cached generic dispatcher. A virtual call and a delegate
    /// invocation cost the same order of magnitude, so the per-field figure should not move;
    /// <c>FieldInfo.SetValue</c> is expected to dominate it.
    /// </summary>
    [TestFixture]
    public class AspectInjectorPerfTests
    {
        private const int Iterations = 100_000;
        private const int FieldsPerHost = 3;

        [Test]
        [Explicit("Timing harness — run manually and compare the printed numbers.")]
        public void Inject_ThreeAspectFields_ReportsNanosecondsPerInjection()
        {
            var entity = new Entity();
            try
            {
                var host = new ThreeFieldHost();

                // Warm every cache the measured loop touches: the per-component-type FieldInfo[]
                // in AspectInjector and the per-aspect-type dispatcher in AspectResolver. Without
                // this the first iteration folds one-time build cost into the average.
                AspectInjector.Inject(entity, host);

                var beforeBytes = GC.GetTotalMemory(forceFullCollection: true);
                var stopwatch = Stopwatch.StartNew();

                for (int i = 0; i < Iterations; i++)
                    AspectInjector.Inject(entity, host);

                stopwatch.Stop();
                var afterBytes = GC.GetTotalMemory(forceFullCollection: false);

                var nsPerInjection = stopwatch.Elapsed.TotalMilliseconds * 1_000_000d / Iterations;
                var bytesPerInjection = (afterBytes - beforeBytes) / (double)Iterations;

                TestContext.WriteLine($"Iterations:      {Iterations:N0} × {FieldsPerHost} fields");
                TestContext.WriteLine($"Total:           {stopwatch.Elapsed.TotalMilliseconds:F2} ms");
                TestContext.WriteLine($"Per injection:   {nsPerInjection:F1} ns");
                TestContext.WriteLine($"Per field:       {nsPerInjection / FieldsPerHost:F1} ns");
                // Indicative only — the managed heap moves for reasons outside this loop.
                TestContext.WriteLine($"Alloc/injection: ~{bytesPerInjection:F1} B");
            }
            finally
            {
                entity.Dispose();
            }
        }

        private class PerfAspectA : IEntityAspect { }
        private class PerfAspectB : IEntityAspect { }
        private class PerfAspectC : IEntityAspect { }

        private class ThreeFieldHost
        {
            [Aspect] private readonly PerfAspectA _a = default!;
            [Aspect] private readonly PerfAspectB _b = default!;
            [Aspect] private readonly PerfAspectC _c = default!;

            // Referenced so the fields aren't flagged as unused; injection writes them via
            // reflection, which the compiler can't see.
            public object Fields => (object)_a ?? (object)_b ?? _c;
        }
    }
}

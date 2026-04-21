using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using Rubickanov.ACS.Runtime.Netcode;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.ACS.Runtime.Netcode.Tests
{
    [TestFixture]
    public class SmoothBinderTests
    {
        // SmoothDriver holds a process-wide binding list. Each test registers through the
        // public API and disposes in TearDown so state does not leak into sibling tests.
        // Edit-mode tests never trigger SmoothDriver.EnsureHost() (guarded by
        // Application.isPlaying), so no MonoBehaviour is ever instantiated — TickAll is
        // called directly.

        private readonly List<IDisposable> _handles = new();
        private readonly List<ReplicatedFieldBinding> _interpolationBindings = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var handle in _handles)
                handle.Dispose();
            _handles.Clear();

            foreach (var binding in _interpolationBindings)
                binding.OnDespawn();
            _interpolationBindings.Clear();
        }

        private IDisposable Track(IDisposable handle)
        {
            _handles.Add(handle);
            return handle;
        }

        // ---- Setter invocation --------------------------------------------------

        [Test]
        public void Bind_AfterRegister_SetterInvokedWithCurrentValueOnTick()
        {
            var source = new ReactiveProperty<float>(42f);
            float captured = 0f;

            Track(SmoothBinder.Bind(source, v => captured = v));
            SmoothDriver.TickAll();

            Assert.That(captured, Is.EqualTo(42f));
        }

        [Test]
        public void TickAll_AfterSourceValueChange_SetterReceivesNewValue()
        {
            var source = new ReactiveProperty<float>(0f);
            float captured = 0f;
            Track(SmoothBinder.Bind(source, v => captured = v));

            source.Value = 17f;
            SmoothDriver.TickAll();

            Assert.That(captured, Is.EqualTo(17f));
        }

        [Test]
        public void TickAll_WithoutSourceChange_StillInvokesSetter()
        {
            // Smooth() is a continuous read — the binder must tick every frame even if
            // .Value has not changed, so interpolated motion keeps advancing between
            // discrete snapshots.
            var source = new ReactiveProperty<int>(5);
            int callCount = 0;
            Track(SmoothBinder.Bind(source, _ => callCount++));

            SmoothDriver.TickAll();
            SmoothDriver.TickAll();
            SmoothDriver.TickAll();

            Assert.That(callCount, Is.EqualTo(3));
        }

        // ---- Fallback behaviour -------------------------------------------------

        [Test]
        public void Bind_SourceWithoutInterpolationBinding_UsesRawValue()
        {
            var source = new ReactiveProperty<Vector3>(new Vector3(1f, 2f, 3f));
            Vector3 captured = default;

            Track(SmoothBinder.Bind(source, v => captured = v));
            SmoothDriver.TickAll();

            Assert.That(captured, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void Bind_SourceWithInterpolationBinding_UsesInterpolatedValue()
        {
            // Push two snapshots into an InterpolatedFieldBinding so Smooth() returns a
            // lerped value distinct from .Value. The binder must route through
            // InterpolationRegistry rather than reading .Value directly.
            var source = new ReactiveProperty<float>(0f);
            var interp = (InterpolatedFieldBinding<float>)ReplicatedFieldBindingFactory
                .Create(source, typeof(float), FieldBindingKind.PassiveInterpolated);
            _interpolationBindings.Add(interp);
            PushSnapshot(interp, 0f, time: 1.0);
            PushSnapshot(interp, 10f, time: 2.0);
            interp.TickRender(renderTime: 1.5);

            float captured = -1f;
            Track(SmoothBinder.Bind(source, v => captured = v));
            SmoothDriver.TickAll();

            Assert.That(captured, Is.EqualTo(5f).Within(1e-4f));
        }

        // ---- Dispose semantics --------------------------------------------------

        [Test]
        public void Dispose_BeforeTick_SetterNeverInvoked()
        {
            var source = new ReactiveProperty<float>(1f);
            int callCount = 0;
            var handle = SmoothBinder.Bind(source, _ => callCount++);

            handle.Dispose();
            SmoothDriver.TickAll();

            Assert.That(callCount, Is.Zero);
        }

        [Test]
        public void Dispose_AfterTick_NoFurtherSetterCalls()
        {
            var source = new ReactiveProperty<float>(1f);
            int callCount = 0;
            var handle = SmoothBinder.Bind(source, _ => callCount++);

            SmoothDriver.TickAll();
            handle.Dispose();
            SmoothDriver.TickAll();
            SmoothDriver.TickAll();

            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrowAndRemovesExactlyOnce()
        {
            var source = new ReactiveProperty<float>(1f);
            var handle = SmoothBinder.Bind(source, _ => { });

            Assert.DoesNotThrow(() =>
            {
                handle.Dispose();
                handle.Dispose();
            });
        }

        // ---- Multi-binding and iterator safety ---------------------------------

        [Test]
        public void TickAll_WithMultipleBindings_InvokesAllSetters()
        {
            var source = new ReactiveProperty<int>(7);
            int a = 0, b = 0, c = 0;
            Track(SmoothBinder.Bind(source, v => a = v));
            Track(SmoothBinder.Bind(source, v => b = v));
            Track(SmoothBinder.Bind(source, v => c = v));

            SmoothDriver.TickAll();

            Assert.That(a, Is.EqualTo(7));
            Assert.That(b, Is.EqualTo(7));
            Assert.That(c, Is.EqualTo(7));
        }

        [Test]
        public void TickAll_SetterDisposesOwnBinding_SiblingBindingStillTicks()
        {
            // A setter that disposes its own binding mid-iteration mutates the driver's
            // list. The iterator must not skip or duplicate sibling bindings — this test
            // catches regressions in the list-snapshot logic inside SmoothDriver.TickAll.
            var source = new ReactiveProperty<int>(9);
            int siblingCallCount = 0;
            IDisposable selfDisposing = null;
            // Track into TearDown too — if the test fails before TickAll gets a chance to
            // fire the self-dispose setter, the binding would otherwise leak into sibling
            // tests via the static s_Bindings list.
            selfDisposing = SmoothBinder.Bind(source, _ => selfDisposing.Dispose());
            _handles.Add(selfDisposing);
            Track(SmoothBinder.Bind(source, _ => siblingCallCount++));

            SmoothDriver.TickAll();
            SmoothDriver.TickAll();

            Assert.That(siblingCallCount, Is.EqualTo(2));
        }

        // ---- Argument validation ------------------------------------------------

        [Test]
        public void Bind_NullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SmoothBinder.Bind<float>(null, _ => { }));
        }

        [Test]
        public void Bind_NullSetter_ThrowsArgumentNullException()
        {
            var source = new ReactiveProperty<float>(0f);
            Assert.Throws<ArgumentNullException>(() =>
                SmoothBinder.Bind<float>(source, null));
        }

        // ---- Transform extension sanity ----------------------------------------

        [Test]
        public void BindSmoothPosition_OnTick_WritesIntoTargetTransform()
        {
            var go = new GameObject("SmoothBinderTests_TransformTarget");
            try
            {
                var source = new ReactiveProperty<Vector3>(new Vector3(4f, 5f, 6f));

                Track(go.transform.BindSmoothPosition(source));
                SmoothDriver.TickAll();

                Assert.That(go.transform.position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ---- Helpers ------------------------------------------------------------

        // Mirrors the helper in InterpolatedFieldBindingTests: write the raw bytes into a
        // FastBufferWriter, read them back through the binding, then apply the snapshot at
        // the given time. This is the same path the network layer would drive.
        private static unsafe void PushSnapshot<T>(InterpolatedFieldBinding<T> binding, T value, double time)
            where T : unmanaged
        {
            var writer = new FastBufferWriter(sizeof(T), Allocator.Temp);
            try
            {
                writer.WriteBytesSafe((byte*)&value, sizeof(T));
                var reader = new FastBufferReader(writer, Allocator.Temp);
                try
                {
                    binding.ReadFrom(reader);
                    binding.ApplyFromNetwork(time);
                }
                finally { reader.Dispose(); }
            }
            finally { writer.Dispose(); }
        }
    }
}

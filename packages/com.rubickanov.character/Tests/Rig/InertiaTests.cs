using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    /// <summary>
    /// The offset that dies away (after Sinew InertializationTests): it starts as the whole difference with the old
    /// velocity kept, never passes zero when it closes fast, ends at zero with no velocity, and a switch begun in the
    /// middle of one starts from what is shown.
    /// </summary>
    public sealed class InertiaTests
    {
        private const float Length = 0.25f;
        private const float Dt = 1f / 600f;

        [Test]
        public void Begin_KeepsTheOldVelocity()
        {
            Inertia.BeginPosition(new Vector3(0.3f, 0f, 0f), new Vector3(1.5f, 0f, 0f), Vector3.zero, Vector3.zero, 0f,
                out Vector3 x, out Vector3 v);
            Inertia.Decay(x, v, Length, 0f, out Vector3 x0, out Vector3 v0);
            Assert.That(x0.x, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(v0.x, Is.EqualTo(1.5f).Within(1e-4f));
            // Numerically too: the slope over the first step is the old velocity.
            const float h = 1e-4f;
            Inertia.Decay(x, v, Length, h, out Vector3 x1, out _);
            Assert.That((x1.x - x0.x) / h, Is.EqualTo(1.5f).Within(0.02f));
        }

        [Test]
        public void ClosingFast_NeverPassesZero()
        {
            Inertia.BeginPosition(new Vector3(0.05f, 0f, 0f), new Vector3(-4f, 0f, 0f), Vector3.zero, Vector3.zero, 0f,
                out Vector3 x, out Vector3 v);
            for (float t = 0f; t <= Length; t += Dt)
            {
                Inertia.Decay(x, v, Length, t, out Vector3 at, out _);
                Assert.That(at.x, Is.GreaterThanOrEqualTo(-1e-6f), $"t {t}");
            }
        }

        [Test]
        public void EndsAtZeroWithNoVelocity()
        {
            var x = new Vector3(0.2f, -0.1f, 0.4f);
            var v = new Vector3(1f, 2f, -0.5f);
            Inertia.Decay(x, v, Length, Length - 1e-4f, out Vector3 nearEnd, out Vector3 nearEndV);
            Assert.That(nearEnd.magnitude, Is.LessThan(1e-4f));
            Assert.That(nearEndV.magnitude, Is.LessThan(1e-2f));
            Inertia.Decay(x, v, Length, Length, out Vector3 end, out Vector3 endV);
            Assert.That(end, Is.EqualTo(Vector3.zero));
            Assert.That(endV, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Turn_StartsAtTheOldPoseAndEndsOnTheNew()
        {
            Quaternion last = Quaternion.Euler(10f, 70f, -20f);
            Quaternion target = Quaternion.Euler(-30f, 5f, 40f);
            Inertia.BeginTurn(last, Vector3.zero, target, Vector3.zero, 0f, out Vector3 x, out Vector3 v);
            Assert.That(Quaternion.Angle(Inertia.ApplyTurn(x, v, Length, 0f, target), last), Is.LessThan(0.05f));
            Assert.That(Quaternion.Angle(Inertia.ApplyTurn(x, v, Length, Length, target), target), Is.LessThan(0.05f));
        }

        [Test]
        public void Turn_KeepsTheOldSpin()
        {
            Quaternion last = Quaternion.Euler(0f, 30f, 0f);
            var spin = new Vector3(0f, 2f, 0f);
            Quaternion target = Quaternion.Euler(0f, -40f, 10f);
            Inertia.BeginTurn(last, spin, target, Vector3.zero, 0f, out Vector3 x, out Vector3 v);
            // The shown spin: the offset's own through the left Jacobian (the target stands still).
            Inertia.Decay(x, v, Length, 0f, out Vector3 x0, out Vector3 v0);
            Vector3 shown = Inertia.Spin(x0, v0);
            Assert.That((shown - spin).magnitude, Is.LessThan(1e-3f), $"{shown}");
            // And numerically over a short step.
            const float h = 2e-5f;
            Vector3 step = Inertia.SpinBetween(Inertia.ApplyTurn(x, v, Length, 0f, target),
                Inertia.ApplyTurn(x, v, Length, h, target), h);
            Assert.That((step - spin).magnitude, Is.LessThan(0.05f), $"{step}");
        }

        [Test]
        public void SwitchMidSwitch_StartsFromWhatIsShown()
        {
            Quaternion first = Quaternion.Euler(0f, 80f, 0f);
            Quaternion second = Quaternion.Euler(20f, 0f, 0f);
            Quaternion third = Quaternion.Euler(-10f, -60f, 30f);
            Inertia.BeginTurn(first, Vector3.zero, second, Vector3.zero, 0f, out Vector3 x1, out Vector3 v1);
            float mid = 0.1f;
            Quaternion shown = Inertia.ApplyTurn(x1, v1, Length, mid, second);
            Inertia.Decay(x1, v1, Length, mid, out Vector3 xm, out Vector3 vm);
            Vector3 spin = Inertia.Spin(xm, vm);
            Inertia.BeginTurn(shown, spin, third, Vector3.zero, 0f, out Vector3 x2, out Vector3 v2);
            Assert.That(Quaternion.Angle(Inertia.ApplyTurn(x2, v2, Length, 0f, third), shown), Is.LessThan(0.05f));
            Inertia.Decay(x2, v2, Length, 0f, out Vector3 xs, out Vector3 vs);
            Assert.That((Inertia.Spin(xs, vs) - spin).magnitude, Is.LessThan(1e-3f));
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    public sealed class RagdollMathTests
    {
        private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"Expected {expected}, was {actual}");

        private static void AssertNear(Quaternion expected, Quaternion actual, float degrees = 0.01f) =>
            Assert.Less(Quaternion.Angle(expected, actual), degrees, $"Expected {expected}, was {actual}");

        [Test]
        public void JointSpaceLiesAlongItsAxes()
        {
            Quaternion space = RagdollMath.JointSpace(Vector3.up, new Vector3(0.3f, 0.2f, 1f));
            AssertNear(Vector3.up, space * Vector3.right);
            Assert.AreEqual(0f, Vector3.Dot(space * Vector3.up, Vector3.up), 1e-4f);
        }

        [Test]
        public void TargetIsIdentityAtRest()
        {
            Quaternion space = RagdollMath.JointSpace(Vector3.right, Vector3.forward);
            Quaternion rest = Quaternion.Euler(10f, 20f, 30f);
            AssertNear(Quaternion.identity, RagdollMath.TargetRotation(space, rest, rest));
        }

        [Test]
        public void TargetUndoesTheTurnFromRest()
        {
            // A joint whose axes are the body's: the target is the turn from the pose back to the rest.
            Quaternion rest = Quaternion.Euler(0f, 0f, 15f);
            Quaternion bent = Quaternion.Euler(40f, 0f, 0f) * rest;
            Quaternion target = RagdollMath.TargetRotation(Quaternion.identity, rest, bent);
            AssertNear(Quaternion.Inverse(bent) * rest, target);
        }

        [Test]
        public void MapKeepsTheAnimatedPoseAtZeroWeight()
        {
            Vector3 animated = new(1f, 2f, 3f);
            Quaternion animatedRotation = Quaternion.Euler(0f, 90f, 0f);
            (Vector3 position, Quaternion rotation) = RagdollMath.MapPose(animated, animatedRotation, Vector3.zero,
                Quaternion.identity, Quaternion.identity, false, default, Quaternion.identity, default,
                Quaternion.identity, Quaternion.identity, 0f);
            AssertNear(animated, position);
            AssertNear(animatedRotation, rotation);
        }

        [Test]
        public void MapKeepsTheVisibleBoneLengths()
        {
            // The ragdoll bone sits 0.5 m along its parent's x; the visible parent is elsewhere and turned.
            Vector3 parent = new(0f, 1f, 0f);
            Quaternion parentRotation = Quaternion.Euler(0f, 0f, 90f);
            Vector3 bone = parent + parentRotation * new Vector3(0.5f, 0f, 0f);
            Vector3 visibleParent = new(3f, 0f, 0f);
            Quaternion visibleParentRotation = Quaternion.Euler(0f, 45f, 0f);
            (Vector3 position, _) = RagdollMath.MapPose(Vector3.zero, Quaternion.identity, bone, Quaternion.identity,
                Quaternion.identity, true, parent, parentRotation, visibleParent, visibleParentRotation,
                Quaternion.identity, 1f);
            AssertNear(visibleParent + visibleParentRotation * new Vector3(0.5f, 0f, 0f), position);
        }

        [Test]
        public void AngularVelocityTurnsFromOneToTheOther()
        {
            Quaternion from = Quaternion.Euler(0f, 10f, 0f);
            Quaternion to = Quaternion.Euler(0f, 40f, 0f);
            Vector3 velocity = RagdollMath.AngularVelocity(from, to, 0.5f);
            AssertNear(Vector3.up * (60f * Mathf.Deg2Rad), velocity);
        }

        [Test]
        public void AngularVelocityTakesTheShortWay()
        {
            Quaternion from = Quaternion.Euler(0f, 170f, 0f);
            Quaternion to = Quaternion.Euler(0f, -170f, 0f);
            Vector3 velocity = RagdollMath.AngularVelocity(from, to, 1f);
            AssertNear(Vector3.up * (20f * Mathf.Deg2Rad), velocity);
        }

        [Test]
        public void AngularVelocityIsZeroWithoutTurnOrTime()
        {
            AssertNear(Vector3.zero, RagdollMath.AngularVelocity(Quaternion.identity, Quaternion.identity, 0.1f));
            AssertNear(Vector3.zero, RagdollMath.AngularVelocity(Quaternion.identity, Quaternion.Euler(0f, 30f, 0f), 0f));
        }

        [Test]
        public void TellsFaceUpFromFaceDown()
        {
            Assert.IsTrue(RagdollMath.LiesFaceUp(new Vector3(0.1f, 0.9f, 0f), Vector3.up));
            Assert.IsFalse(RagdollMath.LiesFaceUp(new Vector3(0.1f, -0.9f, 0f), Vector3.up));
        }

        [Test]
        public void GetUpTurnsTheClipOntoTheLyingBody()
        {
            // The clip lies with its head towards the model's back; the body lies with its head to the east.
            Vector3 clip = new(0f, 0.1f, -1f);
            Vector3 lying = new(1f, -0.05f, 0f);
            Quaternion rotation = RagdollMath.GetUpRotation(clip, lying, Vector3.up, Quaternion.identity);
            AssertNear(Vector3.right, (rotation * Vector3.ProjectOnPlane(clip, Vector3.up)).normalized);
            AssertNear(Vector3.up, rotation * Vector3.up);
        }

        [Test]
        public void GetUpFallsBackWhenTheBodyStandsOnEnd()
        {
            Quaternion fallback = Quaternion.Euler(0f, 30f, 0f);
            AssertNear(fallback, RagdollMath.GetUpRotation(Vector3.forward, Vector3.up, Vector3.up, fallback));
        }

        // Hips, spine, chest, head, upper arm, forearm: a chain with a branch at the chest.
        private static readonly int[] Parents = { -1, 0, 1, 2, 2, 4 };

        [Test]
        public void HopsCountJointsBothWaysUpToTheReach()
        {
            var hops = new int[Parents.Length];
            RagdollMath.Hops(Parents, 4, 2, hops);
            CollectionAssert.AreEqual(new[] { -1, 2, 1, 2, 0, 1 }, hops);
        }

        [Test]
        public void SpreadPushFallsOffWithTheJoints()
        {
            float[] masses = { 10f, 10f, 10f, 5f, 2f, 1f };
            var hops = new int[Parents.Length];
            RagdollMath.Hops(Parents, 4, 1, hops);
            var speeds = new float[Parents.Length];
            // Weighted mass 2 + (10 + 1) * 0.5 = 7.5, so 15 N·s is 2 m/s at the bone hit.
            float left = RagdollMath.SpreadPush(masses, hops, 0.5f, 15f, 100f, speeds);
            Assert.AreEqual(0f, left, 1e-4f);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 1f, 0f, 2f, 1f }, speeds);
        }

        [Test]
        public void SpreadPushCapsTheBoneHitAndReturnsTheRest()
        {
            float[] masses = { 10f, 10f, 10f, 5f, 2f, 1f };
            var hops = new int[Parents.Length];
            RagdollMath.Hops(Parents, 5, 0, hops);
            var speeds = new float[Parents.Length];
            float left = RagdollMath.SpreadPush(masses, hops, 0.5f, 100f, 6f, speeds);
            Assert.AreEqual(6f, speeds[5], 1e-4f);
            Assert.AreEqual(94f, left, 1e-4f);
        }

        [Test]
        public void PackedRotationsComeBackWithinAFifthOfADegree()
        {
            var random = new System.Random(7);
            for (int n = 0; n < 1000; n++)
            {
                var rotation = new Quaternion((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f,
                    (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f).normalized;
                AssertNear(rotation, RagdollMath.UnpackRotation(RagdollMath.PackRotation(rotation)), 0.2f);
            }
        }

        [Test]
        public void PacksTheSameRotationEitherSign()
        {
            Quaternion rotation = Quaternion.Euler(10f, -120f, 45f);
            var negated = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            Assert.AreEqual(RagdollMath.PackRotation(rotation), RagdollMath.PackRotation(negated));
            AssertNear(Quaternion.identity, RagdollMath.UnpackRotation(RagdollMath.PackRotation(Quaternion.identity)));
        }
    }
}

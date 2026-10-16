using NUnit.Framework;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    public sealed class TwoBoneIKTests
    {
        private Transform _upper, _lower, _end;

        [SetUp]
        public void SetUp()
        {
            // A slightly bent leg: hip at 1 m, knee forward, foot on the ground.
            _upper = new GameObject("Upper").transform;
            _lower = new GameObject("Lower").transform;
            _end = new GameObject("End").transform;
            _lower.SetParent(_upper);
            _end.SetParent(_lower);
            _upper.position = new Vector3(0f, 1f, 0f);
            _lower.position = new Vector3(0f, 0.5f, 0.05f);
            _end.position = new Vector3(0f, 0f, 0f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_upper.gameObject);

        [Test]
        public void ReachesTargetAndKeepsLengths()
        {
            float upperLength = Vector3.Distance(_upper.position, _lower.position);
            float lowerLength = Vector3.Distance(_lower.position, _end.position);
            var target = new Vector3(0.1f, 0.3f, 0.1f);

            TwoBoneIK.Solve(_upper, _lower, _end, target, _lower.position + Vector3.forward, 1f, 0f);

            Assert.Less(Vector3.Distance(_end.position, target), 1e-3f);
            Assert.AreEqual(upperLength, Vector3.Distance(_upper.position, _lower.position), 1e-4f);
            Assert.AreEqual(lowerLength, Vector3.Distance(_lower.position, _end.position), 1e-4f);
        }

        [Test]
        public void KneePointsTowardHint()
        {
            var target = new Vector3(0f, 0.4f, 0f);
            Vector3 hint = new Vector3(1f, 0.7f, 0f);

            TwoBoneIK.Solve(_upper, _lower, _end, target, hint, 1f, 0f);

            Assert.Greater(_lower.position.x, 0.1f);
            Assert.AreEqual(0f, _lower.position.z, 1e-3f);
        }

        [Test]
        public void OutOfReachStraightensWithoutNaN()
        {
            var target = new Vector3(0f, -5f, 0f);

            TwoBoneIK.Solve(_upper, _lower, _end, target, _lower.position + Vector3.forward, 1f);

            Assert.IsFalse(float.IsNaN(_end.position.y));
            Vector3 toEnd = (_end.position - _upper.position).normalized;
            Assert.Greater(Vector3.Dot(toEnd, Vector3.down), 0.999f);
        }

        [Test]
        public void KeepsEndRotation()
        {
            Quaternion before = _end.rotation;

            TwoBoneIK.Solve(_upper, _lower, _end, new Vector3(0.2f, 0.2f, 0f), _lower.position + Vector3.forward, 1f);

            Assert.Less(Quaternion.Angle(before, _end.rotation), 1e-3f);
        }

        [Test]
        public void ZeroWeightLeavesPose()
        {
            Vector3 before = _end.position;

            TwoBoneIK.Solve(_upper, _lower, _end, new Vector3(0.3f, 0.3f, 0f), Vector3.forward, 0f);

            Assert.AreEqual(before, _end.position);
        }

        [Test]
        public void SoftDistanceNeverExceedsLength()
        {
            Assert.AreEqual(0.5f, TwoBoneIK.SoftDistance(0.5f, 1f, 0.05f), 1e-6f);
            Assert.Less(TwoBoneIK.SoftDistance(1f, 1f, 0.05f), 1f);
            Assert.LessOrEqual(TwoBoneIK.SoftDistance(10f, 1f, 0.05f), 1f);
        }
    }
}

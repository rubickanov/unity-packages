using NUnit.Framework;
using Rubickanov.Kinematics;
using UnityEngine;

namespace Rubickanov.Rig.Tests
{
    /// <summary>
    /// The float boundary to the kinematics package: the two-bone solve on transforms reaches the target with the
    /// end's world rotation kept, and a float spring steps as the package's double one does.
    /// </summary>
    public sealed class KinematicsInteropTests
    {
        [Test]
        public void Solve_ReachesTheTargetAndKeepsTheEndsRotation()
        {
            var root = new GameObject("Upper");
            try
            {
                Transform upper = root.transform;
                Transform lower = new GameObject("Lower").transform;
                Transform end = new GameObject("End").transform;
                lower.SetParent(upper, false);
                end.SetParent(lower, false);
                lower.localPosition = new Vector3(0f, -0.45f, 0f);
                end.localPosition = new Vector3(0f, -0.42f, 0f);
                end.rotation = Quaternion.Euler(10f, 20f, 30f);
                Quaternion endRotation = end.rotation;
                var target = new Vector3(0.1f, -0.7f, 0.2f);

                KinematicsInterop.Solve(upper, lower, end, target, new Vector3(0f, -0.4f, 1f), 1f);

                Assert.That(Vector3.Distance(end.position, target), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(end.rotation, endRotation), Is.LessThan(0.01f));
                Assert.That(lower.position.z, Is.GreaterThan(0f), "the knee bends toward the hint");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FloatSpring_StepsAsTheDoubleOne()
        {
            var step = new SpringStep(new SpringSettings(3.0, 0.7), 1.0 / 60.0);
            float value = 0f, rate = 0f;
            double expected = 0.0, expectedRate = 0.0;
            for (int i = 0; i < 30; i++)
            {
                step.Apply(ref value, ref rate, 1f);
                step.Apply(ref expected, ref expectedRate, 1.0);
            }

            Assert.That(value, Is.EqualTo(expected).Within(1e-5));
            Assert.That(rate, Is.EqualTo(expectedRate).Within(1e-4));
        }
    }
}

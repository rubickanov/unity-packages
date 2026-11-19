using System;
using NUnit.Framework;

namespace Rubickanov.Kinematics.Tests
{
    /// <summary>The exact-operation functions against the platform's own, over the ranges the pose maths uses.</summary>
    public sealed class DetMathTests
    {
        [Test]
        public void SineAndCosineMatchThePlatform()
        {
            for (double x = -60.0; x <= 60.0; x += 0.0137)
            {
                DetMath.SinCos(x, out double s, out double c);
                Assert.AreEqual(Math.Sin(x), s, 2e-15, $"sin {x}");
                Assert.AreEqual(Math.Cos(x), c, 2e-15, $"cos {x}");
            }
        }

        [Test]
        public void ArctangentMatchesThePlatform()
        {
            for (double y = -3.0; y <= 3.0; y += 0.0731)
            {
                for (double x = -3.0; x <= 3.0; x += 0.0677)
                {
                    Assert.AreEqual(Math.Atan2(y, x), DetMath.Atan2(y, x), 2e-15, $"atan2 {y} {x}");
                }
            }

            Assert.AreEqual(Math.PI / 2, DetMath.Atan2(1.0, 0.0), 1e-16);
            Assert.AreEqual(-Math.PI / 2, DetMath.Atan(double.NegativeInfinity), 1e-16);
            Assert.AreEqual(Math.Atan(1e9), DetMath.Atan(1e9), 2e-15);
            Assert.AreEqual(Math.Atan(1e-9), DetMath.Atan(1e-9), 1e-24);
        }

        [Test]
        public void ArcsineAndArccosineMatchThePlatform()
        {
            for (double x = -1.0; x <= 1.0; x += 0.00731)
            {
                Assert.AreEqual(Math.Asin(x), DetMath.Asin(x), 4e-15, $"asin {x}");
                Assert.AreEqual(Math.Acos(x), DetMath.Acos(x), 4e-15, $"acos {x}");
            }

            Assert.AreEqual(0.0, DetMath.Acos(1.0));
            Assert.AreEqual(Math.PI, DetMath.Acos(-1.0), 1e-15);
            Assert.AreEqual(0.0, DetMath.Acos(1.5));
        }

        [Test]
        public void ExponentMatchesThePlatform()
        {
            for (double x = -700.0; x <= 700.0; x += 0.917)
            {
                double expected = Math.Exp(x);
                Assert.AreEqual(expected, DetMath.Exp(x), expected * 4e-15, $"exp {x}");
            }

            Assert.AreEqual(1.0, DetMath.Exp(0.0));
            Assert.AreEqual(0.0, DetMath.Exp(-1000.0));
            Assert.AreEqual(double.PositiveInfinity, DetMath.Exp(1000.0));
        }

        [Test]
        public void WrapsAnglesLikeUnity()
        {
            Assert.AreEqual(-20.0, DetMath.DeltaAngle(170.0, 150.0), 1e-12);
            Assert.AreEqual(20.0, DetMath.DeltaAngle(170.0, -170.0), 1e-12);
            Assert.AreEqual(180.0, DetMath.DeltaAngle(0.0, 180.0), 1e-12);
            Assert.AreEqual(185.0, DetMath.LerpAngle(170.0, -160.0, 0.5), 1e-12);
            Assert.AreEqual(10.0, DetMath.Repeat(370.0, 360.0), 1e-12);
            Assert.AreEqual(350.0, DetMath.Repeat(-10.0, 360.0), 1e-12);
        }
    }
}

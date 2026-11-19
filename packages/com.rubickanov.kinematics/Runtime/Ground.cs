using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>The ground found under a point: where its surface is and which way it faces.</summary>
    public struct GroundSample
    {
        public double3 Point;

        /// <summary>The surface's outward normal, unit length.</summary>
        public double3 Normal;

        public GroundSample(double3 point, double3 normal)
        {
            Point = point;
            Normal = normal;
        }
    }

    /// <summary>
    /// Finds the ground under a point, for the pose maths to stand a foot or a hand on. A struct implementing it is
    /// passed by type (<c>where T : struct, IGroundProbe</c>), so a Burst job calls it without boxing.
    /// </summary>
    public interface IGroundProbe
    {
        /// <summary>
        /// The first surface met going down <paramref name="up"/> from <paramref name="above"/> metres over
        /// <paramref name="point"/> to <paramref name="below"/> metres under it (<paramref name="up"/> of length 1);
        /// false when there is none.
        /// </summary>
        bool Ground(double3 point, double3 up, double above, double below, out GroundSample ground);
    }

    /// <summary>
    /// A ground probe that also looks under many points in one go (one batch of world queries for all of them): each
    /// answer is the one <see cref="IGroundProbe.Ground"/> gives for that point alone.
    /// </summary>
    public interface IGroundBatch : IGroundProbe
    {
        /// <summary><see cref="IGroundProbe.Ground"/> for the first <paramref name="count"/> of
        /// <paramref name="points"/>, into <paramref name="found"/> and <paramref name="grounds"/>.</summary>
        void Ground(double3[] points, int count, double3 up, double above, double below, bool[] found,
            GroundSample[] grounds);
    }

    /// <summary>Level ground at one height along up: for tests and for a body standing on a plain floor.</summary>
    public struct FlatGround : IGroundProbe
    {
        public double Height;

        public FlatGround(double height) => Height = height;

        public bool Ground(double3 point, double3 up, double above, double below, out GroundSample ground)
        {
            double offset = math.dot(point, up) - Height;
            if (offset < -above || offset > below)
            {
                ground = default;
                return false;
            }

            ground = new GroundSample(point - up * offset, up);
            return true;
        }
    }
}

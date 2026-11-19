using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>How a foot rests on the ground found under it (<see cref="SoleContact.Place{T}"/>).</summary>
    public struct SolePlacement
    {
        /// <summary>The ground under the ankle's sole, metres along up from the origin given.</summary>
        public double Height;

        /// <summary>The sole's pitch, degrees, positive with the ball up.</summary>
        public double Pitch;

        /// <summary>The sole's roll, degrees, positive with its right edge up.</summary>
        public double Roll;

        /// <summary>A step edge rather than a slope: the foot stands level on the higher side.</summary>
        public bool OnEdge;

        /// <summary>The foot's heading across up, unit length; zero for a foot with no length.</summary>
        public double3 Forward;

        /// <summary>The world turn that tips a level sole onto the ground: pitch about its side, then roll about its
        /// heading.</summary>
        public readonly double4 Tilt(double3 up)
        {
            if (math.all(Forward == 0.0))
            {
                return Rotation.Identity;
            }

            double3 side = math.cross(up, Forward);
            return Rotation.Mul(Rotation.AxisAngle(Forward, Roll * DetMath.Deg2Rad),
                Rotation.AxisAngle(side, -Pitch * DetMath.Deg2Rad));
        }
    }

    /// <summary>
    /// How a planted foot rests on the ground found under its ankle and under its ball. Ground that slopes between
    /// the two no steeper than the foot may tilt is a slope, and the sole lies along it. Anything steeper is a step
    /// edge: the foot stays level on the higher side, the other end over the edge.
    /// </summary>
    public static class SoleContact
    {
        /// <summary>
        /// Heights are along up from the character's base, given only where something is under that point. Length is
        /// the level distance from the ankle to the ball. Gives the height of the ground under the ankle's sole and
        /// the pitch of the sole in degrees, positive with the ball up. False when there is no ground under either
        /// point.
        /// </summary>
        public static bool Fit(bool hasAnkle, double ankle, bool hasBall, double ball, double length, double maxAngle,
            out double height, out double pitch) =>
            Fit(hasAnkle, ankle, hasBall, ball, length, maxAngle, out height, out pitch, out _);

        private static bool Fit(bool hasAnkle, double ankle, bool hasBall, double ball, double length,
            double maxAngle, out double height, out double pitch, out bool slope)
        {
            height = 0.0;
            pitch = 0.0;
            slope = false;
            if (hasAnkle && hasBall && length > 1e-3)
            {
                double angle = DetMath.Atan2(ball - ankle, length) * DetMath.Rad2Deg;
                if (math.abs(angle) <= maxAngle)
                {
                    height = ankle;
                    pitch = angle;
                    slope = true;
                    return true;
                }
            }

            if (!hasAnkle && !hasBall)
            {
                return false;
            }

            height = hasAnkle && hasBall ? math.max(ankle, ball) : hasAnkle ? ankle : ball;
            return true;
        }

        /// <summary>
        /// Looks for the ground under the animated <paramref name="ankle"/> and <paramref name="ball"/>, from
        /// <paramref name="above"/> metres over each to <paramref name="below"/> under, and rests the sole on it as
        /// <see cref="Fit"/> does. Heights are measured along <paramref name="up"/> (length 1) from
        /// <paramref name="origin"/>. The roll follows the ground's normal where the foot rests, when that is no
        /// steeper across the foot than <paramref name="maxAngle"/>. False when there is no ground under either.
        /// </summary>
        public static bool Place<T>(ref T ground, double3 ankle, double3 ball, double3 up, double3 origin,
            double above, double below, double maxAngle, out SolePlacement placement) where T : struct, IGroundProbe
        {
            placement = default;
            double3 level = DetMath.ProjectOnPlane(ball - ankle, up);
            double length = DetMath.Length(level);
            bool hasAnkle = ground.Ground(ankle, up, above, below, out GroundSample underAnkle);
            bool hasBall = ground.Ground(ball, up, above, below, out GroundSample underBall);
            double ankleHeight = hasAnkle ? math.dot(underAnkle.Point - origin, up) : 0.0;
            double ballHeight = hasBall ? math.dot(underBall.Point - origin, up) : 0.0;
            if (!Fit(hasAnkle, ankleHeight, hasBall, ballHeight, length, maxAngle, out double height,
                    out double pitch, out bool slope))
            {
                return false;
            }

            bool restsOnBall = !slope && hasBall && (!hasAnkle || ballHeight > ankleHeight);
            double3 normal = restsOnBall ? underBall.Normal : underAnkle.Normal;
            placement.Height = height;
            placement.Pitch = pitch;
            placement.OnEdge = !slope && hasAnkle && hasBall && ankleHeight != ballHeight;
            placement.Forward = length > 1e-3 ? level / length : double3.zero;
            if (length > 1e-3)
            {
                double3 side = math.cross(up, placement.Forward);
                double roll = DetMath.Atan2(-math.dot(normal, side), math.dot(normal, up)) * DetMath.Rad2Deg;
                placement.Roll = math.abs(roll) <= maxAngle ? roll : 0.0;
            }

            return true;
        }
    }
}

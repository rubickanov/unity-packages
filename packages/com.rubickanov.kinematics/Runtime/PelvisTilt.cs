using System;
using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    [Serializable]
    public struct PelvisTiltSettings
    {
        /// <summary>Distance between the hip joints, metres.</summary>
        public double HipWidth;

        /// <summary>How much of the slope between the feet the pelvis takes up by rolling, 0 to 1; the knee over the
        /// higher foot bends for the rest.</summary>
        public double RollShare;

        /// <summary>Most the pelvis rolls, degrees.</summary>
        public double MaxRoll;

        /// <summary>Most the pelvis drops below the animation's, metres.</summary>
        public double MaxDrop;

        public PelvisTiltSettings(double hipWidth, double rollShare, double maxRoll, double maxDrop)
        {
            HipWidth = hipWidth;
            RollShare = rollShare;
            MaxRoll = maxRoll;
            MaxDrop = maxDrop;
        }
    }

    /// <summary>
    /// How the pelvis follows two feet standing on ground at different heights: it drops (or rises) so the leg over
    /// the lower ground keeps the animation's length, and rolls so the hip over the higher ground comes up, never so
    /// far that the higher leg would have to stretch.
    /// </summary>
    public struct PelvisTilt
    {
        /// <summary>How far to move the pelvis along up, metres.</summary>
        public double Lift;

        /// <summary>The pelvis's roll about the body's forward axis, degrees, positive with the right hip up.</summary>
        public double Roll;

        /// <summary>
        /// <paramref name="leftGround"/> and <paramref name="rightGround"/> are how far the ground under each foot is
        /// above the ground the animation stands on, metres along up.
        /// </summary>
        public static PelvisTilt Solve(double leftGround, double rightGround, in PelvisTiltSettings settings)
        {
            double low = math.min(leftGround, rightGround);
            double rise = math.abs(rightGround - leftGround);
            double roll = 0.0;
            if (settings.HipWidth > 0.0)
            {
                roll = DetMath.Clamp01(settings.RollShare) * DetMath.Atan2(rise, settings.HipWidth) * DetMath.Rad2Deg;
                roll = math.min(roll, math.max(settings.MaxRoll, 0.0));
            }

            double halfRise = 0.5 * settings.HipWidth * DetMath.Sin(roll * DetMath.Deg2Rad);
            return new PelvisTilt
            {
                Lift = math.max(low + halfRise, -math.max(settings.MaxDrop, 0.0)),
                Roll = rightGround >= leftGround ? roll : -roll
            };
        }

        /// <summary>The roll as a world turn about the body's <paramref name="forward"/>.</summary>
        public readonly double4 Turn(double3 forward) => Rotation.AxisAngle(forward, Roll * DetMath.Deg2Rad);
    }
}

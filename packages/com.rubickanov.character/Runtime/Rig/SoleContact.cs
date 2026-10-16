using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>
    /// How a planted foot rests on the ground found under its ankle and under its ball. Ground that
    /// slopes between the two no steeper than the foot may tilt is a slope, and the sole lies along it.
    /// Anything steeper is a step edge: the foot stays level on the higher side, the other end over the edge.
    /// </summary>
    public static class SoleContact
    {
        /// <summary>
        /// Heights are along up from the character's base, null where nothing is under that point.
        /// Length is the level distance from the ankle to the ball. Gives the height of the ground under
        /// the ankle's sole and the pitch of the sole in degrees, positive with the ball up.
        /// Returns false when there is no ground under either point.
        /// </summary>
        public static bool Fit(float? ankle, float? ball, float length, float maxAngle,
            out float height, out float pitch)
        {
            height = 0f;
            pitch = 0f;
            if (ankle is float a && ball is float b && length > 1e-3f)
            {
                float angle = Mathf.Atan2(b - a, length) * Mathf.Rad2Deg;
                if (Mathf.Abs(angle) <= maxAngle)
                {
                    height = a;
                    pitch = angle;
                    return true;
                }
            }

            if (ankle == null && ball == null)
            {
                return false;
            }

            height = Mathf.Max(ankle ?? float.NegativeInfinity, ball ?? float.NegativeInfinity);
            return true;
        }
    }
}

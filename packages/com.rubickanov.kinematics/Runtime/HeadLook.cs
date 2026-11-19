using System;
using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>How far one neck joint turns from its rest, degrees: to the right and left, bent down (flexion) and
    /// up (extension). All four are positive.</summary>
    [Serializable]
    public struct LookRange
    {
        public double Right;
        public double Left;
        public double Down;
        public double Up;

        public LookRange(double right, double left, double down, double up)
        {
            Right = right;
            Left = left;
            Down = down;
            Up = up;
        }
    }

    /// <summary>
    /// A head turned toward a look as two joints: the lower neck in the chest's axes and the upper neck (the head)
    /// in the neck's. Each is a bend about its parent's X axis then a turn about its own long Y axis, the order the
    /// body reads its joints in, so <see cref="LowerFlexion"/> and <see cref="LowerTurn"/> are its flexion and its
    /// rotation (positive down and to the right) with no sideways bend.
    /// </summary>
    public struct NeckPose
    {
        public double4 Lower;
        public double4 Upper;
        public double LowerFlexion;
        public double LowerTurn;
        public double UpperFlexion;
        public double UpperTurn;

        /// <summary>Where the head looks, in the chest's axes: the look asked for, held within reach.</summary>
        public AimAngles Aim;
    }

    /// <summary>
    /// Turns the head toward a point, shared between the lower and upper neck within each joint's range. Past the
    /// neck's reach the look holds at the limit rather than easing back. Axes: X right, Y up, Z forward, the body
    /// facing Z at rest.
    /// </summary>
    public static class HeadLook
    {
        /// <summary>
        /// The neck that turns the eyes at <paramref name="eyes"/> toward <paramref name="target"/>, the chest
        /// turned by <paramref name="chest"/> in the world.
        /// </summary>
        public static NeckPose Toward(double4 chest, double3 eyes, double3 target, in LookRange lower,
            in LookRange upper) =>
            Solve(AimAngles.FromLocal(Rotation.Rotate(Rotation.Inverse(chest), target - eyes)), lower, upper);

        /// <summary>The neck for a look given as angles in the chest's axes.</summary>
        public static NeckPose Solve(AimAngles look, in LookRange lower, in LookRange upper)
        {
            AimAngles held = look.Limit(lower.Right + upper.Right, lower.Left + upper.Left, lower.Up + upper.Up,
                lower.Down + upper.Down);

            // The lower neck takes its share of the look by its range in that direction; the head finishes it.
            double yawShare = held.Yaw >= 0.0 ? Share(lower.Right, upper.Right) : Share(lower.Left, upper.Left);
            double pitchShare = held.Pitch >= 0.0 ? Share(lower.Up, upper.Up) : Share(lower.Down, upper.Down);
            var pose = new NeckPose
            {
                LowerTurn = DetMath.Clamp(held.Yaw * yawShare, -lower.Left, lower.Right),
                LowerFlexion = DetMath.Clamp(-held.Pitch * pitchShare, -lower.Up, lower.Down)
            };
            pose.Lower = Joint(pose.LowerFlexion, pose.LowerTurn);
            double3 rest = Rotation.Rotate(Rotation.Inverse(pose.Lower), held.Direction());
            Angles(rest, out double flexion, out double turn);
            pose.UpperFlexion = DetMath.Clamp(flexion, -upper.Up, upper.Down);
            pose.UpperTurn = DetMath.Clamp(turn, -upper.Left, upper.Right);
            pose.Upper = Joint(pose.UpperFlexion, pose.UpperTurn);
            pose.Aim = AimAngles.FromLocal(Rotation.Rotate(Rotation.Mul(pose.Lower, pose.Upper),
                new double3(0.0, 0.0, 1.0)));
            return pose;
        }

        /// <summary>A joint bent by <paramref name="flexion"/> about its parent's X, then turned by
        /// <paramref name="turn"/> about its own Y, degrees.</summary>
        public static double4 Joint(double flexion, double turn) =>
            Rotation.Mul(Rotation.AxisAngle(new double3(1.0, 0.0, 0.0), flexion * DetMath.Deg2Rad),
                Rotation.AxisAngle(new double3(0.0, 1.0, 0.0), turn * DetMath.Deg2Rad));

        /// <summary>The bend and turn, degrees, that point a joint's forward along <paramref name="direction"/>.</summary>
        public static void Angles(double3 direction, out double flexion, out double turn)
        {
            double3 d = DetMath.Normalize(direction);
            turn = DetMath.Asin(d.x) * DetMath.Rad2Deg;
            flexion = DetMath.Atan2(-d.y, d.z) * DetMath.Rad2Deg;
        }

        private static double Share(double lower, double upper)
        {
            double sum = lower + upper;
            return sum > 0.0 ? lower / sum : 0.0;
        }
    }
}

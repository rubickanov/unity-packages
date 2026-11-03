using KinematicCharacterController;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>How <see cref="GetUpSpot.Find"/> looks for a place to stand.</summary>
    public struct GetUpSpotSettings
    {
        /// <summary>Rings round the first spot are this far apart, m.</summary>
        public float Step;

        /// <summary>How many rings of <see cref="GetUpSpot.Directions"/> points are tried.</summary>
        public int Rings;

        /// <summary>How many times a capsule put into something is moved out of it.</summary>
        public int FreeTries;

        /// <summary>The standing capsule and the head may be this deep in a fixed piece, m.</summary>
        public float PieceSlop;

        /// <summary>Radius of the lying head swept up for a piece over it, m; 0 skips the sweep.</summary>
        public float HeadRadius;
    }

    /// <summary>
    /// Where a body lying down can stand up: the capsule at the first spot or the nearest point round it, in rings,
    /// where it stands on stable ground (not on a step's nose, where it would slide off), out of what it was put in,
    /// with room for the standing capsule and nothing fixed over the lying head that standing up would pass through.
    /// Queries go through the character's KCC motor, so ground counts as the motor would count it.
    /// </summary>
    public static class GetUpSpot
    {
        /// <summary>Points on each ring.</summary>
        public const int Directions = 8;

        // A capsule moved out of something less than this has come free, m.
        private const float FreeStill = 0.001f;

        // The ground under a ring point is looked for this far above and below it, m.
        private const float GroundAbove = 0.6f;
        private const float GroundBelow = 0.9f;

        private static readonly ProfilerMarker FindMarker = new("GetUpSpot.Find");
        private static readonly Collider[] Overlaps = new Collider[16];
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>
        /// The capsule's place nearest <paramref name="feet"/> where it stands upright facing
        /// <paramref name="heading"/>, or false where no point of the rings has one. <paramref name="lyingHead"/> is
        /// where the head lies: a fixed piece over it lower than the standing head's top rules a point out. The
        /// motor is left as it was; its ground solving must be on (KCC finds no ground stable without it), and
        /// colliders it must not see (the body's own bones) go into its controller's ignore list first.
        /// </summary>
        public static bool Find(KinematicCharacterMotor motor, Vector3 feet, Quaternion heading, Vector3 lyingHead,
            in GetUpSpotSettings settings, out Vector3 spot)
        {
            using ProfilerMarker.AutoScope scope = FindMarker.Auto();
            Vector3 up = heading * Vector3.up;
            // KCC judges a ground hit with the motor's own rotation, speed and last grounding (a step's denivelation
            // against the ground it stood on): an upright, still capsule with no ground before it, the same for every
            // point.
            Quaternion rotation = motor.TransientRotation;
            CharacterTransientGroundingReport last = motor.LastGroundingStatus;
            Vector3 velocity = motor.BaseVelocity;
            // PhysX finds no penetration for a disabled collider, and a body that is down has its capsule off.
            bool enabled = motor.Capsule.enabled;
            motor.Capsule.enabled = true;
            motor.SetRotation(heading);
            motor.BaseVelocity = Vector3.zero;
            motor.LastGroundingStatus = new CharacterTransientGroundingReport();
            try
            {
                if (StandsAt(motor, feet, heading, up, lyingHead, settings, out spot))
                {
                    return true;
                }

                Quaternion round = Quaternion.FromToRotation(Vector3.up, up);
                for (int ring = 1; ring <= settings.Rings; ring++)
                {
                    for (int k = 0; k < Directions; k++)
                    {
                        float angle = k * (2f * Mathf.PI / Directions);
                        Vector3 at = feet + round * new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) *
                            (settings.Step * ring);
                        if (StandsAt(motor, at, heading, up, lyingHead, settings, out spot))
                        {
                            return true;
                        }
                    }
                }

                spot = feet;
                return false;
            }
            finally
            {
                motor.SetRotation(rotation);
                motor.Capsule.enabled = enabled;
                motor.BaseVelocity = velocity;
                motor.LastGroundingStatus = last;
            }
        }

        // The capsule put on the ground at a point and moved out of what it is in: stable there, with room to stand
        // and nothing over the lying head.
        private static bool StandsAt(KinematicCharacterMotor motor, Vector3 at, Quaternion heading, Vector3 up,
            Vector3 lyingHead, in GetUpSpotSettings settings, out Vector3 spot)
        {
            spot = Free(motor, GroundUnder(motor, at, up), heading, up, settings.FreeTries);
            var report = new CharacterGroundingReport();
            Vector3 probed = spot + up * KinematicCharacterMotor.CollisionOffset;
            motor.ProbeGround(ref probed, heading, Mathf.Max(motor.Capsule.radius, motor.MaxStepHeight), ref report);
            // On the ground and not only held by an edge it leans over.
            if (!report.IsStableOnGround || report.SnappingPrevented)
            {
                return false;
            }

            spot = probed;
            return RoomToStand(motor, spot, heading, settings.PieceSlop) &&
                   !HeadUnderPiece(motor, lyingHead, spot, up, settings);
        }

        // Moved out of what it was put in, as the solver's depenetration would over its first steps, each time set
        // back on the ground, until it moves less than FreeStill.
        private static Vector3 Free(KinematicCharacterMotor motor, Vector3 feet, Quaternion heading, Vector3 up,
            int tries)
        {
            for (int i = 0; i < tries; i++)
            {
                Vector3 moved = feet;
                int count = motor.CharacterCollisionsOverlap(moved, heading, Overlaps);
                for (int j = 0; j < count; j++)
                {
                    Collider other = Overlaps[j];
                    Transform pose = other.transform;
                    if (Physics.ComputePenetration(motor.Capsule, moved, heading, other, pose.position, pose.rotation,
                            out Vector3 direction, out float distance))
                    {
                        moved += direction * (distance + KinematicCharacterMotor.CollisionOffset);
                    }
                }

                if ((moved - feet).sqrMagnitude < FreeStill * FreeStill)
                {
                    break;
                }

                feet = GroundUnder(motor, moved, up);
            }

            return feet;
        }

        // The nearest ground the motor collides with under a point, looked for from a little above it; the point
        // itself where there is none.
        private static Vector3 GroundUnder(KinematicCharacterMotor motor, Vector3 at, Vector3 up)
        {
            int count = Physics.RaycastNonAlloc(at + up * GroundAbove, -up, Hits, GroundAbove + GroundBelow,
                motor.CollidableLayers & motor.StableGroundLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Vector3 ground = at;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.distance > 0f && hit.distance < nearest && Collides(motor, hit.collider))
                {
                    nearest = hit.distance;
                    ground = hit.point;
                }
            }

            return ground;
        }

        // The standing capsule there overlaps nothing it collides with deeper than the slop.
        private static bool RoomToStand(KinematicCharacterMotor motor, Vector3 feet, Quaternion heading, float slop) =>
            motor.CharacterCollisionsOverlap(feet, heading, Overlaps, -slop) == 0;

        // A fixed piece over the lying head lower than the standing head's top: getting up would pass the head
        // through it.
        private static bool HeadUnderPiece(KinematicCharacterMotor motor, Vector3 head, Vector3 feet, Vector3 up,
            in GetUpSpotSettings settings)
        {
            float radius = settings.HeadRadius - settings.PieceSlop;
            float top = Vector3.Dot(feet - head, up) + motor.Capsule.height;
            float reach = top - radius;
            if (radius <= 0f || reach <= 0f)
            {
                return false;
            }

            int count = Physics.SphereCastNonAlloc(head, radius, up, Hits, reach, motor.CollidableLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                Rigidbody body = hit.collider.attachedRigidbody;
                // Started inside: the piece the head lies in, not one over it.
                if (hit.distance > 0f && (body == null || body.isKinematic) && Collides(motor, hit.collider))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Collides(KinematicCharacterMotor motor, Collider collider) =>
            collider != motor.Capsule &&
            (motor.CharacterController == null || motor.CharacterController.IsColliderValidForCollisions(collider));
    }
}

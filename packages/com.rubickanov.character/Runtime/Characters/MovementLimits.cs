using System;
using System.Collections.Generic;

namespace Rubickanov.Characters
{
    /// <summary>Limits on motor intent; external velocity, gravity and mount control remain independent.</summary>
    public readonly struct MovementLimits
    {
        public readonly float MaxSpeed;
        public readonly bool Sprint, Jump, TurnBody;
        public static MovementLimits Unlimited => new(float.PositiveInfinity, true, true, true);
        public MovementLimits(float maxSpeed, bool sprint, bool jump, bool turnBody)
        {
            if (float.IsNaN(maxSpeed) || maxSpeed < 0) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            MaxSpeed = maxSpeed; Sprint = sprint; Jump = jump; TurnBody = turnBody;
        }
        public MovementLimits Intersect(in MovementLimits other) => new(Math.Min(MaxSpeed, other.MaxSpeed),
            Sprint && other.Sprint, Jump && other.Jump, TurnBody && other.TurnBody);
    }

    /// <summary>Scoped contributions. Disposing a hand action cannot release a seat or another action.</summary>
    public sealed class MovementConstraints
    {
        private sealed class Lease : IDisposable
        {
            private readonly MovementConstraints _owner;
            internal readonly MovementLimits Limits;
            internal Lease(MovementConstraints owner, MovementLimits limits) { _owner = owner; Limits = limits; }
            public void Dispose() => _owner._leases.Remove(this);
        }
        private readonly HashSet<Lease> _leases = new();
        public MovementLimits Current
        {
            get
            {
                var result = MovementLimits.Unlimited;
                foreach (var lease in _leases) result = result.Intersect(lease.Limits);
                return result;
            }
        }
        public IDisposable Add(MovementLimits limits)
        {
            var lease = new Lease(this, limits); _leases.Add(lease); return lease;
        }
    }
}

using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// External systems (weapons, explosions, AI) use this to influence the motor.
    /// </summary>
    public interface IForceReceiver
    {
        void AddExternalForce(Vector3 force);
        void SetSpeedModifier(object source, float multiplier);
        void RemoveSpeedModifier(object source);
    }
}

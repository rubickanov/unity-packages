namespace Rubickanov.Motor
{
    public enum MotorBodyType
    {
        /// <summary>
        /// CapsuleCast-based kinematic body. Deterministic, suitable for multiplayer
        /// with prediction and reconciliation.
        /// </summary>
        Kinematic,

        /// <summary>
        /// Unity Rigidbody-based body. Uses AddForce for physics.
        /// Simple setup for singleplayer prototypes.
        /// </summary>
        Rigidbody
    }
}

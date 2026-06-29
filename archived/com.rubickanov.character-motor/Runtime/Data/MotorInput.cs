using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Serializable input struct. For singleplayer, built by <see cref="CharacterMotor"/>
    /// from <see cref="IMotorInputProvider"/>. For multiplayer, built by the game from
    /// its own network input command.
    /// </summary>
    public struct MotorInput
    {
        public Vector2 Move;
        public bool Jump;
        public bool Sprint;
        public bool Crouch;
        public InputExtensions? Extensions;
    }
}

using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Input provider for singleplayer auto-tick mode.
    /// <see cref="CharacterMotor"/> reads this and builds <see cref="MotorInput"/> each frame.
    /// <para>
    /// <b>Contract:</b> <see cref="JumpPressed"/> and <see cref="CrouchPressed"/>
    /// must be single-frame pulses (true only on the frame the button was pressed).
    /// <see cref="SprintHeld"/> is a continuous hold state.
    /// </para>
    /// </summary>
    public interface IMotorInputProvider
    {
        Vector2 MoveInput { get; }
        bool JumpPressed { get; }
        bool SprintHeld { get; }
        bool CrouchPressed { get; }
    }
}

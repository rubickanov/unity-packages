namespace Rubickanov.Characters
{
    /// <summary>
    /// Whoever controls a character: the local player's input, a bot's brain. A character without one (a copy driven
    /// over the network) gets no input.
    /// </summary>
    public interface ICharacterInputSource
    {
        /// <summary>The intentions for this frame. Read once a frame by the <see cref="CharacterMotor"/> it controls.</summary>
        CharacterInput ReadInput();
    }
}

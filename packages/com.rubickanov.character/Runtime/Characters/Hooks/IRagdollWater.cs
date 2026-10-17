namespace Rubickanov.Characters
{
    /// <summary>What the water does to a downed body this physics step, as the game decides it.</summary>
    public enum RagdollWaterStep
    {
        /// <summary>The ragdoll floats and drags in the water itself, as without a hook.</summary>
        Float,

        /// <summary>The game pushes the bones in the water: no float or drag, the bones count as wet.</summary>
        Pushed,

        /// <summary>The game carries the body over the water: no float or drag, the bones count as dry.</summary>
        Carried,
    }

    /// <summary>
    /// The game's say over a downed body at the water, beside <see cref="ICharacterWater"/>, such as a body skipping
    /// over the sea. Set on <see cref="CharacterRagdoll.WaterHook"/>; asked only where the ragdoll is simulated.
    /// </summary>
    public interface IRagdollWater
    {
        /// <summary>
        /// Each physics step the body is down, before its float. A step other than <see cref="RagdollWaterStep.Float"/>
        /// does not count as floating and ends the fall's stronger gravity, as reaching the water does.
        /// </summary>
        RagdollWaterStep Step(CharacterRagdoll ragdoll, float dt);
    }
}

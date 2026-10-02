namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Contract for per-frame (or per-simulation-step) logic attached to an
    /// entity. Driven in-editor by <see cref="EntityTickRunner"/>, which calls
    /// <see cref="Tick"/> once per <c>Update</c> with the frame delta time.
    /// In a headless simulation (console host, fixed-step server) the same
    /// implementation is fed by a custom loop that passes its own <c>dt</c> —
    /// the logic code is identical on both sides.
    /// </summary>
    public interface ITickable
    {
        /// <summary>
        /// Advances the logic by <paramref name="dt"/> seconds. In Unity
        /// <paramref name="dt"/> is <c>Time.deltaTime</c>; in headless mode it
        /// is the simulation tick interval.
        /// </summary>
        void Tick(float dt);
    }
}

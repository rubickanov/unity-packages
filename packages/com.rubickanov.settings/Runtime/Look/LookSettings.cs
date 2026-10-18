using System;
using UnityEngine;

namespace Rubickanov.Settings
{
    /// <summary>
    /// The ready-made look settings: mouse and stick speed as steps 0–10 (5 is the game as authored) and invert Y. Ids
    /// are <c>prefix.mouse</c>, <c>prefix.pad</c>, <c>prefix.invertY</c>. The game's camera applies them
    /// (<see cref="Scale"/> times its own speed).
    /// </summary>
    public sealed class LookSettings
    {
        public const int MiddleStep = 5;

        public LookSettings(SettingsStore store, string prefix = "look")
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            Mouse = store.Steps(prefix + ".mouse", MiddleStep);
            Pad = store.Steps(prefix + ".pad", MiddleStep);
            InvertY = store.Toggle(prefix + ".invertY", false);
        }

        public StepSetting Mouse { get; }

        public StepSetting Pad { get; }

        public Setting<bool> InvertY { get; }

        /// <summary>A step as a speed scale: ×1 at 5, about a third more or less each step, ×0.25 at 0 and ×4 at 10.</summary>
        public static float Scale(int step) => Mathf.Pow(2f, (step - MiddleStep) / 2.5f);

        /// <summary>Mouse speed while keys are in hand, stick speed while a pad is, and invert Y.</summary>
        public void AddRows(SectionBuilder section, Func<bool> padInHand)
        {
            if (padInHand == null) throw new ArgumentNullException(nameof(padInHand));
            section.Steps(Mouse).Visible(() => !padInHand());
            section.Steps(Pad).Visible(padInHand);
            section.Toggle(InvertY);
        }
    }
}

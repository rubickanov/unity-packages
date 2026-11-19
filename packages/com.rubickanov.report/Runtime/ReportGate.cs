using Sentry.Unity;
using UnityEngine;

namespace Rubickanov.Report
{
    /// <summary>
    /// Keeps the SDK from starting itself in play: its own start at <c>SubsystemRegistration</c> comes before the game
    /// has read the player's choice, so <see cref="Reporter.Start"/> starts it instead. At build time the options pass
    /// untouched, so the SDK's build steps still see them enabled.
    /// </summary>
    [CreateAssetMenu(menuName = "Rubickanov/Report Gate", fileName = "ReportGate")]
    public sealed class ReportGate : SentryOptionsConfiguration
    {
        public override void Configure(SentryUnityOptions options)
        {
            if (Application.isPlaying)
            {
                options.Enabled = false;
            }
        }
    }
}

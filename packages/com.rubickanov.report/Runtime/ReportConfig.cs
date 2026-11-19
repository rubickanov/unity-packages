using UnityEngine;

namespace Rubickanov.Report
{
    /// <summary>The numbers of the reports: caps a session, the hang's length, the frame rate's window, the shot.</summary>
    [CreateAssetMenu(menuName = "Rubickanov/Report Config", fileName = "ReportConfig")]
    public sealed class ReportConfig : ScriptableObject
    {
        [Tooltip("New kinds of errors sent a session; more are dropped on this machine.")]
        [Min(1)] public int PerSessionCap = 20;

        [Tooltip("Feedbacks sent a session.")]
        [Min(1)] public int FeedbackCap = 10;

        [Tooltip("The main thread standing still this long, in seconds, is a hang.")]
        [Min(1f)] public float HangSeconds = 5f;

        [Tooltip("Seconds between two frame-rate points.")]
        [Min(5f)] public float MetricsInterval = 60f;

        [Tooltip("The end of the log attached to errors and feedback, in bytes.")]
        [Min(1024)] public int LogTailBytes = 65536;

        [Tooltip("The feedback shot's width in pixels.")]
        [Min(64)] public int ShotWidth = 1280;

        [Tooltip("The feedback shot's JPEG quality.")]
        [Range(1, 100)] public int ShotQuality = 75;

        [Tooltip("How long a feedback waits to be sent, in seconds, before it counts as queued.")]
        [Min(0.5f)] public float FlushSeconds = 5f;

        [Tooltip("Where the Editor's and the builds' reports go in Sentry: playtest until the game ships, then release.")]
        public string Environment = ReportEnvironment.Playtest;
    }
}

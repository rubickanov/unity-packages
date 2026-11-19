using UnityEngine;

namespace Rubickanov.Report
{
    /// <summary>The hidden object the report's coroutines run on, kept across scenes. Made by <see cref="Moment"/>.</summary>
    [AddComponentMenu("")]
    public sealed class ReportHost : MonoBehaviour
    {
        private static ReportHost _host;

        internal static ReportHost Get()
        {
            if (_host != null)
            {
                return _host;
            }

            var go = new GameObject("Report Host") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _host = go.AddComponent<ReportHost>();
            return _host;
        }

        private void OnDestroy()
        {
            if (_host == this)
            {
                _host = null;
            }
        }
    }
}

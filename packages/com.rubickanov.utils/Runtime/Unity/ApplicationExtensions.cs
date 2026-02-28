using UnityEditor;

namespace Rubickanov.Utils
{
    /// <summary>Application lifecycle helpers.</summary>
    public static class ApplicationExtensions
    {
        /// <summary>
        /// Quits the application. Stops play mode in the Editor, calls Application.Quit() in builds.
        /// </summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }
    }
}

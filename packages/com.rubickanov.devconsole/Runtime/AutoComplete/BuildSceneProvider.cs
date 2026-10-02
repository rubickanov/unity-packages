using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;

namespace Rubickanov.DevConsole
{
    /// <summary>Suggests the names of the scenes in the build.</summary>
    public class BuildSceneProvider : IAutoCompleteProvider
    {
        public string Hint => "<scene>";

        public void GetSuggestions(string partial, List<string> results)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var name = NameAt(i);
                if (name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(name);
            }
        }

        /// <summary>Name of the scene at <paramref name="buildIndex"/>, its file name without extension.</summary>
        public static string NameAt(int buildIndex)
            => Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(buildIndex));

        /// <summary>Names of all scenes in the build, in build order.</summary>
        public static List<string> Names()
        {
            var names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                names.Add(NameAt(i));
            return names;
        }
    }
}

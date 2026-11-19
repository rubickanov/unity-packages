using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Report.Editor
{
    /// <summary>
    /// The fields of the Sentry wizard's options asset the reporter relies on, set by the game's builder so the asset
    /// never drifts: the gate as its options configuration, hangs on, events from the Editor possible (the game asks for
    /// them itself), no SDK screenshot, stack traces on, no personal data. The DSN and the native flags stay the
    /// wizard's.
    /// </summary>
    public static class SentryOptionsSetup
    {
        /// <summary>Where the Sentry wizard keeps its options; the SDK loads them from Resources by this path.</summary>
        public const string OptionsPath = "Assets/Resources/Sentry/SentryOptions.asset";

        /// <summary>Sets the fields on <paramref name="options"/>; the names of fields the SDK no longer has.</summary>
        public static List<string> Apply(ScriptableObject options, ReportGate gate)
        {
            var missing = new List<string>();
            var serialized = new SerializedObject(options);
            Set(serialized, "OptionsConfiguration", gate, missing);
            Set(serialized, "AnrDetectionEnabled", true, missing);
            Set(serialized, "CaptureInEditor", true, missing);
            Set(serialized, "AttachScreenshot", false, missing);
            Set(serialized, "AttachStacktrace", true, missing);
            Set(serialized, "SendDefaultPii", false, missing);
            Set(serialized, "IsEnvironmentUser", false, missing);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(options);
            return missing;
        }

        private static void Set(SerializedObject serialized, string name, object value, List<string> missing)
        {
            SerializedProperty property = serialized.FindProperty($"<{name}>k__BackingField");
            if (property == null)
            {
                missing.Add(name);
                return;
            }

            switch (value)
            {
                case bool flag:
                    property.boolValue = flag;
                    break;
                case Object reference:
                    property.objectReferenceValue = reference;
                    break;
            }
        }
    }
}

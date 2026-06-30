using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// Single Project Settings panel ("Project / Rubickanov Codegen") listing every registered
    /// generator with its configuration and a per-generator Generate button, plus a top-level
    /// "Generate All" action. Replaces the per-package generator settings providers.
    /// </summary>
    public class CodegenSettingsProvider : SettingsProvider
    {
        private readonly Dictionary<string, bool> _foldouts = new();

        public CodegenSettingsProvider() : base("Project/Rubickanov Codegen", SettingsScope.Project)
        {
            keywords = new[] { "codegen", "generator", "constants", "scenes", "layers", "tags" };
        }

        public override void OnGUI(string searchContext)
        {
            var generators = CodeGeneratorRegistry.All;

            EditorGUILayout.Space(10);

            if (generators.Count == 0)
            {
                EditorGUILayout.HelpBox("No code generators found.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Generate All Enabled", GUILayout.Height(25)))
                GenerateAllEnabled();

            EditorGUILayout.Space(10);

            foreach (var generator in generators)
                DrawGenerator(generator);
        }

        private void DrawGenerator(ICodeGenerator generator)
        {
            var config = CodegenSettings.instance.GetOrCreate(generator);

            _foldouts.TryGetValue(generator.Id, out var expanded);
            expanded = EditorGUILayout.Foldout(expanded, generator.DisplayName, true, EditorStyles.foldoutHeader);
            _foldouts[generator.Id] = expanded;

            if (!expanded)
                return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            config.Enabled = EditorGUILayout.Toggle("Enabled", config.Enabled);
            config.OutputPath = EditorGUILayout.TextField("Output Path", config.OutputPath);
            config.Namespace = EditorGUILayout.TextField("Namespace", config.Namespace);
            config.ClassName = EditorGUILayout.TextField("Class Name", config.ClassName);
            config.Access = (GeneratedAccess)EditorGUILayout.EnumPopup("Access Modifier", config.Access);
            config.MakePartial = EditorGUILayout.Toggle("Partial Class", config.MakePartial);
            config.AutoRegenerate = EditorGUILayout.Toggle("Auto Regenerate", config.AutoRegenerate);

            if (EditorGUI.EndChangeCheck())
                CodegenSettings.instance.Save();

            var invalidPath = string.IsNullOrWhiteSpace(config.OutputPath);
            if (invalidPath)
                EditorGUILayout.HelpBox("Output Path is empty. Generation is disabled until it is set.",
                    MessageType.Warning);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(invalidPath))
            {
                if (GUILayout.Button($"Generate {generator.DisplayName}"))
                    RunGenerator(generator, config);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);
        }

        private static void GenerateAllEnabled()
        {
            foreach (var generator in CodeGeneratorRegistry.All)
            {
                var config = CodegenSettings.instance.GetOrCreate(generator);
                if (config.Enabled && !string.IsNullOrWhiteSpace(config.OutputPath))
                    RunGenerator(generator, config);
            }
        }

        private static void RunGenerator(ICodeGenerator generator, GeneratorConfig config)
        {
            try
            {
                generator.Generate(config);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Codegen] '{generator.DisplayName}' failed: {e}");
            }
        }

        [MenuItem("Tools/Rubickanov/Codegen")]
        private static void Open() => SettingsService.OpenProjectSettings("Project/Rubickanov Codegen");

        [SettingsProvider]
        public static SettingsProvider Create() => new CodegenSettingsProvider();
    }
}

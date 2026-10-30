using UnityEditor;
using UnityEngine;

namespace Rubickanov.Audio.Editor
{
    /// <summary>
    /// The cue's own settings plus a preview without Play Mode: ▶ per variant, ▶ random through <see cref="CuePick"/>,
    /// ■; it plays in 2D through the cue's group with its volume and jitter. Shows the group, the volume in dB and
    /// Unity's priority.
    /// </summary>
    [CustomEditor(typeof(SoundCue))]
    public sealed class SoundCueEditor : UnityEditor.Editor
    {
        private static AudioSource? _preview;
        private CueState _state;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var cue = (SoundCue)target;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                var group = cue.OutputOr(null);
                EditorGUILayout.TextField("Group", group != null ? group.name : "(the service's SFX group)");
                EditorGUILayout.TextField("Volume", cue.Volume > 0f
                    ? $"{20f * Mathf.Log10(cue.Volume):0.0} dB" + (cue.VolumeJitter > 0f ? $" (down to −{cue.VolumeJitter:0.#} dB more)" : "")
                    : "silent");
                EditorGUILayout.IntField("Unity priority", SoundPriority.ToUnity(cue.Priority));
            }

            EditorGUILayout.Space();
            var variants = cue.Variants;
            for (int i = 0; i < variants.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(variants[i] == null))
                        if (GUILayout.Button("▶", GUILayout.Width(28f))) Play(cue, i);
                    EditorGUILayout.LabelField(variants[i] != null ? variants[i].name : "(empty)");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(variants.Length == 0))
                    if (GUILayout.Button("▶ random")) Play(cue, -1);
                if (GUILayout.Button("■", GUILayout.Width(28f))) StopPreview();
            }
        }

        private void OnDisable() => StopPreview();

        private void Play(SoundCue cue, int variant)
        {
            int index = CuePick.Next(cue, ref _state, variant, UnityCueRandom.Shared);
            if (index < 0 || cue.Variants[index] == null) return;

            var source = Preview();
            source.Stop();
            source.resource = cue.Variants[index];
            source.outputAudioMixerGroup = cue.OutputOr(null);
            float db = cue.VolumeJitter > 0f ? -Random.Range(0f, cue.VolumeJitter) : 0f;
            source.volume = cue.Volume * Mathf.Pow(10f, db / 20f);
            source.pitch = cue.PitchJitter > 0f ? 1f + Random.Range(-cue.PitchJitter, cue.PitchJitter) : 1f;
            source.Play();
        }

        private static AudioSource Preview()
        {
            if (_preview != null) return _preview;
            var go = new GameObject("[SoundCuePreview]") { hideFlags = HideFlags.HideAndDontSave };
            _preview = go.AddComponent<AudioSource>();
            _preview.playOnAwake = false;
            _preview.spatialBlend = 0f;
            return _preview;
        }

        private static void StopPreview()
        {
            if (_preview != null) _preview.Stop();
        }
    }
}

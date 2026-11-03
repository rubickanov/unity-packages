using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Rubickanov.Audio.Editor
{
    /// <summary>
    /// Ensures a layout in an existing AudioMixer from code: groups, effects, sends, exposed volumes, a limiter.
    /// Only adds what is missing and sets values only on what it creates, so a second run changes nothing and hand
    /// edits stay. Changes mark the mixer dirty; the caller saves (AssetDatabase.SaveAssets). Works through Unity's internal mixer editor API by reflection; when that changes, it logs what it
    /// could not make and leaves the mixer as it was.
    /// </summary>
    public static class MixerKit
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly EditorAssembly = typeof(UnityEditor.Editor).Assembly;
        private static readonly Type? MixerT = EditorAssembly.GetType("UnityEditor.Audio.AudioMixerController");
        private static readonly Type? GroupT = EditorAssembly.GetType("UnityEditor.Audio.AudioMixerGroupController");
        private static readonly Type? EffectT = EditorAssembly.GetType("UnityEditor.Audio.AudioMixerEffectController");
        private static readonly Type? GroupPathT = EditorAssembly.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        private static readonly Type? EffectPathT = EditorAssembly.GetType("UnityEditor.Audio.AudioEffectParameterPath");
        private static readonly Type? ExposedT = EditorAssembly.GetType("UnityEditor.Audio.ExposedAudioParameter");

        /// <summary>Makes an empty mixer asset (Master only) at the path, replacing nothing that exists.</summary>
        public static AudioMixer? CreateMixer(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
            if (existing != null) return existing;
            return Try("create a mixer at " + path, () =>
                (AudioMixer)MixerT!.GetMethod("CreateMixerControllerAtPath", All)!.Invoke(null, new object[] { path }));
        }

        /// <summary>The group named <paramref name="name"/>, made under <paramref name="parent"/> when missing.</summary>
        public static AudioMixerGroup? EnsureGroup(AudioMixer mixer, string name, string parent)
        {
            var found = Find(mixer, name);
            if (found != null) return found;
            var parentGroup = Find(mixer, parent);
            if (parentGroup == null)
            {
                Debug.LogWarning($"[MixerKit] {mixer.name}: no group {parent} to put {name} under.");
                return null;
            }

            return Try($"add group {name} to {mixer.name}", () =>
            {
                object group = MixerT!.GetMethod("CreateNewGroup", All, null, new[] { typeof(string), typeof(bool) }, null)!
                    .Invoke(mixer, new object[] { name, false });
                MixerT.GetMethod("AddChildToParent", All)!.Invoke(mixer, new[] { group, parentGroup });
                try
                {
                    // Shown in the mixer window's current view; a mixer made in code has no views yet.
                    MixerT.GetMethod("AddGroupToCurrentView", All)?.Invoke(mixer, new[] { group });
                }
                catch (TargetInvocationException) { }
                Save(mixer);
                // Saving may reload the sub-assets: hand out the live one.
                return Find(mixer, name) ?? (AudioMixerGroup)group;
            });
        }

        /// <summary>
        /// Adds the effect before the group's Attenuation when the group has none of that name; sets
        /// <paramref name="values"/> only on an effect it created. True when the group has the effect afterwards.
        /// </summary>
        public static bool EnsureEffect(AudioMixerGroup group, string effect, IReadOnlyDictionary<string, float>? values = null)
        {
            if (FindEffect(group, effect) != null) return true;
            return Try($"add {effect} to {group.name}", () =>
            {
                Insert(group, effect, values);
                return true;
            });
        }

        /// <summary>
        /// A Send on <paramref name="from"/> into a Receive on <paramref name="to"/> (made when missing), at
        /// <paramref name="levelDb"/> when the send is new.
        /// </summary>
        public static bool EnsureSend(AudioMixerGroup from, AudioMixerGroup to, float levelDb)
        {
            if (!EnsureEffect(to, "Receive")) return false;
            object receive = FindEffect(to, "Receive")!;
            foreach (object e in Effects(from))
                if (Name(e) == "Send" && Equals(EffectT!.GetProperty("sendTarget", All)!.GetValue(e), receive)) return true;

            return Try($"add a send {from.name} → {to.name}", () =>
            {
                object send = Insert(from, "Send", null);
                EffectT!.GetProperty("sendTarget", All)!.SetValue(send, receive);
                EffectT.GetMethod("SetValueForMixLevel", All)!.Invoke(send, new[] { from.audioMixer, Snapshot(from.audioMixer), (object)levelDb });
                Save(from.audioMixer);
                return true;
            });
        }

        /// <summary>Exposes the group's volume as <paramref name="parameter"/> unless that name is exposed already.</summary>
        public static bool EnsureExposedVolume(AudioMixerGroup group, string parameter)
        {
            AudioMixer mixer = group.audioMixer;
            if (Exposed(mixer, parameter)) return true;
            return Try($"expose {group.name} volume as {parameter}", () =>
            {
                var guid = (GUID)GroupT!.GetMethod("GetGUIDForVolume", All)!.Invoke(group, null);
                object path = Activator.CreateInstance(GroupPathT!, All, null, new object[] { group, guid }, null);
                return Expose(mixer, guid, path, parameter);
            });
        }

        /// <summary>
        /// A Compressor on the master used as a peak limiter (the mixer Compressor clamps everything above its
        /// threshold: attack 0, no lookahead), its threshold exposed as <paramref name="exposedThreshold"/> so a probe
        /// can switch it off (0 dB). Values are set only when the compressor is new.
        /// </summary>
        public static bool EnsureLimiter(AudioMixerGroup master, float ceilingDb = -1f, float releaseMs = 100f,
            string? exposedThreshold = null)
        {
            var values = new Dictionary<string, float> { ["Threshold"] = ceilingDb, ["Attack"] = 0f, ["Release"] = releaseMs };
            if (!EnsureEffect(master, "Compressor", values)) return false;
            if (string.IsNullOrEmpty(exposedThreshold) || Exposed(master.audioMixer, exposedThreshold!)) return true;

            object compressor = FindEffect(master, "Compressor")!;
            return Try($"expose the limiter threshold as {exposedThreshold}", () =>
            {
                var guid = (GUID)EffectT!.GetMethod("GetGUIDForParameter", All)!.Invoke(compressor, new object[] { "Threshold" });
                object path = Activator.CreateInstance(EffectPathT!, All, null, new[] { master, compressor, (object)guid }, null);
                return Expose(master.audioMixer, guid, path, exposedThreshold!);
            });
        }

        /// <summary>An effect parameter's value in the mixer's start snapshot; NaN when there is no such effect.</summary>
        public static float GetEffectValue(AudioMixerGroup group, string effect, string parameter)
        {
            object? e = FindEffect(group, effect);
            if (e == null) return float.NaN;
            return (float)EffectT!.GetMethod("GetValueForParameter", All)!
                .Invoke(e, new[] { group.audioMixer, Snapshot(group.audioMixer), (object)parameter });
        }

        /// <summary>Sets an effect parameter in the start snapshot (tests, tools); false when there is no such effect.</summary>
        public static bool SetEffectValue(AudioMixerGroup group, string effect, string parameter, float value)
        {
            object? e = FindEffect(group, effect);
            if (e == null) return false;
            EffectT!.GetMethod("SetValueForParameter", All)!
                .Invoke(e, new[] { group.audioMixer, Snapshot(group.audioMixer), parameter, (object)value });
            Save(group.audioMixer);
            return true;
        }

        /// <summary>Effect names of the group in order, Attenuation included.</summary>
        public static string[] EffectNames(AudioMixerGroup group) => Effects(group).Select(Name).ToArray();

        /// <summary>Names of the mixer's exposed parameters.</summary>
        public static string[] ExposedNames(AudioMixer mixer) => ExposedList(mixer).Select(p => p.name).ToArray();

        private static AudioMixerGroup? Find(AudioMixer mixer, string name) =>
            mixer.FindMatchingGroups(string.Empty).FirstOrDefault(g => g.name == name);

        private static object[] Effects(AudioMixerGroup group) =>
            (object[])GroupT!.GetProperty("effects", All)!.GetValue(group);

        private static string Name(object effect) => (string)EffectT!.GetProperty("effectName", All)!.GetValue(effect);

        private static object? FindEffect(AudioMixerGroup group, string effect) =>
            Effects(group).FirstOrDefault(e => Name(e) == effect);

        private static object Snapshot(AudioMixer mixer) =>
            MixerT!.GetProperty("TargetSnapshot", All)?.GetValue(mixer) ?? MixerT.GetProperty("startSnapshot", All)!.GetValue(mixer);

        private static object Insert(AudioMixerGroup group, string effect, IReadOnlyDictionary<string, float>? values)
        {
            AudioMixer mixer = group.audioMixer;
            object e = Activator.CreateInstance(EffectT!, effect);
            AssetDatabase.AddObjectToAsset((Object)e, mixer);
            EffectT!.GetMethod("PreallocateGUIDs", All)?.Invoke(e, null);
            object[] effects = Effects(group);
            // A Receive heads the group so its effects process what it receives; others go before the fader.
            int at = effect == "Receive" ? 0 : Array.FindIndex(effects, x => Name(x) == "Attenuation");
            GroupT!.GetMethod("InsertEffect", All, null, new[] { EffectT, typeof(int) }, null)!
                .Invoke(group, new[] { e, (object)(at < 0 ? effects.Length : at) });
            if (values != null)
            {
                MethodInfo set = EffectT.GetMethod("SetValueForParameter", All)!;
                object snapshot = Snapshot(mixer);
                foreach (var (name, value) in values)
                    set.Invoke(e, new[] { mixer, snapshot, name, (object)value });
            }
            Save(mixer);
            return e;
        }

        private static bool Exposed(AudioMixer mixer, string name) => ExposedList(mixer).Any(p => p.name == name);

        private static List<(GUID guid, string name)> ExposedList(AudioMixer mixer)
        {
            var list = new List<(GUID, string)>();
            var array = (Array)MixerT!.GetProperty("exposedParameters", All)!.GetValue(mixer);
            foreach (object p in array)
                list.Add(((GUID)ExposedT!.GetField("guid", All)!.GetValue(p), (string)ExposedT.GetField("name", All)!.GetValue(p)));
            return list;
        }

        private static bool Expose(AudioMixer mixer, GUID guid, object path, string name)
        {
            if ((bool)MixerT!.GetMethod("ContainsExposedParameter", All)!.Invoke(mixer, new object[] { guid }))
            {
                Debug.LogWarning($"[MixerKit] {mixer.name}: that parameter is exposed under another name; {name} not added.");
                return false;
            }

            MixerT.GetMethod("AddExposedParameter", All)!.Invoke(mixer, new[] { path });
            var array = (Array)MixerT.GetProperty("exposedParameters", All)!.GetValue(mixer);
            for (int i = 0; i < array.Length; i++)
            {
                object p = array.GetValue(i);
                if (!guid.Equals(ExposedT!.GetField("guid", All)!.GetValue(p))) continue;
                ExposedT.GetField("name", All)!.SetValue(p, name);
                array.SetValue(p, i);
            }
            MixerT.GetProperty("exposedParameters", All)!.SetValue(mixer, array);
            MixerT.GetMethod("OnChangedExposedParameter", All)?.Invoke(mixer, null);
            Save(mixer);
            return true;
        }

        // Dirty only: saving in the middle of a layout reloads sub-assets under the caller. The caller saves.
        private static void Save(AudioMixer mixer) => EditorUtility.SetDirty(mixer);

        private static T? Try<T>(string what, Func<T> make)
        {
            if (MixerT == null || GroupT == null || EffectT == null)
            {
                Debug.LogWarning($"[MixerKit] Could not {what}: Unity's mixer editor API is not there.");
                return default;
            }

            try
            {
                return make();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MixerKit] Could not {what}: {(e is TargetInvocationException t ? t.InnerException : e)}");
                return default;
            }
        }
    }
}

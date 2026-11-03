using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Rubickanov.Audio.Editor;
using UnityEditor;
using UnityEngine.Audio;

namespace Rubickanov.Audio.Tests
{
    public class MixerKitTests
    {
        private const string Path = "Assets/MixerKitTests_Temp.mixer";
        private AudioMixer _mixer = null!;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Path);
            _mixer = MixerKit.CreateMixer(Path)!;
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Path);

        private AudioMixerGroup Group(string name) => _mixer.FindMatchingGroups(string.Empty).Single(g => g.name == name);

        private void Layout()
        {
            AudioMixerGroup master = Group("Master");
            MixerKit.EnsureLimiter(master, -1f, 100f, "MasterLimit");
            AudioMixerGroup music = MixerKit.EnsureGroup(_mixer, "Music", "Master")!;
            AudioMixerGroup reverb = MixerKit.EnsureGroup(_mixer, "Reverb", "Master")!;
            MixerKit.EnsureEffect(reverb, "SFX Reverb");
            MixerKit.EnsureSend(music, reverb, -12f);
            MixerKit.EnsureExposedVolume(music, "MusicVolume");
        }

        private static float SendLevel(AudioMixerGroup group)
        {
            var groupT = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var effectT = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerEffectController");
            var mixerT = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var effects = (object[])groupT.GetProperty("effects", all)!.GetValue(group);
            object send = effects.Single(e => (string)effectT.GetProperty("effectName", all)!.GetValue(e) == "Send");
            object snapshot = mixerT.GetProperty("TargetSnapshot", all)!.GetValue(group.audioMixer);
            return (float)effectT.GetMethod("GetValueForMixLevel", all)!.Invoke(send, new[] { group.audioMixer, snapshot });
        }

        [Test]
        public void Layout_OnEmptyMixer_MakesGroupsLimiterSendAndExposedParameters()
        {
            Layout();

            AudioMixerGroup master = Group("Master");
            CollectionAssert.AreEqual(new[] { "Compressor", "Attenuation" }, MixerKit.EffectNames(master));
            Assert.AreEqual(-1f, MixerKit.GetEffectValue(master, "Compressor", "Threshold"), 1e-4f);
            Assert.AreEqual(0f, MixerKit.GetEffectValue(master, "Compressor", "Attack"), 1e-4f);
            Assert.AreEqual(100f, MixerKit.GetEffectValue(master, "Compressor", "Release"), 1e-4f);
            CollectionAssert.AreEqual(new[] { "Send", "Attenuation" }, MixerKit.EffectNames(Group("Music")));
            CollectionAssert.AreEqual(new[] { "Receive", "SFX Reverb", "Attenuation" }, MixerKit.EffectNames(Group("Reverb")));
            Assert.AreEqual(-12f, SendLevel(Group("Music")), 1e-4f);
            CollectionAssert.AreEquivalent(new[] { "MasterLimit", "MusicVolume" }, MixerKit.ExposedNames(_mixer));
        }

        [Test]
        public void Layout_SecondRun_ChangesNothingAndKeepsHandEditedThreshold()
        {
            Layout();
            MixerKit.SetEffectValue(Group("Master"), "Compressor", "Threshold", -6f);
            int groups = _mixer.FindMatchingGroups(string.Empty).Length;

            Layout();

            Assert.AreEqual(groups, _mixer.FindMatchingGroups(string.Empty).Length);
            CollectionAssert.AreEqual(new[] { "Compressor", "Attenuation" }, MixerKit.EffectNames(Group("Master")));
            CollectionAssert.AreEqual(new[] { "Send", "Attenuation" }, MixerKit.EffectNames(Group("Music")));
            CollectionAssert.AreEqual(new[] { "Receive", "SFX Reverb", "Attenuation" }, MixerKit.EffectNames(Group("Reverb")));
            Assert.AreEqual(-6f, MixerKit.GetEffectValue(Group("Master"), "Compressor", "Threshold"), 1e-4f);
            Assert.AreEqual(2, MixerKit.ExposedNames(_mixer).Length);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rubickanov.Audio.Tests
{
    // Пауза слушателя и нулевой timeScale видны только на живом звуке: без аудиоустройства timeSamples
    // стоит, и такие тесты пропускаются, а не врут.
    public class UnityAudioServicePauseTests
    {
        private const int Rate = 44100;
        private const float ClipSeconds = 0.2f;

        private AudioServiceConfig _config = null!;
        private UnityAudioService _service = null!;
        private GameObject _listener = null!;
        private AudioClip _clip = null!;
        private readonly List<Object> _garbage = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _listener = new GameObject("Listener", typeof(AudioListener));
            _clip = NewClip(ClipSeconds);
            _config = ScriptableObject.CreateInstance<AudioServiceConfig>();
            _service = new UnityAudioService(_config);
            yield return RequireLiveOutput();
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.pause = false;
            Time.timeScale = 1f;
            _service?.Dispose();
            Object.Destroy(_listener);
            Object.Destroy(_clip);
            Object.Destroy(_config);
            foreach (var garbage in _garbage)
                if (garbage != null) Object.Destroy(garbage);
            _garbage.Clear();
        }

        [UnityTest]
        public IEnumerator PlaySFX_ListenerPausedPastClipEnd_KeepsSource()
        {
            var handle = _service.PlaySFX(MakeSound(_clip));
            AudioListener.pause = true;

            yield return new WaitForSecondsRealtime(ClipSeconds * 3f);

            Assert.IsTrue(IsTracked(handle), "a paused sound must not be returned to the pool");
            Assert.AreEqual(1, ActiveCount());
        }

        [UnityTest]
        public IEnumerator PlaySFX_ListenerResumed_ReturnsSourceWhenClipEnds()
        {
            var handle = _service.PlaySFX(MakeSound(_clip));
            AudioListener.pause = true;
            yield return new WaitForSecondsRealtime(ClipSeconds * 2f);

            AudioListener.pause = false;
            yield return new WaitForSecondsRealtime(ClipSeconds * 2f);

            Assert.IsFalse(IsTracked(handle));
            Assert.AreEqual(0, ActiveCount());
        }

        [UnityTest]
        public IEnumerator PlaySFXAttached_ListenerPaused_KeepsFollowing()
        {
            var target = new GameObject("Target");
            _garbage.Add(target);
            var handle = _service.PlaySFXAttached(MakeSound(_clip), target.transform);
            AudioListener.pause = true;
            yield return new WaitForSecondsRealtime(ClipSeconds * 2f);

            target.transform.position = new Vector3(0f, 0f, 5f);
            yield return null;
            yield return null;

            Assert.IsTrue(IsTracked(handle), "a paused sound must not be returned to the pool");
            Assert.AreEqual(target.transform.position, SourceOf(handle).transform.position);
        }

        [UnityTest]
        public IEnumerator PlaySFX_PlaysOnPauseWhileListenerPaused_ReturnsSourceWhenClipEnds()
        {
            AudioListener.pause = true;
            var handle = _service.PlaySFX(MakeSound(_clip, playsOnPause: true));

            yield return new WaitForSecondsRealtime(ClipSeconds * 3f);

            Assert.IsFalse(IsTracked(handle), "a sound that plays on pause must finish and free its source");
            Assert.AreEqual(0, ActiveCount());
        }

        [UnityTest]
        public IEnumerator IsLoopPlaying_ListenerPaused_ReturnsTrue()
        {
            _service.PlayLoop("crowd", MakeSound(_clip));
            AudioListener.pause = true;

            yield return null;
            yield return null;

            Assert.IsTrue(_service.IsLoopPlaying("crowd"));
        }

        [UnityTest]
        public IEnumerator IsLoopPlaying_StoppedLoopListenerPaused_ReturnsFalse()
        {
            _service.PlayLoop("crowd", MakeSound(_clip));
            _service.StopLoop("crowd");
            AudioListener.pause = true;

            yield return null;

            Assert.IsFalse(_service.IsLoopPlaying("crowd"));
        }

        [UnityTest]
        public IEnumerator PlaySFX_TimeScaleZero_ReturnsSourceWhenClipEnds()
        {
            Time.timeScale = 0f;
            var handle = _service.PlaySFX(MakeSound(_clip));

            yield return new WaitForSecondsRealtime(ClipSeconds * 3f);

            Assert.IsFalse(IsTracked(handle));
            Assert.AreEqual(0, ActiveCount());
        }

        [UnityTest]
        public IEnumerator StopSound_FadeOutAtTimeScaleZero_ReclaimsSource()
        {
            var handle = _service.PlaySFX(MakeSound(NewClip(2f)));
            var source = SourceOf(handle);
            Time.timeScale = 0f;

            _service.StopSound(handle, fadeOut: 0.1f);
            yield return new WaitForSecondsRealtime(0.4f);

            Assert.IsFalse(source.isPlaying);
            Assert.IsNull(source.resource, "the faded source must be back in the pool");
        }

        // Без аудиоустройства источник не двигается, и о паузе он ничего не скажет.
        private IEnumerator RequireLiveOutput()
        {
            var probe = _listener.AddComponent<AudioSource>();
            probe.clip = _clip;
            probe.volume = 0f;
            probe.Play();
            yield return new WaitForSecondsRealtime(0.1f);
            bool live = probe.timeSamples > 0;
            Object.Destroy(probe);
            if (!live) Assert.Ignore("No live audio output: pause behaviour can't be observed.");
        }

        private AudioClip NewClip(float seconds)
        {
            int samples = Mathf.CeilToInt(Rate * seconds);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
                data[i] = 0.01f * Mathf.Sin(i * 440f * 2f * Mathf.PI / Rate);
            var clip = AudioClip.Create("tone", samples, 1, Rate, false);
            clip.SetData(data, 0);
            _garbage.Add(clip);
            return clip;
        }

        private static SoundConfig MakeSound(AudioClip clip, bool playsOnPause = false)
        {
            object boxed = default(SoundConfig);
            SetField(boxed, "_resource", clip);
            SetField(boxed, "_playsOnPause", playsOnPause);
            return (SoundConfig)boxed;
        }

        private static void SetField(object boxed, string name, object value) =>
            typeof(SoundConfig).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(boxed, value);

        private bool IsTracked(SoundHandle handle) => HandleSources().ContainsKey(HandleId(handle));

        private AudioSource SourceOf(SoundHandle handle) => HandleSources()[HandleId(handle)];

        private int ActiveCount() => GetField<ICollection>("_activeSources").Count;

        private Dictionary<long, AudioSource> HandleSources() => GetField<Dictionary<long, AudioSource>>("_handleSources");

        private static long HandleId(SoundHandle handle) =>
            (long)typeof(SoundHandle).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handle);

        private T GetField<T>(string name) =>
            (T)typeof(UnityAudioService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_service);
    }
}

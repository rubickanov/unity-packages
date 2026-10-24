#if UNITY_EDITOR || DEBUG
using System;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Rubickanov.Audio
{
    /// <summary>
    /// Records what the player hears into a float32 stereo WAV, for checks and probes. Editor and development
    /// builds only.
    /// </summary>
    public sealed class ListenerCapture : MonoBehaviour
    {
        public enum Tap
        {
            /// <summary>The final output, reverb-zone wet included; the device is silent while it records.</summary>
            Renderer,
            /// <summary>OnAudioFilterRead on the listener: heard live, but without the reverb-zone wet.</summary>
            Listener,
        }

        private float[] _buffer = Array.Empty<float>();
        private long _written;
        private string _path = "";
        private Tap _tap;
        private int _rate;
        private bool _finished;
        private bool _destroying;

        public bool Done { get; private set; }
        /// <summary>Stereo frames recorded so far: marks where a probe's events land in the file.</summary>
        public long Frames => Interlocked.Read(ref _written) / 2;
        /// <summary>Raised with the path once the file is written.</summary>
        public event Action<string>? Written;

        /// <summary>Starts recording <paramref name="seconds"/> of output to <paramref name="wavPath"/>.</summary>
        public static ListenerCapture Start(string wavPath, float seconds, Tap tap = Tap.Renderer)
        {
            ListenerCapture capture;
            if (tap == Tap.Listener)
            {
                AudioListener? ears = null;
                foreach (AudioListener l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                {
                    if (l.isActiveAndEnabled)
                    {
                        ears = l;
                        break;
                    }
                }
                if (ears == null) throw new InvalidOperationException("No enabled AudioListener to record on.");
                capture = ears.gameObject.AddComponent<ListenerCapture>();
            }
            else
            {
                capture = new GameObject("[ListenerCapture]").AddComponent<ListenerCapture>();
            }

            capture._path = wavPath;
            capture._tap = tap;
            capture._rate = AudioSettings.outputSampleRate;
            capture._buffer = new float[(int)(Mathf.Max(0f, seconds) * capture._rate) * 2];
            if (tap == Tap.Renderer) AudioRenderer.Start();
            return capture;
        }

        /// <summary>Stops early and writes what was recorded.</summary>
        public void Stop() => Finish();

        private void Update()
        {
            if (_finished) return;
            if (_tap == Tap.Renderer)
            {
                // Render every frame, also with no samples due: skipping it stalls the renderer's clock.
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                using var data = new NativeArray<float>(Mathf.Max(0, n) * 2, Allocator.Temp);
                AudioRenderer.Render(data);
                int m = (int)Math.Min(data.Length, _buffer.Length - _written);
                if (m > 0)
                {
                    NativeArray<float>.Copy(data, 0, _buffer, (int)_written, m);
                    _written += m;
                }
            }

            if (Interlocked.Read(ref _written) >= _buffer.Length) Finish();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_tap != Tap.Listener || _finished || channels != 2) return;
            long at = Interlocked.Read(ref _written);
            int n = (int)Math.Min(data.Length, _buffer.Length - at);
            if (n <= 0) return;
            Array.Copy(data, 0, _buffer, at, n);
            Interlocked.Add(ref _written, n);
        }

        private void OnDestroy()
        {
            _destroying = true;
            Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_tap == Tap.Renderer) AudioRenderer.Stop();
            int samples = (int)Interlocked.Read(ref _written);
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WriteWav(_path, _buffer, samples, _rate);
            Done = true;
            Written?.Invoke(_path);
            if (!_destroying)
            {
                if (_tap == Tap.Renderer) Destroy(gameObject);
                else Destroy(this);
            }
        }

        private static void WriteWav(string path, float[] data, int samples, int rate)
        {
            using var w = new BinaryWriter(File.Create(path));
            int bytes = samples * 4;
            w.Write("RIFF".ToCharArray());
            w.Write(36 + bytes);
            w.Write("WAVEfmt ".ToCharArray());
            w.Write(16);
            w.Write((short)3);
            w.Write((short)2);
            w.Write(rate);
            w.Write(rate * 8);
            w.Write((short)8);
            w.Write((short)32);
            w.Write("data".ToCharArray());
            w.Write(bytes);
            for (int i = 0; i < samples; i++) w.Write(data[i]);
        }
    }
}
#endif

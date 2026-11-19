using System;
using System.Collections;
using Rubickanov.Log;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Rubickanov.Report
{
    /// <summary>
    /// The frame as the player saw it, as a JPEG for a feedback, taken before any report window is drawn: at the end
    /// of this frame from the screen; in batch mode, where the end of a frame never comes, from the main camera after
    /// a short real-time wait; with no graphics device, none.
    /// </summary>
    public static class Moment
    {
        private const float BatchSettleSeconds = 0.3f;

        private static readonly LogChannel Log = LogChannel.Get("Report");

        /// <summary>Takes the shot and calls <paramref name="done"/> with it, or with null when there is none.</summary>
        public static void Take(int width, int quality, Action<byte[]> done)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                done(null);
                return;
            }

            ReportHost host = ReportHost.Get();
            host.StartCoroutine(Application.isBatchMode ? FromCamera(width, quality, done) : FromScreen(width, quality, done));
        }

        private static IEnumerator FromScreen(int width, int quality, Action<byte[]> done)
        {
            yield return new WaitForEndOfFrame();
            byte[] shot = null;
            Texture2D screen = null;
            try
            {
                screen = ScreenCapture.CaptureScreenshotAsTexture();
                shot = Encode(screen, width, quality);
            }
            catch (Exception exception)
            {
                Log.Warn($"No shot of the screen: {exception.Message}");
            }
            finally
            {
                Destroy(screen);
            }

            done(shot);
        }

        private static IEnumerator FromCamera(int width, int quality, Action<byte[]> done)
        {
            float until = Time.realtimeSinceStartup + BatchSettleSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                done(null);
                yield break;
            }

            int height = Mathf.Max(1, Mathf.RoundToInt(width * 9f / 16f));
            var target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture before = camera.targetTexture;
            byte[] shot = null;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                shot = Read(target, quality);
            }
            catch (Exception exception)
            {
                Log.Warn($"No shot of the camera: {exception.Message}");
            }
            finally
            {
                camera.targetTexture = before;
                RenderTexture.ReleaseTemporary(target);
            }

            done(shot);
        }

        // The shot scaled down to the width, never up.
        private static byte[] Encode(Texture2D screen, int width, int quality)
        {
            if (screen.width <= width)
            {
                return screen.EncodeToJPG(quality);
            }

            int height = Mathf.Max(1, Mathf.RoundToInt(screen.height * (float)width / screen.width));
            var target = RenderTexture.GetTemporary(width, height, 0);
            try
            {
                Graphics.Blit(screen, target);
                return Read(target, quality);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static byte[] Read(RenderTexture target, int quality)
        {
            RenderTexture active = RenderTexture.active;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                picture.Apply(false);
                return picture.EncodeToJPG(quality);
            }
            finally
            {
                RenderTexture.active = active;
                Destroy(picture);
            }
        }

        private static void Destroy(Object thing)
        {
            if (thing != null)
            {
                Object.Destroy(thing);
            }
        }
    }
}

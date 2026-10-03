using System;
using System.Collections;
using ColossalFramework.UI;
using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// Copies the finished city frame into "Local\RaccoonSkylines.City" for RE2's compositor. Unity 5.6 has no
    /// asynchronous GPU readback, so this reads the back buffer at the end of the frame; run Cities: Skylines
    /// windowed at a small size (960x540 is plenty for a skyline) and the stall is a few milliseconds.
    /// The game's UI is hidden while the feed runs so it doesn't end up in RE2's sky.
    /// </summary>
    public sealed class SkylineExporter : MonoBehaviour
    {
        public const int MaxWidth = 1920;
        public const int MaxHeight = 1080;

        public Func<long> HostFrame = () => 0;
        public Func<float> Daylight = () => 1f;
        public Func<float> Infection = () => 0f;
        public float Fps = 30f;

        FrameWriter writer;
        Texture2D tex;
        float nextCapture;
        long frames;
        bool uiHidden;

        void OnEnable()
        {
            try
            {
                if (writer == null)
                    writer = new FrameWriter(SharedFrames.City, MaxWidth, MaxHeight);
            }
            catch (Exception e)
            {
                Log.Warn("skyline feed unavailable: " + e.Message);
                enabled = false;
                return;
            }
            UIView.Show(false);
            uiHidden = true;
            StartCoroutine(Capture());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (uiHidden)
                UIView.Show(true);
            uiHidden = false;
        }

        IEnumerator Capture()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (Time.realtimeSinceStartup < nextCapture)
                    continue;
                nextCapture = Time.realtimeSinceStartup + 1f / Mathf.Max(1f, Fps);
                int w = Mathf.Min(Screen.width, MaxWidth), h = Mathf.Min(Screen.height, MaxHeight);
                if (tex == null || tex.width != w || tex.height != h)
                {
                    if (tex != null)
                        Destroy(tex);
                    tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                }
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                byte[] rgba = tex.GetRawTextureData();
                Camera cam = Camera.main;
                writer.Write(new FrameInfo
                {
                    Frame = ++frames,
                    HostFrame = HostFrame(),
                    Width = w,
                    Height = h,
                    Near = cam != null ? cam.nearClipPlane : 1f,
                    Far = cam != null ? cam.farClipPlane : 40000f,
                    Fov = cam != null ? cam.fieldOfView : 45f,
                    Flags = SharedFrames.FlagBottomUp, // ReadPixels fills row 0 from the bottom of the screen
                    Daylight = Daylight(),
                    Infection = Infection(),
                }, rgba);
            }
        }

        void OnDestroy()
        {
            if (writer != null)
                writer.Dispose();
            if (tex != null)
                Destroy(tex);
        }
    }
}

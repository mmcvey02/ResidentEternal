using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// The reverse passthrough: RE2's picture, published by the ReShade add-on into "Local\RaccoonSkylines.Bodycam",
    /// shown in a corner of the city view while you build, with the survivor's status.
    /// </summary>
    public sealed class BodycamPanel : MonoBehaviour
    {
        public string Status = "";
        /// <summary>True while the skyline feed runs: anything drawn now would end up in RE2's sky.</summary>
        public bool Suppressed;
        FrameReader reader;
        Texture2D tex;
        float lastFrameTime = -100f;

        void Awake()
        {
            reader = new FrameReader(SharedFrames.Bodycam);
        }

        void Update()
        {
            byte[] rgba;
            FrameInfo f = reader.TryRead(out rgba);
            if (f == null)
                return;
            if (tex == null || tex.width != f.Width || tex.height != f.Height)
            {
                if (tex != null)
                    Destroy(tex);
                tex = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false);
            }
            tex.LoadRawTextureData(rgba);
            tex.Apply(false);
            lastFrameTime = Time.realtimeSinceStartup;
        }

        void OnGUI()
        {
            if (tex == null || Suppressed)
                return;
            float w = Mathf.Min(480f, Screen.width * 0.3f);
            float h = w * tex.height / Mathf.Max(1, tex.width);
            var rect = new Rect(Screen.width - w - 16f, 64f, w, h);
            // The add-on writes rows top-down; Unity textures are bottom-up, so flip V.
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(0f, 1f, 1f, -1f));
            bool stale = Time.realtimeSinceStartup - lastFrameTime > 2f;
            GUI.Label(new Rect(rect.x + 8f, rect.yMax + 4f, w, 40f), (stale ? "BODYCAM: NO SIGNAL  " : "BODYCAM  ") + Status);
        }

        void OnDestroy()
        {
            reader.Dispose();
            if (tex != null)
                Destroy(tex);
        }
    }
}

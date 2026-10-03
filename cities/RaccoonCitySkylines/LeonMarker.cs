using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// Shows where the RE2 survivor is in the city: a pulsing marker drawn over the city view, coloured by health.
    /// Drawn with OnGUI from the world position, so the mod needs nothing beyond the game's own assemblies.
    /// </summary>
    public sealed class LeonMarker : MonoBehaviour
    {
        public bool Visible;
        public Vector3 Position;
        public float Health = 1f;
        Texture2D dot;

        void OnGUI()
        {
            Camera cam = Camera.main;
            if (!Visible || cam == null)
                return;
            Vector3 s = cam.WorldToScreenPoint(Position);
            if (s.z <= 0f)
                return; // behind the camera
            if (dot == null)
            {
                dot = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                dot.SetPixel(0, 0, Color.white);
                dot.Apply(false);
            }
            float size = 14f + 4f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
            float x = s.x, y = Screen.height - s.y; // GUI space is top-down
            Color old = GUI.color;
            GUI.color = Color.Lerp(new Color(0.9f, 0.1f, 0.1f, 0.9f), new Color(0.2f, 0.9f, 0.3f, 0.9f), Health);
            GUI.DrawTexture(new Rect(x - size / 2f, y - size / 2f, size, size), dot);
            GUI.color = old;
            GUI.Label(new Rect(x + size, y - 10f, 200f, 24f), "SURVIVOR");
        }

        void OnDestroy()
        {
            if (dot != null)
                Destroy(dot);
        }
    }
}

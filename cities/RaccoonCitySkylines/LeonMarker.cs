using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>Draws where the RE2 survivor is on the city map (a pulsing ring), via a Harmony postfix on the overlay pass.</summary>
    public static class LeonMarker
    {
        const string HarmonyId = "raccoon.city.skylines";
        public static bool Visible;
        public static Vector3 Position;
        public static float Health = 1f;

        public static void Patch()
        {
            var h = new Harmony(HarmonyId);
            h.Patch(AccessTools.Method(typeof(ToolManager), "EndOverlayImpl"), postfix: new HarmonyMethod(typeof(LeonMarker), nameof(Postfix)));
        }

        public static void Unpatch()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        }

        static void Postfix(RenderManager.CameraInfo cameraInfo)
        {
            if (!Visible)
                return;
            float pulse = 1f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
            Color c = Color.Lerp(new Color(0.9f, 0.1f, 0.1f, 0.8f), new Color(0.2f, 0.9f, 0.3f, 0.8f), Health);
            Singleton<RenderManager>.instance.OverlayEffect.DrawCircle(cameraInfo, c, Position, 24f * pulse, Position.y - 50f, Position.y + 50f, false, true);
            ToolManager.instance.m_drawCallData.m_overlayCalls++;
        }
    }
}

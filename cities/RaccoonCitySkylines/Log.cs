namespace RaccoonCitySkylines
{
    static class Log
    {
        public static void Info(string s) { UnityEngine.Debug.Log("[RaccoonCitySkylines] " + s); }
        public static void Warn(string s) { UnityEngine.Debug.LogWarning("[RaccoonCitySkylines] " + s); }
    }
}

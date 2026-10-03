using ICities;
using UnityEngine;

namespace RaccoonCitySkylines
{
    public sealed class Loading : LoadingExtensionBase
    {
        GameObject host;

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame && mode != LoadMode.NewGameFromScenario)
                return; // not in the editors
            host = new GameObject("RaccoonCitySkylines");
            host.AddComponent<Bridge>();
        }

        public override void OnLevelUnloading()
        {
            if (host != null)
                Object.Destroy(host);
            host = null;
        }
    }
}

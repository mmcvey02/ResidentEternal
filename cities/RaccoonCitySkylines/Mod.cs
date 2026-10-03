using System.Globalization;
using CitiesHarmony.API;
using ICities;

namespace RaccoonCitySkylines
{
    public sealed class Mod : IUserMod
    {
        public string Name { get { return "Raccoon City Skylines"; } }

        public string Description
        {
            get { return "Resident Evil 2 x Cities: Skylines passthrough: your city becomes RE2's skyline, and RE2 fights its T-virus outbreak."; }
        }

        public void OnEnabled()
        {
            HarmonyHelper.EnsureHarmonyInstalled();
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            Settings s = Settings.Instance;
            UIHelperBase link = helper.AddGroup("Link to Resident Evil 2");
            link.AddTextfield("Port (127.0.0.1 only)", s.Port.ToString(CultureInfo.InvariantCulture), v => { }, v =>
            {
                int p;
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p > 1024 && p < 65536)
                {
                    s.Port = p;
                    s.Save();
                }
            });
            link.AddSlider("Skyline frames per second", 5f, 60f, 5f, s.FeedFps, v => { s.FeedFps = v; s.Save(); });
            link.AddCheckbox("Show RE2 bodycam panel", s.ShowBodycam, v => { s.ShowBodycam = v; s.Save(); });

            UIHelperBase cam = helper.AddGroup("Where RE2 stands in your city");
            cam.AddCheckbox("Anchor at the first police station (the RPD)", s.AnchorAtPoliceStation, v => { s.AnchorAtPoliceStation = v; s.Save(); });
            cam.AddSlider("Heading (degrees)", -180f, 180f, 5f, s.HeadingDeg, v => { s.HeadingDeg = v; s.Save(); });
            cam.AddSlider("Eye height (m)", 0f, 120f, 0.5f, s.EyeHeight, v => { s.EyeHeight = v; s.Save(); });
            cam.AddSlider("City metres per RE2 metre", 0.5f, 10f, 0.5f, s.Scale, v => { s.Scale = v; s.Save(); });
            cam.AddCheckbox("Mirror Z (RE Engine handedness)", s.FlipZ, v => { s.FlipZ = v; s.Save(); });

            UIHelperBase sim = helper.AddGroup("Outbreak");
            sim.AddCheckbox("T-virus outbreak in the city", s.Outbreak, v => { s.Outbreak = v; s.Save(); });
            sim.AddSlider("Most new infections per step", 0f, 100f, 1f, s.MaxInfectionsPerStep, v => { s.MaxInfectionsPerStep = (int)v; s.Save(); });
        }
    }
}

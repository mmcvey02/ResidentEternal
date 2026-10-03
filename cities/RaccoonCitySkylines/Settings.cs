using System;
using System.IO;
using System.Collections.Generic;

namespace RaccoonCitySkylines
{
    /// <summary>Mod options, saved next to Cities: Skylines' own settings (never inside a save game).</summary>
    public sealed class Settings
    {
        public int Port = 25600;
        /// <summary>Anchor the RE2 camera at the city's first police station (the RPD); otherwise at AnchorX/Z.</summary>
        public bool AnchorAtPoliceStation = true;
        public float AnchorX = 0f;
        public float AnchorZ = 0f;
        public float HeadingDeg = 0f;
        /// <summary>Metres above the anchor building's base where RE2's camera origin sits.</summary>
        public float EyeHeight = 1.6f;
        /// <summary>City metres per RE2 metre. Raise it to make RE2's small maps cover more of the city.</summary>
        public float Scale = 1f;
        /// <summary>RE Engine and Unity disagree on handedness; mirror Z (see protocol/PROTOCOL.md section 4).</summary>
        public bool FlipZ = true;
        /// <summary>Skyline frames per second sent to RE2.</summary>
        public float FeedFps = 30f;
        public bool Outbreak = true;
        /// <summary>Most citizens the outbreak may make sick per simulation step.</summary>
        public int MaxInfectionsPerStep = 12;
        public bool ShowBodycam = true;

        public static string Path
        {
            get { return System.IO.Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "RaccoonCitySkylines.json"); }
        }

        static Settings instance;

        public static Settings Instance
        {
            get
            {
                if (instance == null)
                    instance = Load();
                return instance;
            }
        }

        static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (File.Exists(Path))
                    s.Apply(Json.Parse(File.ReadAllText(Path)) as Dictionary<string, object>);
            }
            catch (Exception e)
            {
                Log.Warn("settings unreadable, using defaults: " + e.Message);
            }
            return s;
        }

        /// <summary>Takes every known field from a parsed settings file; missing or wrong-typed ones keep their defaults.</summary>
        public void Apply(Dictionary<string, object> o)
        {
            if (o == null)
                return;
            Port = (int)Json.Num(o, "port", Port);
            AnchorAtPoliceStation = Json.Bool(o, "anchorAtPoliceStation", AnchorAtPoliceStation);
            AnchorX = (float)Json.Num(o, "anchorX", AnchorX);
            AnchorZ = (float)Json.Num(o, "anchorZ", AnchorZ);
            HeadingDeg = (float)Json.Num(o, "headingDeg", HeadingDeg);
            EyeHeight = (float)Json.Num(o, "eyeHeight", EyeHeight);
            Scale = (float)Json.Num(o, "scale", Scale);
            FlipZ = Json.Bool(o, "flipZ", FlipZ);
            FeedFps = (float)Json.Num(o, "feedFps", FeedFps);
            Outbreak = Json.Bool(o, "outbreak", Outbreak);
            MaxInfectionsPerStep = (int)Json.Num(o, "maxInfectionsPerStep", MaxInfectionsPerStep);
            ShowBodycam = Json.Bool(o, "showBodycam", ShowBodycam);
        }

        public Dictionary<string, object> ToJson()
        {
            return Json.Obj("port", Port, "anchorAtPoliceStation", AnchorAtPoliceStation, "anchorX", AnchorX, "anchorZ", AnchorZ,
                "headingDeg", HeadingDeg, "eyeHeight", EyeHeight, "scale", Scale, "flipZ", FlipZ, "feedFps", FeedFps,
                "outbreak", Outbreak, "maxInfectionsPerStep", MaxInfectionsPerStep, "showBodycam", ShowBodycam);
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(Path, Json.Write(ToJson()));
            }
            catch (Exception e)
            {
                Log.Warn("could not save settings: " + e.Message);
            }
        }
    }
}

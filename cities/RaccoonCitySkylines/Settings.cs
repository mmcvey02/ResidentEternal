using System;
using System.IO;
using System.Xml.Serialization;

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

        [XmlIgnore]
        public static string Path
        {
            get
            {
                string dir = ColossalFramework.IO.DataLocation.localApplicationData;
                return System.IO.Path.Combine(dir, "RaccoonCitySkylines.xml");
            }
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
            try
            {
                if (File.Exists(Path))
                    using (var r = new StreamReader(Path))
                        return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(r);
            }
            catch (Exception e)
            {
                Log.Warn("settings unreadable, using defaults: " + e.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                using (var w = new StreamWriter(Path))
                    new XmlSerializer(typeof(Settings)).Serialize(w, this);
            }
            catch (Exception e)
            {
                Log.Warn("could not save settings: " + e.Message);
            }
        }
    }
}

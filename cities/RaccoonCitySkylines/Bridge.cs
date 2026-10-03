using System.Collections.Generic;
using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// The Cities: Skylines half of the passthrough: answers RE2 on the link, drives the camera from RE2's, feeds the
    /// skyline back, runs the outbreak and shows RE2's bodycam.
    ///
    /// Keys: F9 switches between the skyline feed (camera follows RE2) and normal building, F10 re-centres RE2's
    /// current position on the anchor, F11 shows or hides the bodycam.
    /// </summary>
    public sealed class Bridge : MonoBehaviour
    {
        Settings settings;
        LinkServer link;
        readonly CameraSync camSync = new CameraSync();
        readonly OutbreakController outbreak = new OutbreakController();
        SkylineExporter exporter;
        BodycamPanel bodycam;
        LeonMarker marker;
        int seenConnections;
        Dictionary<string, object> lastCam;
        float nextCityMessage;
        float nextSave;
        int evId;
        bool feedWanted = true;
        bool lastPower = true;
        string cityName = "";
        string toast = "";
        float toastUntil;

        void Start()
        {
            settings = Settings.Instance;
            cityName = CityProbe.CityName();
            camSync.ResolveAnchor(settings);
            Vec3 a = camSync.Mapper.Anchor;
            outbreak.AnchorDistrict = CityProbe.DistrictAt(new Vector3((float)a.X, (float)a.Y, (float)a.Z));
            outbreak.Load(cityName);

            exporter = gameObject.AddComponent<SkylineExporter>();
            exporter.enabled = false;
            exporter.HostFrame = () => camSync.LastHostFrame;
            exporter.Daylight = CityProbe.Daylight;
            exporter.Infection = () => (float)outbreak.Model.Infection[outbreak.AnchorDistrict];
            bodycam = gameObject.AddComponent<BodycamPanel>();
            bodycam.enabled = settings.ShowBodycam;
            marker = gameObject.AddComponent<LeonMarker>();

            link = new LinkServer(settings.Port) { Log = Log.Info };
            try
            {
                link.Start();
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Log.Warn("link could not listen on port " + settings.Port + ": " + e.Message);
                link = null;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
                feedWanted = !feedWanted;
            if (Input.GetKeyDown(KeyCode.F10))
                camSync.Recentre(lastCam);
            if (Input.GetKeyDown(KeyCode.F11))
                bodycam.enabled = !bodycam.enabled;

            outbreak.Update(settings);
            if (link == null)
                return;

            if (link.Connections != seenConnections)
            {
                seenConnections = link.Connections;
                link.Send(Json.Obj("t", "hello", "side", "city", "v", 1, "city", cityName));
                camSync.Mapper.HasOrigin = false; // a new RE2 session: its first camera lands on the anchor
            }

            foreach (Dictionary<string, object> m in link.Drain())
                Handle(m);

            bool feed = feedWanted && link.Connected;
            if (feed && !camSync.Driving)
                camSync.Begin();
            else if (!feed && camSync.Driving)
                camSync.End();
            bodycam.Suppressed = camSync.Driving;
            marker.Visible = !camSync.Driving && link.Connected && camSync.Mapper.HasOrigin;
            exporter.Fps = settings.FeedFps;
            if (exporter.enabled != feed)
                exporter.enabled = feed;

            if (Time.realtimeSinceStartup >= nextCityMessage && link.Connected)
            {
                nextCityMessage = Time.realtimeSinceStartup + 0.25f;
                SendCity(feed);
            }
            if (Time.realtimeSinceStartup >= nextSave)
            {
                nextSave = Time.realtimeSinceStartup + 60f;
                outbreak.Save(cityName);
            }
        }

        void LateUpdate()
        {
            if (lastCam != null)
                camSync.Apply(lastCam);
        }

        void Handle(Dictionary<string, object> m)
        {
            switch (Json.Str(m, "t"))
            {
                case "cam":
                    lastCam = m;
                    break;
                case "player":
                    OnPlayer(m);
                    break;
                case "ev":
                    OnEvent(Json.Str(m, "k"), Json.Get(m, "d") as Dictionary<string, object>);
                    break;
            }
        }

        readonly double[] pos = new double[3];

        void OnPlayer(Dictionary<string, object> m)
        {
            // The origin comes from RE2's camera; until the first camera arrives there is nowhere to put the survivor.
            if (!Json.Nums(m, "p", pos) || !camSync.Mapper.HasOrigin)
                return;
            Vec3 p;
            Quat unused;
            camSync.Mapper.Map(new Vec3(pos[0], pos[1], pos[2]), Quat.Identity, out p, out unused);
            marker.Position = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
            double hp = Json.Num(m, "hp", -1);
            marker.Health = hp < 0 ? 1f : Mathf.Clamp01((float)hp);
            string area = Json.Str(m, "area");
            bodycam.Status = (area.Length > 0 ? area + "  " : "") + (hp < 0 ? "" : "HP " + Mathf.RoundToInt((float)hp * 100f) + "%");
        }

        void OnEvent(string kind, Dictionary<string, object> d)
        {
            switch (kind)
            {
                case "kill":
                    outbreak.OnKill();
                    break;
                case "tyrant":
                    Surge(1.0, "Mr. X is walking the streets of " + CityProbe.DistrictName(outbreak.AnchorDistrict) + ".");
                    break;
                case "birkin":
                    Surge(0.7, "Something is moving in the sewers under " + CityProbe.DistrictName(outbreak.AnchorDistrict) + ".");
                    break;
                case "save":
                    Toast("A survivor reached a safe room.");
                    break;
            }
        }

        void Surge(double strength, string message)
        {
            outbreak.Model.Wave(outbreak.AnchorDistrict, strength);
            SendEvent("wave", Json.Obj("strength", strength));
            Toast(message);
        }

        void SendCity(bool feed)
        {
            int d = outbreak.AnchorDistrict;
            DistrictInput x = outbreak.Input(d);
            if (x.Power != lastPower)
            {
                lastPower = x.Power;
                SendEvent("blackout", Json.Obj("on", !x.Power));
                Toast(x.Power ? "Power is back in " + CityProbe.DistrictName(d) + "." : "Blackout in " + CityProbe.DistrictName(d) + ". The RPD goes dark.");
            }
            link.Send(Json.Obj(
                "t", "city",
                "district", CityProbe.DistrictName(d),
                "inf", System.Math.Round(outbreak.Model.Infection[d], 4),
                "cityInf", System.Math.Round(outbreak.CityInfection, 4),
                "power", x.Power,
                "police", System.Math.Round(x.Police, 3),
                "health", System.Math.Round(x.Health, 3),
                "pop", (long)x.Population,
                "daylight", System.Math.Round(CityProbe.Daylight(), 3),
                "feed", feed));
        }

        void SendEvent(string kind, Dictionary<string, object> d)
        {
            if (link != null)
                link.Send(Json.Obj("t", "ev", "id", ++evId, "k", kind, "d", d));
        }

        void Toast(string s)
        {
            toast = s;
            toastUntil = Time.realtimeSinceStartup + 6f;
            Log.Info(s);
        }

        void OnGUI()
        {
            if (Time.realtimeSinceStartup < toastUntil && !camSync.Driving)
                GUI.Label(new Rect(16f, Screen.height * 0.2f, 640f, 40f), toast);
        }

        void OnDestroy()
        {
            camSync.End();
            outbreak.Save(cityName);
            if (link != null)
                link.Dispose();
        }
    }
}

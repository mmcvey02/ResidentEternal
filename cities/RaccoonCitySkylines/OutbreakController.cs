using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// Runs the <see cref="OutbreakModel"/> against the live city and makes it visible in Cities: Skylines: infected
    /// residents fall sick (ambulances and hearses get busy). State lives in a sidecar file per city, never in the
    /// save game, so removing the mod leaves saves untouched.
    /// </summary>
    public sealed class OutbreakController
    {
        public readonly OutbreakModel Model = new OutbreakModel();
        readonly DistrictInput[] inputs = new DistrictInput[OutbreakModel.MaxDistricts];
        readonly System.Random rng = new System.Random();
        uint lastFrame;
        bool started;
        float nextProbe;
        int pendingKills;
        public int AnchorDistrict;

        public DistrictInput Input(int d)
        {
            return d >= 0 && d < inputs.Length ? inputs[d] : new DistrictInput { Power = true };
        }

        public double CityInfection { get { return Model.CityInfection(inputs); } }

        static string StatePath(string city)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                city = city.Replace(c, '_');
            return Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "RaccoonCitySkylines." + city + ".json");
        }

        public void Load(string city)
        {
            try
            {
                string path = StatePath(city);
                if (!File.Exists(path))
                    return;
                var o = Json.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
                var arr = Json.Get(o, "infection") as List<object>;
                if (arr == null)
                    return;
                for (int i = 0; i < arr.Count && i < OutbreakModel.MaxDistricts; i++)
                    if (arr[i] is double)
                        Model.Infection[i] = OutbreakModel.Clamp01((double)arr[i]);
            }
            catch (Exception e)
            {
                Log.Warn("outbreak state unreadable: " + e.Message);
            }
        }

        public void Save(string city)
        {
            try
            {
                File.WriteAllText(StatePath(city), Json.Write(Json.Obj("infection", Model.Infection)));
            }
            catch (Exception e)
            {
                Log.Warn("could not save outbreak state: " + e.Message);
            }
        }

        public void OnKill()
        {
            pendingKills++;
        }

        /// <summary>Call every frame from the game thread.</summary>
        public void Update(Settings s)
        {
            SimulationManager sim = Singleton<SimulationManager>.instance;
            if (Time.realtimeSinceStartup >= nextProbe)
            {
                nextProbe = Time.realtimeSinceStartup + 2f;
                CityProbe.Read(inputs);
            }
            if (pendingKills > 0)
            {
                Model.Kills(AnchorDistrict, pendingKills);
                pendingKills = 0;
            }
            if (sim.SimulationPaused || !s.Outbreak)
            {
                lastFrame = sim.m_currentFrameIndex;
                return;
            }
            uint frame = sim.m_currentFrameIndex;
            if (!started)
            {
                started = true;
                lastFrame = frame;
                return;
            }
            uint elapsed = frame - lastFrame;
            if (elapsed < 256)
                return; // step every 256 simulation frames
            lastFrame = frame;
            Model.SeedIfClean(AnchorDistrict);
            // One in-game day is 2^16 simulation frames (SimulationManager.DAYTIME_FRAMES).
            Model.Step(inputs, elapsed / 65536.0);
            int budget = s.MaxInfectionsPerStep;
            if (budget > 0)
                sim.AddAction(() => Infect(budget));
        }

        /// <summary>On the simulation thread: makes up to `budget` healthy residents sick, weighted by infection.</summary>
        void Infect(int budget)
        {
            Citizen[] citizens = Singleton<CitizenManager>.instance.m_citizens.m_buffer;
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            DistrictManager dm = Singleton<DistrictManager>.instance;
            for (int tries = 0; tries < budget * 8 && budget > 0; tries++)
            {
                uint id = (uint)rng.Next(1, citizens.Length);
                if ((citizens[id].m_flags & Citizen.Flags.Created) == Citizen.Flags.None || citizens[id].Dead || citizens[id].Sick)
                    continue;
                ushort home = citizens[id].m_homeBuilding;
                if (home == 0)
                    continue;
                int d = dm.GetDistrict(buildings[home].m_position);
                if (rng.NextDouble() < Model.Infection[d] * 0.5)
                {
                    citizens[id].Sick = true;
                    budget--;
                }
            }
        }
    }
}

using System;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace RaccoonCitySkylines
{
    /// <summary>
    /// Everything read from Cities: Skylines' simulation, in one place. If a game update moves something, this is the
    /// file to fix. Coverage is sampled at each district's residential buildings.
    /// </summary>
    public static class CityProbe
    {
        public static string CityName()
        {
            try
            {
                return Singleton<SimulationManager>.instance.m_metaData.m_CityName ?? "";
            }
            catch (NullReferenceException)
            {
                return "";
            }
        }

        public static byte DistrictAt(Vector3 p)
        {
            return Singleton<DistrictManager>.instance.GetDistrict(p);
        }

        public static string DistrictName(int d)
        {
            if (d == 0)
                return CityName();
            return Singleton<DistrictManager>.instance.GetDistrictName(d) ?? ("District " + d);
        }

        /// <summary>0 at night, 1 at noon, from the in-game clock.</summary>
        public static float Daylight()
        {
            float hour = Singleton<SimulationManager>.instance.m_currentDayTimeHour;
            return Mathf.Clamp01(Mathf.Sin((hour - 6f) / 12f * Mathf.PI) * 1.5f);
        }

        static float Coverage(ImmaterialResourceManager.Resource r, Vector3 p)
        {
            int local;
            Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(r, p, out local);
            return Mathf.Clamp01(local / 100f); // ~100 is what the game's info view shows as fully covered
        }

        // Whether a building has power, read by name at runtime so the mod compiles against any game version:
        // a building without electricity counts up m_electricityProblemTimer. If the field is missing, every
        // building counts as powered (no blackouts) instead of the mod failing to build.
        static readonly FieldInfo ElectricityProblemTimer = typeof(Building).GetField("m_electricityProblemTimer", BindingFlags.Public | BindingFlags.Instance);
        static bool warnedNoPowerField;

        static bool HasPower(ref Building b)
        {
            if (ElectricityProblemTimer == null)
            {
                if (!warnedNoPowerField)
                {
                    warnedNoPowerField = true;
                    Log.Warn("Building.m_electricityProblemTimer not found; blackouts are off");
                }
                return true;
            }
            object v = ElectricityProblemTimer.GetValue(b);
            return v == null || Convert.ToInt32(v) == 0;
        }

        /// <summary>Fills one DistrictInput per district from up to `budget` residential buildings.</summary>
        public static void Read(DistrictInput[] inputs, int budget = 4096)
        {
            DistrictManager dm = Singleton<DistrictManager>.instance;
            var police = new double[inputs.Length];
            var health = new double[inputs.Length];
            var powered = new int[inputs.Length];
            var samples = new int[inputs.Length];
            for (int d = 0; d < inputs.Length; d++)
            {
                inputs[d] = new DistrictInput();
                if (d == 0 || (dm.m_districts.m_buffer[d].m_flags & District.Flags.Created) != District.Flags.None)
                {
                    inputs[d].Exists = true;
                    inputs[d].Population = dm.m_districts.m_buffer[d].m_populationData.m_finalCount;
                }
            }
            Building[] buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            int step = Mathf.Max(1, buildings.Length / budget);
            int offset = UnityEngine.Random.Range(0, step);
            for (int i = 1 + offset; i < buildings.Length; i += step)
            {
                if ((buildings[i].m_flags & Building.Flags.Created) == Building.Flags.None)
                    continue;
                BuildingInfo info = buildings[i].Info;
                if (info == null || info.m_class == null || info.m_class.m_service != ItemClass.Service.Residential)
                    continue;
                Vector3 p = buildings[i].m_position;
                int d = dm.GetDistrict(p);
                if (d >= inputs.Length)
                    continue;
                police[d] += Coverage(ImmaterialResourceManager.Resource.PoliceDepartment, p);
                health[d] += Coverage(ImmaterialResourceManager.Resource.HealthCare, p);
                powered[d] += HasPower(ref buildings[i]) ? 1 : 0;
                samples[d]++;
            }
            for (int d = 0; d < inputs.Length; d++)
            {
                if (samples[d] == 0)
                {
                    inputs[d].Power = true;
                    continue;
                }
                inputs[d].Police = police[d] / samples[d];
                inputs[d].Health = health[d] / samples[d];
                inputs[d].Power = powered[d] * 2 >= samples[d];
            }
        }
    }
}

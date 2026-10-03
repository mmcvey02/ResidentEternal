using System;

namespace RaccoonCitySkylines
{
    /// <summary>What the outbreak needs to know about one district (index 0 = land outside any district).</summary>
    public struct DistrictInput
    {
        public bool Exists;
        public double Population;
        /// <summary>Police coverage 0..1 where people live.</summary>
        public double Police;
        /// <summary>Healthcare coverage 0..1.</summary>
        public double Health;
        /// <summary>True when the district has electricity.</summary>
        public bool Power;
    }

    public sealed class OutbreakParams
    {
        /// <summary>Logistic growth per day at full density with nothing fighting it.</summary>
        public double Beta = 0.9;
        /// <summary>How fast districts drift toward the citywide level (commuters carry it) per day.</summary>
        public double Mixing = 0.25;
        /// <summary>Removal per day at full police coverage (the RPD holds the line).</summary>
        public double PoliceSuppression = 0.6;
        /// <summary>Removal per day at full healthcare coverage (vaccine research, quarantine).</summary>
        public double HealthSuppression = 0.4;
        /// <summary>Extra growth per day in a blackout.</summary>
        public double BlackoutBeta = 0.5;
        /// <summary>Population at which density stops adding to growth.</summary>
        public double DensityPopulation = 5000;
        /// <summary>Infection removed per zombie Leon or Claire kills in RE2.</summary>
        public double KillRelief = 0.004;
        /// <summary>Infection added to the anchor district by a Mr. X or G event, times strength.</summary>
        public double WaveSize = 0.15;
        /// <summary>Patient zero: infection in the anchor district when an outbreak starts from nothing.</summary>
        public double Seed = 0.03;
    }

    /// <summary>
    /// A T-virus outbreak over the city's districts. Pure logistic growth, slowed by police and healthcare coverage,
    /// spread by mixing toward the population-weighted city mean. It knows nothing about the game, so it is tested
    /// on its own (cities/tests) and fed by <c>CityProbe</c> in the game.
    /// </summary>
    public sealed class OutbreakModel
    {
        public const int MaxDistricts = 128;

        public readonly OutbreakParams P;
        public readonly double[] Infection = new double[MaxDistricts];

        public OutbreakModel(OutbreakParams p = null)
        {
            P = p ?? new OutbreakParams();
        }

        public double CityInfection(DistrictInput[] inputs)
        {
            double pop = 0, inf = 0;
            for (int d = 0; d < MaxDistricts && d < inputs.Length; d++)
            {
                if (!inputs[d].Exists || inputs[d].Population <= 0)
                    continue;
                pop += inputs[d].Population;
                inf += inputs[d].Population * Infection[d];
            }
            return pop > 0 ? inf / pop : 0;
        }

        /// <summary>Advances the outbreak by dtDays of game time.</summary>
        public void Step(DistrictInput[] inputs, double dtDays)
        {
            if (dtDays <= 0)
                return;
            double mean = CityInfection(inputs);
            var next = new double[MaxDistricts];
            for (int d = 0; d < MaxDistricts; d++)
            {
                next[d] = Infection[d];
                if (d >= inputs.Length || !inputs[d].Exists || inputs[d].Population <= 0)
                    continue;
                DistrictInput x = inputs[d];
                double i = Infection[d];
                double density = Math.Min(1.0, x.Population / P.DensityPopulation);
                double growth = P.Beta * (0.35 + 0.65 * density) * i * (1 - i);
                if (!x.Power)
                    growth += P.BlackoutBeta * i * (1 - i);
                double mixing = P.Mixing * (mean - i);
                double suppression = (P.PoliceSuppression * Clamp01(x.Police) + P.HealthSuppression * Clamp01(x.Health)) * i;
                next[d] = Clamp01(i + dtDays * (growth + mixing - suppression));
            }
            Array.Copy(next, Infection, MaxDistricts);
        }

        /// <summary>RE2 reported kills in this district's streets.</summary>
        public void Kills(int district, int count)
        {
            if (Valid(district))
                Infection[district] = Clamp01(Infection[district] - P.KillRelief * count);
        }

        /// <summary>Mr. X or G showed up: the outbreak surges where the player is.</summary>
        public void Wave(int district, double strength)
        {
            if (Valid(district))
                Infection[district] = Clamp01(Infection[district] + P.WaveSize * Clamp01(strength));
        }

        /// <summary>Starts an outbreak in this district if the whole city is clean.</summary>
        public bool SeedIfClean(int district)
        {
            if (!Valid(district))
                return false;
            foreach (double i in Infection)
                if (i > 1e-6)
                    return false;
            Infection[district] = P.Seed;
            return true;
        }

        static bool Valid(int d)
        {
            return d >= 0 && d < MaxDistricts;
        }

        public static double Clamp01(double v)
        {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}

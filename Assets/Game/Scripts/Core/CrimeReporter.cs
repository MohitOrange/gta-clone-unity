using UnityEngine;

namespace MiniGTA
{
    public enum Crime
    {
        RanRedLight,
        FiredWeapon,
        AssaultedCivilian,
        KilledCivilian,
        RanOverCivilian,
        StoleVehicle,
        AssaultedPolice,
        KilledPolice,
    }

    /// <summary>
    /// Turns criminal acts into wanted heat, but only when somebody actually saw them.
    ///
    /// Centralised so that no weapon, vehicle or fist needs to know about the wanted system --
    /// they report what they did and this decides whether it costs anything. The witness rule
    /// is what makes the wanted level feel fair: the same act is free down an empty alley and
    /// expensive in a crowd, which is exactly the judgement a player should be making.
    /// </summary>
    public static class CrimeReporter
    {
        /// <summary>Civilians this close will notice and report.</summary>
        public const float CivilianWitnessRadius = 26f;

        /// <summary>Police this close will notice directly.</summary>
        public const float PoliceWitnessRadius = 55f;

        /// <summary>Panic radius when a weapon goes off or someone is hurt.</summary>
        public const float PanicRadius = 24f;

        /// <summary>
        /// Report a crime at a position. Returns the heat actually added -- zero if unwitnessed.
        /// </summary>
        public static float Report(Crime crime, Vector3 where, GameObject perpetrator = null)
        {
            var heat = HeatSystem.Instance;
            if (heat == null) return 0f;

            // Crimes against police are always known: the victim radios it in.
            bool alwaysKnown = crime == Crime.AssaultedPolice || crime == Crime.KilledPolice;

            bool seen = alwaysKnown
                        || PoliceVehicle.NearestWitness(where, PoliceWitnessRadius) != null
                        || Pedestrian.CountNear(where, CivilianWitnessRadius) > 0;

            // Already wanted? The police are actively looking, so everything counts.
            if (!seen && heat.WantedLevel > 0) seen = true;

            if (!seen) return 0f;

            float amount = HeatFor(crime, heat);
            heat.AddHeat(amount, Describe(crime));
            return amount;
        }

        /// <summary>Report a crime and scatter nearby civilians in one call.</summary>
        public static float ReportAndPanic(Crime crime, Vector3 where, Vector3 threat,
                                           GameObject perpetrator = null)
        {
            float added = Report(crime, where, perpetrator);
            Pedestrian.AlarmNear(where, PanicRadius, threat);
            return added;
        }

        static float HeatFor(Crime crime, HeatSystem heat) => crime switch
        {
            Crime.RanRedLight => heat.RedLightHeat,
            Crime.FiredWeapon => heat.FiredWeaponHeat,
            Crime.AssaultedCivilian => heat.AssaultHeat,
            Crime.KilledCivilian => heat.KillCivilianHeat,
            Crime.RanOverCivilian => heat.PedestrianHitHeat,
            Crime.StoleVehicle => heat.VehicleTheftHeat,
            Crime.AssaultedPolice => heat.AssaultPoliceHeat,
            Crime.KilledPolice => heat.KillPoliceHeat,
            _ => 0f,
        };

        static string Describe(Crime crime) => crime switch
        {
            Crime.RanRedLight => "Ran a red light",
            Crime.FiredWeapon => "Firearm discharged",
            Crime.AssaultedCivilian => "Assault",
            Crime.KilledCivilian => "Manslaughter",
            Crime.RanOverCivilian => "Hit a pedestrian",
            Crime.StoleVehicle => "Grand theft auto",
            Crime.AssaultedPolice => "Assaulting an officer",
            Crime.KilledPolice => "Killed an officer",
            _ => "Disturbance",
        };
    }
}

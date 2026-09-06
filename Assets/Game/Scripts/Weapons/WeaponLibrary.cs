using System;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// How one weapon prefab sits in a hand.
    ///
    /// This exists because the weapon pack does not model its weapons on a single convention:
    /// the firearms run along +Z and the melee weapons along +Y, all of them at roughly 2.5x
    /// life size, with pivots near but not on the grip. There is no one offset that works for
    /// every prefab, so the offset is data per weapon rather than a constant in code.
    /// </summary>
    [Serializable]
    public class WeaponDefinition
    {
        [Tooltip("Lookup key. WeaponSocket.Equip takes this.")]
        public string Id = "pistol";

        public GameObject Prefab;

        [Header("Fit in the hand")]
        [Tooltip("Offset from the socket, in socket space.")]
        public Vector3 LocalPosition;
        public Vector3 LocalEuler;
        [Tooltip("Uniform scale. The pack models are oversize; this brings them to life size.")]
        public float Scale = 1f;

        [Header("Muzzle")]
        [Tooltip("Where the barrel ends, in the weapon's own local space (pre-scale).")]
        public Vector3 MuzzleLocal;
        [Tooltip("False for melee weapons -- no muzzle transform is created.")]
        public bool HasMuzzle = true;

        // ------------------------------------------------------------------ ballistics
        //
        // Phase 14, Section 2. These live here rather than on PlayerCombat because 2.9 requires
        // the player and every armed NPC to run the *same* weapon mechanic -- so the numbers
        // that define a weapon have to be a property of the weapon, not of whoever is holding
        // it. Adding fields to a serialized class is additive: existing WeaponLibrary assets
        // pick up these defaults without being rewritten.

        [Header("Ballistics")]
        [Tooltip("Rounds per magazine. Firing draws from here; reloading refills it.")]
        public int MagazineSize = 12;
        [Tooltip("Rounds the holder can carry beyond the loaded magazine.")]
        public int ReserveCapacity = 96;
        [Tooltip("Seconds between shots.")]
        public float FireCooldown = 0.3f;
        [Tooltip("Seconds the reload animation locks the weapon for.")]
        public float ReloadSeconds = 1.6f;
        [Tooltip("Seconds to bring the weapon up on equip or switch.")]
        public float EquipSeconds = 0.45f;
        public float Damage = 34f;
        public float Range = 90f;
        [Tooltip("Cone of inaccuracy in degrees. Zero feels robotic.")]
        public float Spread = 1.4f;

        [Header("Animation")]
        [Tooltip("Clip family for the upper-body layer: Gun, Rifle, AssaultRifle, Bazooka, "
                 + "DualGun. Matches the Kevin Iglesias naming, e.g. HumanM@Gun_Reload01.")]
        public string ClipFamily = "Gun";

        [Tooltip("Section 11. Which carry/aim stance this weapon uses, as an index into the "
                 + "AimPose blend: 0 pistol, 1 alternate pistol, 2 rifle, 3 assault rifle, "
                 + "4 bazooka. Everything used to reuse the pistol clip, so a rifle was held "
                 + "like a handgun.")]
        public float Stance;
    }

    /// <summary>
    /// The project's weapon fitting table, as one shared asset.
    ///
    /// One asset rather than a copy of the array on every prefab that can hold a weapon: the
    /// player, the police and any future armed NPC all read the same fit, so correcting a
    /// weapon's grip corrects it everywhere instead of in the one place someone remembered.
    /// </summary>
    [CreateAssetMenu(menuName = "Mini GTA/Weapon Library", fileName = "WeaponLibrary")]
    public class WeaponLibrary : ScriptableObject
    {
        public WeaponDefinition[] Weapons = new WeaponDefinition[0];

        public WeaponDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id) || Weapons == null) return null;
            for (int i = 0; i < Weapons.Length; i++)
                if (Weapons[i] != null && Weapons[i].Id == id) return Weapons[i];
            return null;
        }
    }
}

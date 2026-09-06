using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>One control's customised placement.</summary>
    [System.Serializable]
    public class HudControlLayout
    {
        /// <summary>Stable identifier. The GameObject name of the control.</summary>
        public string Id;

        /// <summary>Offset from the control's authored position, in canvas units.</summary>
        public float OffsetX;
        public float OffsetY;

        /// <summary>Multiplier on the control's authored size. 1 = as designed.</summary>
        public float Scale = 1f;
    }

    /// <summary>Serialised form. A list, because Unity's JsonUtility cannot write a dictionary.</summary>
    [System.Serializable]
    public class HudLayoutData
    {
        public List<HudControlLayout> Controls = new List<HudControlLayout>();
    }

    /// <summary>
    /// Where the player has moved and resized their touch controls.
    ///
    /// <b>Offsets and multipliers, not absolute positions.</b> A saved absolute rect would be
    /// wrong the moment the authored layout changed -- every rebuild of the interface moves the
    /// defaults, and a save file from before that would pin the buttons to wherever they used
    /// to be, including off the edge of a different-shaped screen. Storing the delta means a
    /// customised layout survives the HUD being redesigned underneath it, and a player who
    /// never touched the settings is unaffected by this system existing at all.
    ///
    /// <b>Keyed by control id rather than a field per control.</b> The HUD has gained buttons
    /// four times now; a struct with a field per control would have to be edited, migrated and
    /// version-bumped on each of those. A list of ids means a new button is simply a new id,
    /// an old id nobody recognises is ignored, and a missing one falls back to the default.
    /// </summary>
    public static class HudLayoutStore
    {
        readonly static Dictionary<string, HudControlLayout> Entries =
            new Dictionary<string, HudControlLayout>();

        /// <summary>Raised when any control's placement changes, so live widgets can re-apply.</summary>
        public static event System.Action Changed;

        /// <summary>Every control the settings screen offers, in the order it lists them.</summary>
        public static readonly string[] Customisable =
        {
            "MoveStick",
            "Btn_Attack",
            "Btn_Aim",
            "Btn_Jump",
            "Btn_Crouch",
            "Btn_Prone",
            "Btn_Sprint",
            "Btn_Reload",
            "Btn_WeaponSwitch",
            "Btn_Interact",
        };

        /// <summary>Human-readable names, for the settings list.</summary>
        public static string Label(string id) => id switch
        {
            "MoveStick" => "MOVE STICK",
            "Btn_Attack" => "FIRE",
            "Btn_Aim" => "AIM",
            "Btn_Jump" => "JUMP",
            "Btn_Crouch" => "CROUCH",
            "Btn_Prone" => "PRONE",
            "Btn_Sprint" => "RUN LOCK",
            "Btn_Reload" => "RELOAD",
            "Btn_WeaponSwitch" => "SWITCH WEAPON",
            "Btn_Interact" => "ENTER / EXIT",
            _ => id.ToUpperInvariant(),
        };

        public static HudControlLayout Get(string id)
        {
            if (Entries.TryGetValue(id, out var found)) return found;

            var fresh = new HudControlLayout { Id = id, OffsetX = 0f, OffsetY = 0f, Scale = 1f };
            Entries[id] = fresh;
            return fresh;
        }

        public static void Set(string id, float offsetX, float offsetY, float scale)
        {
            var entry = Get(id);
            entry.OffsetX = offsetX;
            entry.OffsetY = offsetY;
            entry.Scale = Mathf.Clamp(scale, 0.6f, 1.8f);
            Changed?.Invoke();
        }

        /// <summary>Puts every control back where it was designed to be.</summary>
        public static void ResetAll()
        {
            Entries.Clear();
            Changed?.Invoke();
        }

        /// <summary>True when the player has actually moved something.</summary>
        public static bool IsCustomised
        {
            get
            {
                foreach (var e in Entries.Values)
                    if (Mathf.Abs(e.OffsetX) > 0.01f || Mathf.Abs(e.OffsetY) > 0.01f
                        || Mathf.Abs(e.Scale - 1f) > 0.001f) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ persistence

        /// <summary>Flattens to the form SaveSystem writes.</summary>
        public static HudLayoutData Capture()
        {
            var data = new HudLayoutData();
            foreach (var e in Entries.Values)
            {
                if (Mathf.Abs(e.OffsetX) < 0.01f && Mathf.Abs(e.OffsetY) < 0.01f
                    && Mathf.Abs(e.Scale - 1f) < 0.001f) continue;   // defaults are not worth storing

                data.Controls.Add(new HudControlLayout
                {
                    Id = e.Id, OffsetX = e.OffsetX, OffsetY = e.OffsetY, Scale = e.Scale,
                });
            }
            return data;
        }

        /// <summary>
        /// Restores a saved layout.
        ///
        /// Unknown ids are dropped rather than kept: they are controls that no longer exist, and
        /// carrying them forward would resurrect them in the next save file forever.
        /// </summary>
        public static void Restore(HudLayoutData data)
        {
            Entries.Clear();

            if (data?.Controls != null)
            {
                foreach (var c in data.Controls)
                {
                    if (string.IsNullOrEmpty(c.Id)) continue;
                    if (System.Array.IndexOf(Customisable, c.Id) < 0) continue;

                    Entries[c.Id] = new HudControlLayout
                    {
                        Id = c.Id, OffsetX = c.OffsetX, OffsetY = c.OffsetY,
                        Scale = Mathf.Clamp(c.Scale <= 0f ? 1f : c.Scale, 0.6f, 1.8f),
                    };
                }
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Play mode only. Statics outlive a play session when domain reloading is off, so a
        /// layout from the previous run would leak into the next one and look like a load.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Entries.Clear();
            Changed = null;
        }
    }
}

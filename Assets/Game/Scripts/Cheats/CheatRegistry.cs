using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>What happened when a code was submitted.</summary>
    public enum CheatResult
    {
        /// <summary>This build cannot run cheats at all. Shipping builds land here.</summary>
        Unavailable,
        /// <summary>Cheats exist in this build but the hidden toggle is off.</summary>
        Disabled,
        /// <summary>No code by that name.</summary>
        Unknown,
        /// <summary>The code exists but could not run -- usually nothing to act on yet.</summary>
        Failed,
        Applied,
    }

    public enum CheatCategory { Player, Weapons, Money, Police, World, Vehicles, Crowd, Debug }

    /// <summary>One cheat code and the thing it does.</summary>
    public sealed class Cheat
    {
        public readonly string Code;
        public readonly string Description;
        public readonly CheatCategory Category;

        readonly Func<bool> _apply;

        public Cheat(string code, CheatCategory category, string description, Func<bool> apply)
        {
            Code = code;
            Category = category;
            Description = description;
            _apply = apply;
        }

        /// <summary>Runs it. False means the code was valid but had nothing to act on.</summary>
        public bool Invoke()
        {
            try { return _apply != null && _apply(); }
            catch (Exception e)
            {
                // A cheat that throws must not take the game down with it. These are debug
                // tools; half of them poke at systems that may legitimately not exist yet
                // (no vehicle, no weapon, no mission), and the correct response to that is a
                // failed cheat, not a crashed session.
                Debug.LogWarning("[Cheat] '" + Code + "' threw: " + e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// The cheat code table, and the two gates in front of it.
    ///
    /// <b>Two gates, not one, and they mean different things.</b>
    /// <see cref="Available"/> is a property of the build: a shipping player build has no way
    /// to reach any of this, whatever it types, because the standing resolution for item 9.4
    /// is that cheats are dev-gated and off by default. <see cref="Enabled"/> is a property of
    /// the session: even in a development build the codes do nothing until the hidden toggle
    /// is switched on, so a tester who does not know about them cannot trip one by accident.
    ///
    /// Kept as a static registry rather than a MonoBehaviour because the codes have to be
    /// declarable next to nothing -- a code is a name, a category, a sentence and a lambda --
    /// and because <see cref="Try"/> then works from a dev menu, a console, a unit test or an
    /// editor command without any of them needing an object in the scene.
    /// </summary>
    public static class CheatRegistry
    {
        static readonly Dictionary<string, Cheat> ByCode =
            new Dictionary<string, Cheat>(StringComparer.OrdinalIgnoreCase);
        static readonly List<Cheat> Ordered = new List<Cheat>();

        static bool _registered;

        /// <summary>
        /// Whether this build contains a usable cheat system at all.
        ///
        /// Development builds and the Editor only. A release APK returns
        /// <see cref="CheatResult.Unavailable"/> for every code, including the right ones.
        /// </summary>
        public static bool Available => Debug.isDebugBuild || Application.isEditor;

        /// <summary>The hidden per-session toggle. Off until something turns it on.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>Raised after every attempt, so a HUD can say what happened.</summary>
        public static event Action<string, CheatResult> Attempted;

        /// <summary>Raised when the toggle changes.</summary>
        public static event Action<bool> EnabledChanged;

        public static IReadOnlyList<Cheat> All { get { EnsureRegistered(); return Ordered; } }

        public static int Count { get { EnsureRegistered(); return Ordered.Count; } }

        /// <summary>
        /// Statics outlive a Play session when domain reloading is off, which would leave the
        /// toggle on from a previous run and the table full of stale closures.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Enabled = false;
            _registered = false;
            ByCode.Clear();
            Ordered.Clear();
            Attempted = null;
            EnabledChanged = null;
        }

        public static void SetEnabled(bool on)
        {
            if (!Available)
            {
                // Not merely ignored: saying so is the difference between "the toggle is
                // broken" and "this build has no cheats", which are very different bugs.
                Debug.LogWarning("[Cheat] this build cannot enable cheats (release build)");
                return;
            }

            if (Enabled == on) return;
            Enabled = on;

            Debug.Log("[Cheat] cheats " + (on ? "ENABLED" : "disabled")
                      + " (" + Count + " codes)");
            EnabledChanged?.Invoke(on);
        }

        public static void Register(Cheat cheat)
        {
            if (cheat == null || string.IsNullOrEmpty(cheat.Code)) return;

            if (ByCode.ContainsKey(cheat.Code))
            {
                Debug.LogWarning("[Cheat] duplicate code '" + cheat.Code + "' ignored");
                return;
            }

            ByCode[cheat.Code] = cheat;
            Ordered.Add(cheat);
        }

        public static Cheat Find(string code)
        {
            EnsureRegistered();
            if (string.IsNullOrEmpty(code)) return null;
            return ByCode.TryGetValue(code.Trim(), out var c) ? c : null;
        }

        /// <summary>
        /// Submits a code. Every path reports through <see cref="Attempted"/> so the caller
        /// does not have to duplicate the feedback logic.
        /// </summary>
        public static CheatResult Try(string code)
        {
            EnsureRegistered();

            CheatResult result;

            if (!Available) result = CheatResult.Unavailable;
            else if (!Enabled) result = CheatResult.Disabled;
            else
            {
                var cheat = Find(code);
                if (cheat == null) result = CheatResult.Unknown;
                else result = cheat.Invoke() ? CheatResult.Applied : CheatResult.Failed;
            }

            Attempted?.Invoke(code, result);
            return result;
        }

        static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            CheatCodes.RegisterAll();
        }
    }
}

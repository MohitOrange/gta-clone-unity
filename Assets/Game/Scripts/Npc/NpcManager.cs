using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// The registry of everyone in town worth talking to.
    ///
    /// <b>Static, not a MonoBehaviour singleton.</b> Every other manager in this project is a
    /// component on GameSystems because it owns per-frame behaviour -- dispatching police,
    /// ticking the day cycle, running a mission. This one owns no behaviour at all: NPCs
    /// register themselves on enable and answer for themselves in their own Update. A registry
    /// with nothing to tick does not need a GameObject, and making it static means an NPC can
    /// register before GameSystems has finished waking up.
    ///
    /// <see cref="MissionGiver"/> is deliberately left alone. The four Phase 4 contacts are
    /// beacon-and-radius fixtures with their own art; these are converted pedestrians. They
    /// answer the same Interact button through the same arbiter, which is where it matters.
    /// </summary>
    public static class NpcManager
    {
        static readonly List<TownNpc> Npcs = new List<TownNpc>();

        /// <summary>Everyone currently registered. Order is registration order, not stable.</summary>
        public static IReadOnlyList<TownNpc> All => Npcs;

        public static int Count => Npcs.Count;

        /// <summary>How many of them hand out work.</summary>
        public static int QuestGiverCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Npcs.Count; i++)
                    if (Npcs[i] != null && Npcs[i].IsQuestGiver) n++;
                return n;
            }
        }

        /// <summary>
        /// Statics outlive a Play mode session when domain reloading is off, which would leave
        /// the registry full of destroyed objects from the previous run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Npcs.Clear();

        public static void Register(TownNpc npc)
        {
            if (npc == null || Npcs.Contains(npc)) return;
            Npcs.Add(npc);
        }

        public static void Unregister(TownNpc npc)
        {
            if (npc == null) return;
            Npcs.Remove(npc);
        }

        public static TownNpc Find(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;

            for (int i = 0; i < Npcs.Count; i++)
                if (Npcs[i] != null && Npcs[i].NpcId == npcId) return Npcs[i];

            return null;
        }

        /// <summary>The NPC who hands out this mission, or null if nobody does.</summary>
        public static TownNpc GiverOf(string missionId)
        {
            if (string.IsNullOrEmpty(missionId)) return null;

            for (int i = 0; i < Npcs.Count; i++)
            {
                var npc = Npcs[i];
                if (npc != null && npc.Mission != null && npc.Mission.MissionId == missionId)
                    return npc;
            }
            return null;
        }

        public static TownNpc Nearest(Vector3 position, float maxDistance = float.MaxValue)
        {
            TownNpc best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < Npcs.Count; i++)
            {
                var npc = Npcs[i];
                if (npc == null) continue;

                float sqr = (npc.transform.position - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = npc;
            }
            return best;
        }
    }
}

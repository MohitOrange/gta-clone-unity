using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Turns part of the ambient crowd into people you can talk to.
    ///
    /// <b>Conversion, not spawning.</b> Phase 11 left 66 pedestrians walking the city, each
    /// already carrying a body from CharacterCatalog, an Animator and a Pedestrian brain. The
    /// task asked for 50+ interactable NPCs; adding fifty *more* characters would put fifty
    /// more skinned meshes on a renderer budget that PHASE11 already doubled. Adding a
    /// component and a few strings to people who are already there costs nothing extra to draw.
    ///
    /// Selection is deterministic -- pedestrians are sorted by position before conversion --
    /// so the same individuals become the same named characters on every rebuild, and a save
    /// referencing "npc.joe" still finds Joe where the player left him.
    /// </summary>
    public static class NpcBuilder
    {
        const string ScenePath = "Assets/Scenes/City.unity";

        /// <summary>
        /// The recurring cast the Phase 4 mission list hands work out through.
        ///
        /// Every one of the thirteen names the mission list references is here, so no mission
        /// can end up an orphan with no entry point in the world. Their <c>Mission</c> hooks
        /// are left null at this step -- the missions themselves are Step 3 -- and the wiring
        /// pass fills them in by id.
        /// </summary>
        struct Cast
        {
            public string Id, Name, Trade;
            public string[] Lines;
        }

        static readonly Cast[] Roster =
        {
            new Cast { Id = "npc.joe", Name = "Mechanic Joe", Trade = "mechanic", Lines = new[]
            {
                "You look like someone who needs work more than I need help.",
                "Shop's mine, the debts are somebody else's. Long story.",
                "Stick around. Things break around here constantly.",
            }},
            new Cast { Id = "npc.anita", Name = "Shopkeeper Anita", Trade = "shopkeeper", Lines = new[]
            {
                "Careful in here, I just mopped.",
                "Third break-in this month. I'm running out of glass.",
                "You seem capable. That's rarer than you'd think.",
            }},
            new Cast { Id = "npc.ray", Name = "Gang Contact Ray", Trade = "gang", Lines = new[]
            {
                "You're either lost or looking. Which one?",
                "This block used to be ours. Used to be.",
                "Prove you're useful and we'll talk properly.",
            }},
            new Cast { Id = "npc.nico", Name = "Dealer Nico", Trade = "dealer", Lines = new[]
            {
                "Don't stand so close. People watch this corner.",
                "I move things. I don't ask what, you don't ask why.",
                "Keep your hands visible and we'll get along.",
            }},
            new Cast { Id = "npc.marcus", Name = "Fence Marcus", Trade = "fence", Lines = new[]
            {
                "If you're selling, I'm listening. If you're asking, I'm not.",
                "Everything in this city has a price and a previous owner.",
                "I like people who finish things. Are you one?",
            }},
            new Cast { Id = "npc.kade", Name = "Officer Kade", Trade = "police", Lines = new[]
            {
                "Keep it moving. Or don't -- I'm bored either way.",
                "Half this precinct is paperwork and the other half is worse.",
                "You want to be useful? There's a board with names on it.",
            }},
            new Cast { Id = "npc.whisper", Name = "Contact Whisper", Trade = "fixer", Lines = new[]
            {
                "Don't say my name out loud. That's the whole point of it.",
                "I know things. Knowing them is cheap. Acting is expensive.",
                "When you're ready to earn properly, find me here.",
            }},
            new Cast { Id = "npc.mia", Name = "Florist Mia", Trade = "florist", Lines = new[]
            {
                "Careful, those are the expensive ones.",
                "Everyone buys flowers for an apology. Nobody buys them early.",
                "If you're heading across town anyway, I might have an errand.",
            }},
            new Cast { Id = "npc.dale", Name = "Mail Carrier Dale", Trade = "mail", Lines = new[]
            {
                "Route's forty blocks and the van died in the spring.",
                "Half my packages end up in the wrong district.",
                "You walking that way? Might be worth your while.",
            }},
            new Cast { Id = "npc.lenny", Name = "Record Shop Lenny", Trade = "records", Lines = new[]
            {
                "Don't touch the sleeves without asking.",
                "People throw out treasure and buy rubbish. Every week.",
                "There's pressings out there worth more than my rent.",
            }},
            new Cast { Id = "npc.rex", Name = "Junkyard Rex", Trade = "junkyard", Lines = new[]
            {
                "Mind the dog. He's friendly. Mostly.",
                "One man's wreck is my entire retirement plan.",
                "You want work, there's scrap out there with my name on it.",
            }},
            new Cast { Id = "npc.suki", Name = "Garage Owner Suki", Trade = "garage", Lines = new[]
            {
                "If it's leaking, I can fix it. If it's on fire, maybe.",
                "Parts are scarce. Good parts are rarer than honest customers.",
                "Bring me something rare and we'll discuss a discount.",
            }},
            new Cast { Id = "npc.herb", Name = "Pawn Shop Herb", Trade = "pawn", Lines = new[]
            {
                "Everything behind that glass has a story I didn't ask for.",
                "I buy low. I sell reluctantly. That's the business.",
                "Antiques, mostly. People don't know what they're throwing away.",
            }},
        };

        /// <summary>Filler townspeople. Flavour only -- none of them hand out work.</summary>
        static readonly string[] FirstNames =
        {
            "Ada", "Bruno", "Cass", "Dev", "Elin", "Fitz", "Greta", "Hal", "Ivy", "Jonas",
            "Kira", "Luis", "Mona", "Nils", "Oona", "Pax", "Quinn", "Rosa", "Sol", "Tam",
            "Ula", "Vic", "Wren", "Xan", "Yara", "Zeb", "Beck", "Cleo", "Dot", "Emre",
            "Fay", "Gus", "Hana", "Iggy", "Jo", "Kai", "Lena",
        };

        static readonly string[][] SmallTalk =
        {
            new[] { "Buses stopped running this route. Nobody told the timetable.",
                    "You get used to the noise. Eventually." },
            new[] { "Rent went up again. Twice this year.",
                    "Everyone says they're leaving. Nobody does." },
            new[] { "Watch the crossing on the avenue, drivers don't.",
                    "Third near-miss I've seen today." },
            new[] { "Was a decent neighbourhood once.",
                    "Still is, if you squint and it's early." },
            new[] { "You're not from the north blocks, are you?",
                    "Thought not. You walk differently." },
            new[] { "Sirens again. You stop hearing them after a while.",
                    "That's the part that worries me." },
            new[] { "Coffee here is terrible and I buy it daily.",
                    "That's loyalty, or it's a medical problem." },
        };

        [MenuItem("Tools/Mini GTA/16. Build Town NPCs", priority = 136)]
        public static void Build()
        {
            var pedestrians = Object.FindObjectsByType<Pedestrian>(FindObjectsInactive.Include);
            if (pedestrians.Length == 0)
            {
                Debug.LogError("[NPCs] No pedestrians in the scene. Run step 6 and the traffic "
                               + "step first -- this converts the existing crowd, it does not "
                               + "spawn anyone.");
                return;
            }

            // Deterministic order, so the same body becomes the same character every rebuild.
            var ordered = new List<Pedestrian>(pedestrians);
            ordered.Sort((a, b) =>
            {
                Vector3 pa = a.transform.position, pb = b.transform.position;
                int byX = pa.x.CompareTo(pb.x);
                return byX != 0 ? byX : pa.z.CompareTo(pb.z);
            });

            // Clear any previous pass so re-running does not stack components.
            int cleared = 0;
            foreach (var existing in Object.FindObjectsByType<TownNpc>(FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(existing);
                cleared++;
            }

            int named = 0, filler = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                var ped = ordered[i];
                if (ped == null) continue;

                var npc = ped.gameObject.AddComponent<TownNpc>();

                if (i < Roster.Length)
                {
                    var cast = Roster[i];
                    npc.NpcId = cast.Id;
                    npc.DisplayName = cast.Name;
                    npc.Lines = ToLines(cast.Lines);
                    ped.gameObject.name = "NPC_" + cast.Name.Replace(" ", "");
                    named++;
                }
                else
                {
                    int f = i - Roster.Length;
                    if (f >= FirstNames.Length) { Object.DestroyImmediate(npc); continue; }

                    npc.NpcId = "npc.town" + f.ToString("00");
                    npc.DisplayName = FirstNames[f];
                    npc.Lines = ToLines(SmallTalk[f % SmallTalk.Length]);
                    ped.gameObject.name = "NPC_" + FirstNames[f];
                    filler++;
                }
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[NPCs] " + (named + filler) + " interactable townspeople"
                      + " (" + named + " named cast, " + filler + " flavour)"
                      + (cleared > 0 ? ", replacing " + cleared + " from a previous pass" : "")
                      + ".\n  " + (pedestrians.Length - named - filler)
                      + " pedestrians left as ambient crowd."
                      + "\n  Mission hooks are null until the mission wiring step runs.");
        }

        static DialogueLine[] ToLines(string[] text)
        {
            var lines = new DialogueLine[text.Length];
            for (int i = 0; i < text.Length; i++) lines[i] = new DialogueLine { Text = text[i] };
            return lines;
        }
    }
}

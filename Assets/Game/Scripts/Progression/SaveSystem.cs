using System.IO;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Everything that survives a session. Kept flat and primitive so JsonUtility can handle
    /// it without custom serialisers.
    /// </summary>
    /// <summary>One vehicle in the player's garage, with whatever has been fitted to it.</summary>
    [System.Serializable]
    public class OwnedVehicleData
    {
        /// <summary>Prefab name under Assets/Game/Prefabs, e.g. "Car_0".</summary>
        public string PrefabId = "";
        public string DisplayName = "";

        /// <summary>Upgrade steps fitted, 0 = stock.</summary>
        public int SpeedLevel;
        public int ArmorLevel;

        public bool HasCustomColour;
        public float ColourR = 1f, ColourG = 1f, ColourB = 1f;
    }

    [System.Serializable]
    public class SaveData
    {
        /// <summary>Bumped when fields are added. Older saves still load; new fields default.</summary>
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;

        public int Money;
        public int Xp;
        public int Level = 1;

        /// <summary>
        /// The name the player types into the lobby's profile screen. Added in v4.
        ///
        /// A v3 file loads with this empty, and <see cref="PlayerProgress.CleanProfileName"/>
        /// turns empty into the default name -- so an older save gets a name rather than a
        /// blank chip, and the file is rewritten as v4 on the next write.
        /// </summary>
        public string ProfileName = "";

        public string[] Unlocked = System.Array.Empty<string>();
        public string[] CompletedMissions = System.Array.Empty<string>();

        public string[] OwnedItems = System.Array.Empty<string>();
        public string[] OwnedProperties = System.Array.Empty<string>();
        public string ActiveSkin = "";

        /// <summary>Remove Ads entitlement. In a real build this is restored from the store.</summary>
        public bool AdsRemoved;

        public OwnedVehicleData[] Garage = System.Array.Empty<OwnedVehicleData>();

        /// <summary>Mission the player had accepted but not finished, or empty.</summary>
        public string ActiveMissionId = "";

        public float PlayerX, PlayerY, PlayerZ;
        public bool HasPosition;

        public string SavedAtUtc = "";
    }

    /// <summary>
    /// Reads and writes the save file.
    ///
    /// Uses <see cref="Application.persistentDataPath"/>, which is the browser's IndexedDB on
    /// WebGL, app-private storage on Android and AppData on Windows -- the same role
    /// localStorage plays for a web build, without tying the game to one platform's API.
    ///
    /// Writes go to a temporary file first and are then swapped in. A save interrupted
    /// half-written would otherwise leave unparseable JSON and lose all progress.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        static SaveSystem _instance;

        public static SaveSystem Instance
        {
            get
            {
                if (_instance == null) _instance = FindAnyObjectByType<SaveSystem>();
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("File")]
        public string FileName = "minigta_save.json";

        [Header("Autosave")]
        [Tooltip("Save automatically when a mission finishes and on quit.")]
        public bool AutoSave = true;
        [Tooltip("Also save on this interval while playing. 0 disables.")]
        public float AutoSaveInterval = 60f;

        [Header("Load safety")]
        [Tooltip("Seconds of invulnerability after loading. A saved position can easily be in a " +
                 "live traffic lane, and being run over during the load is not a fair death.")]
        public float LoadGraceSeconds = 4f;
        [Tooltip("Drop the restored position onto whatever ground is under it, so a save taken " +
                 "mid-jump or on a since-changed map does not spawn the player inside geometry.")]
        public bool SnapLoadToGround = true;

        [Tooltip("Refuse a restored position that is outside the playable world and use the " +
                 "scene's spawn point instead. Guards against a save file that predates a map " +
                 "change, or one written somewhere the player cannot walk back from.")]
        public bool RejectOutOfWorldLoad = true;

        [Tooltip("Slack outside the terrain that still counts as the world. Zero means the " +
                 "1600x1600 terrain footprint exactly. The ocean plane extends past that edge, " +
                 "so raise this if a boat sailed offshore should keep its saved position.")]
        public float WorldEdgeMargin;

        [Tooltip("How close a rejected position must be to an interior's entry point before it " +
                 "is treated as 'they were in that room' and sent to that room's door.")]
        public float InteriorMatchRadius = 30f;

        [Header("Refs")]
        public GameObject Player;

        float _autoSaveTimer;

        /// <summary>
        /// Set once the existing save has been read (or confirmed absent).
        ///
        /// Until then every write is refused. Autosave triggers -- losing focus, pausing,
        /// quitting -- can all fire before Start has loaded, and writing the freshly
        /// constructed default progress at that moment silently destroys the player's save.
        /// </summary>
        bool _loadAttempted;

        Vector3 _spawnPoint;
        bool _hasSpawnPoint;

        public string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        public bool SaveExists => File.Exists(SavePath);

        public event System.Action Saved;
        public event System.Action<SaveData> Loaded;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Start()
        {
            if (Player == null) Player = GameObject.FindWithTag("Player");
            _autoSaveTimer = AutoSaveInterval;

            // The scene's authored spawn, captured before Load can move the player off it.
            // It is the fallback when a save file names somewhere they cannot stand.
            if (Player != null)
            {
                _spawnPoint = Player.transform.position;
                _hasSpawnPoint = true;
            }

            if (SaveExists) Load();

            // Writes are unblocked only after this point, whether or not a file existed.
            _loadAttempted = true;
        }

        void Update()
        {
            if (!AutoSave || AutoSaveInterval <= 0f) return;

            _autoSaveTimer -= Time.deltaTime;
            if (_autoSaveTimer > 0f) return;

            _autoSaveTimer = AutoSaveInterval;
            Save();
        }

        void OnApplicationQuit()
        {
            if (AutoSave) Save();
        }

        void OnApplicationPause(bool paused)
        {
            // On mobile this is the only reliable "we are going away" signal.
            if (paused && AutoSave) Save();
        }

        // -------------------------------------------------------------------- io

        public void Save()
        {
            // Refuse to write over a save we have not read yet. This is the guard that stops
            // an early autosave from replacing real progress with a blank slate.
            if (!_loadAttempted) return;

            var progress = PlayerProgress.Instance;
            if (progress == null) return;

            var data = progress.CaptureSave();

            var missions = MissionManager.Instance;
            data.ActiveMissionId = missions != null && missions.Active != null
                ? missions.Active.MissionId
                : "";

            var garage = Garage.Instance;
            if (garage != null) data.Garage = garage.Capture();

            if (Player != null)
            {
                Vector3 p = Player.transform.position;

                // Never write a position that is inside an interior.
                //
                // Interiors are rooms parked in dead air at x = -420, y = 200, and nothing in
                // the save file records that the player was in one. Restoring that raw position
                // put them in an off-map room with InteriorManager convinced they were outdoors
                // -- and InteriorExit refuses to fire unless IsInside is true, so there was no
                // way out. An unrecoverable save, reachable by walking into the safehouse,
                // which saves on entry by design.
                //
                // The doorway they came in through is the honest answer: it is where they were
                // standing a moment earlier, and it is the same philosophy the rest of this
                // file already follows -- a mission in progress is re-offered rather than
                // resumed, so a shop visit in progress puts you back on its doorstep.
                var interiors = InteriorManager.Instance;
                if (interiors != null && interiors.IsInside && interiors.Current != null)
                {
                    Vector3 outside = interiors.Current.ReturnPosition;
                    if (outside.sqrMagnitude > 0.01f) p = outside;
                }

                data.PlayerX = p.x; data.PlayerY = p.y; data.PlayerZ = p.z;
                data.HasPosition = true;
            }

            data.SavedAtUtc = System.DateTime.UtcNow.ToString("o");

            try
            {
                string json = JsonUtility.ToJson(data, true);
                string temp = SavePath + ".tmp";

                File.WriteAllText(temp, json);
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(temp, SavePath);

                Saved?.Invoke();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Save] Failed to write " + SavePath + ": " + e.Message);
            }
        }

        /// <summary>
        /// Removes the save file. Used by New Game.
        ///
        /// Only the file goes; the live progress objects are the caller's to reset. Doing both
        /// here would make Delete unsafe to call from anywhere that just wants the file gone.
        /// </summary>
        public bool Delete()
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);

                // A crash between the write and the swap can leave this behind, and it would
                // otherwise sit next to a deleted save forever.
                string temp = SavePath + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);

                Debug.Log("[Save] Deleted " + SavePath);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Save] Could not delete " + SavePath + ": " + e.Message);
                return false;
            }
        }

        public SaveData Load()
        {
            if (!SaveExists) return null;

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Save] Corrupt save at " + SavePath + ": " + e.Message
                               + " -- starting fresh.");
                return null;
            }

            if (data == null) return null;

            if (data.Version > SaveData.CurrentVersion)
            {
                Debug.LogWarning("[Save] Save was written by a newer build (v" + data.Version
                                 + "); ignoring it rather than risking bad state.");
                return null;
            }

            PlayerProgress.Instance?.RestoreFrom(data);
            Garage.Instance?.Restore(data.Garage);

            if (data.HasPosition && Player != null)
            {
                var position = new Vector3(data.PlayerX, data.PlayerY, data.PlayerZ);

                if (RejectOutOfWorldLoad && !IsInsideWorld(position))
                {
                    Vector3 safe = FindSafeFallback(position);
                    Debug.LogWarning("[Save] Restored position " + position.ToString("F1")
                                     + " is outside the playable world; redirected to "
                                     + safe.ToString("F1") + ".");
                    position = safe;
                }

                if (SnapLoadToGround) position = SnapToGround(position);

                var controller = Player.GetComponent<PlayerController>();
                if (controller != null) controller.Warp(position);
                else Player.transform.position = position;

                GrantLoadGrace();
            }

            Loaded?.Invoke(data);
            Debug.Log("[Save] Loaded from " + SavePath + " (level " + data.Level
                      + ", $" + data.Money + ", " + (data.CompletedMissions?.Length ?? 0)
                      + " missions done)");
            return data;
        }

        /// <summary>
        /// The nearest safe place to put a player whose saved position was rejected.
        ///
        /// Tries hardest first. A position inside one of the interiors almost certainly means
        /// the save was written in that room, so the honest answer is the street outside that
        /// room's own door -- not a generic spawn on the other side of the city. The doorstep
        /// expression is the one <see cref="Doorway"/> itself uses when it sends the player in,
        /// so they come back out exactly where they would have walked out.
        ///
        /// <c>Interior.ReturnPosition</c> is preferred when it is set, but it is runtime state:
        /// on a fresh boot nobody has entered anything yet, so it is zero and the doorway is
        /// what remains. Failing both, the scene's authored spawn.
        /// </summary>
        Vector3 FindSafeFallback(Vector3 rejected)
        {
            Interior match = null;
            float best = float.MaxValue;

            foreach (var interior in FindObjectsByType<Interior>(FindObjectsInactive.Include))
            {
                if (interior == null || interior.EntryPoint == null) continue;

                float d = Vector3.Distance(interior.EntryPoint.position, rejected);
                if (d >= best) continue;

                best = d;
                match = interior;
            }

            if (match != null && best <= InteriorMatchRadius)
            {
                if (match.ReturnPosition.sqrMagnitude > 0.01f) return match.ReturnPosition;

                foreach (var door in FindObjectsByType<Doorway>(FindObjectsInactive.Include))
                {
                    if (door == null || door.Target != match) continue;
                    return door.transform.position + door.transform.forward * 2f;
                }
            }

            return _hasSpawnPoint ? _spawnPoint : rejected;
        }

        /// <summary>
        /// Is this somewhere the player could actually be standing?
        ///
        /// <b>Bounded by the terrain, deliberately not by the city.</b> The city occupies
        /// x 524-1268 of a 1600 m map, and the beach, the coast road, the mountains and the
        /// offshore boat are all outside it and all legitimate places to save. Clamping to the
        /// city would teleport a player off their own boat.
        ///
        /// What this actually catches is the unrecoverable case: interiors are rooms parked in
        /// dead air at y = 200 and x = -420, and the lobby's display stage is at y = 400. The
        /// terrain is 160 m tall and its highest walkable point measures 119 m, so the altitude
        /// test alone separates them with 40 m to spare -- no hand-picked number involved.
        ///
        /// The horizontal test is the terrain footprint exactly (1600 x 1600). Note that the
        /// ocean plane reaches past that edge, so a boat sailed off the map would be redirected;
        /// raise <see cref="WorldEdgeMargin"/> if that ever matters.
        /// </summary>
        bool IsInsideWorld(Vector3 position)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return true;   // cannot judge

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            // Above the terrain's own ceiling means off-map: no building, hill or jump reaches it.
            if (position.y > origin.y + size.y) return false;

            return position.x >= origin.x - WorldEdgeMargin
                && position.x <= origin.x + size.x + WorldEdgeMargin
                && position.z >= origin.z - WorldEdgeMargin
                && position.z <= origin.z + size.z + WorldEdgeMargin;
        }

        /// <summary>
        /// Drops a position onto the ground beneath it. Starts above the point so a save taken
        /// while airborne still lands on the surface rather than under it.
        ///
        /// Skips anything belonging to the player: the cast begins above their own capsule, so
        /// a plain raycast would hit the player and "snap" them to the top of their own head.
        /// </summary>
        Vector3 SnapToGround(Vector3 position)
        {
            var hits = Physics.RaycastAll(position + Vector3.up * 10f, Vector3.down,
                                          60f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return position;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (Player != null && hit.collider.transform.IsChildOf(Player.transform)) continue;

                // Vehicles are not ground either -- landing on a car roof is not a safe spawn.
                if (hit.collider.GetComponentInParent<Vehicle>() != null) continue;

                return hit.point + Vector3.up * 0.2f;
            }

            return position;
        }

        void GrantLoadGrace()
        {
            if (LoadGraceSeconds <= 0f || Player == null) return;

            var health = Player.GetComponent<Health>();
            if (health == null) return;

            StartCoroutine(LoadGraceRoutine(health));
        }

        System.Collections.IEnumerator LoadGraceRoutine(Health health)
        {
            health.Invulnerable = true;
            yield return new WaitForSeconds(LoadGraceSeconds);
            if (health != null) health.Invulnerable = false;
        }

        /// <summary>Delete the save and reset progress. Exposed for a "new game" button.</summary>
        public void DeleteSave()
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Save] Could not delete save: " + e.Message);
            }

            PlayerProgress.Instance?.ResetProgress();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGTA
{
    /// <summary>
    /// A schematic map of the whole city with live blips.
    ///
    /// Drawn entirely in UI from the known grid constants rather than by rendering the world
    /// with a second camera. On a phone that saves a whole extra render pass and a set of draw
    /// calls, and for a city this small a fixed whole-map view is more useful than a scrolling
    /// minimap -- you can always see where the job is relative to where you are.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        [Header("Area shown, in world XZ")]
        public Vector2 WorldMin = new Vector2(340f, 280f);
        public Vector2 WorldMax = new Vector2(790f, 730f);

        [Header("Wiring")]
        public RectTransform MapArea;
        public RectTransform PlayerBlip;
        public RectTransform ObjectiveBlip;
        public RectTransform GiverBlipTemplate;
        public RectTransform PoliceBlipTemplate;
        [Tooltip("Weapons lying in the world. Taken and dropped during play, so this pool is "
                 + "refreshed on the rescan timer rather than cached once.")]
        public RectTransform WeaponBlipTemplate;
        [Tooltip("Shop counters.")]
        public RectTransform ShopBlipTemplate;
        [Tooltip("Properties that are for sale.")]
        public RectTransform PropertyBlipTemplate;
        [Tooltip("Boats the player can take. Found by Vehicle.Kind, so a boat moved or added "
                 + "by the level builder appears without any extra wiring.")]
        public RectTransform BoatBlipTemplate;
        [Tooltip("Helicopters. Shows nothing until one is actually placed in the scene.")]
        public RectTransform HelicopterBlipTemplate;
        [Tooltip("Walk-in doorways -- shop and building entrances on the street.")]
        public RectTransform DoorBlipTemplate;

        [Header("Pools")]
        [Tooltip("Maximum police blips drawn at once.")]
        public int MaxPoliceBlips = 12;
        public int MaxGiverBlips = 8;
        public int MaxWeaponBlips = 32;
        public int MaxShopBlips = 8;
        public int MaxPropertyBlips = 8;
        public int MaxVehicleBlips = 8;
        public int MaxDoorBlips = 16;

        [Header("Scanning")]
        [Tooltip("Seconds between rescans of the world for things that come and go -- police "
                 + "units and weapons on the ground. Blip positions still update every frame; "
                 + "this only controls how often the set of things being tracked is rebuilt.")]
        public float RescanInterval = 0.5f;

        Transform _player;
        readonly List<RectTransform> _policePool = new List<RectTransform>();
        readonly List<RectTransform> _giverPool = new List<RectTransform>();
        readonly List<RectTransform> _weaponPool = new List<RectTransform>();
        readonly List<RectTransform> _shopPool = new List<RectTransform>();
        readonly List<RectTransform> _propertyPool = new List<RectTransform>();
        readonly List<RectTransform> _boatPool = new List<RectTransform>();
        readonly List<RectTransform> _heliPool = new List<RectTransform>();
        readonly List<RectTransform> _doorPool = new List<RectTransform>();

        MissionGiver[] _givers;
        Shop[] _shops;
        Property[] _properties;
        Transform[] _shopPoints;
        Transform[] _boats;
        Transform[] _helicopters;
        Doorway[] _doors;

        // Rebuilt on the rescan timer, not per frame. See RescanInterval.
        readonly List<Transform> _policeUnits = new List<Transform>();
        readonly List<WeaponPickup> _weapons = new List<WeaponPickup>();
        float _nextScan;

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) _player = player.transform;

            // Mission givers, shops and for-sale properties are level fixtures: they do not
            // appear or disappear during play, so they are found once.
            _givers = FindObjectsByType<MissionGiver>();
            // Shops are shown at the door you walk through, not at the counter.
            //
            // The counter lives inside the interior room, and the rooms are parked 3 km west of
            // the island so they are never in shot -- so a blip on the Shop itself mapped to
            // somewhere far off the map and got clamped into a little stack against the left
            // edge. Interior.Shop and Doorway.Target between them give the street entrance,
            // which is the position the player actually wants.
            _shops = FindObjectsByType<Shop>();
            _shopPoints = new Transform[_shops.Length];
            var doorways = FindObjectsByType<Doorway>();
            for (int i = 0; i < _shops.Length; i++)
            {
                _shopPoints[i] = _shops[i] != null ? _shops[i].transform : null;
                foreach (var d in doorways)
                {
                    if (d == null || d.Target == null || d.Target.Shop != _shops[i]) continue;
                    _shopPoints[i] = d.transform;
                    break;
                }
            }
            _properties = FindObjectsByType<Property>();
            _doors = FindObjectsByType<Doorway>();

            // Boats and helicopters are found by what they are, not by a hand-placed marker,
            // so the map follows the level builders rather than needing to be told twice.
            var vehicles = FindObjectsByType<Vehicle>();
            var boats = new List<Transform>();
            var helis = new List<Transform>();
            foreach (var v in vehicles)
            {
                if (v == null) continue;
                if (v.Kind == VehicleKind.Boat) boats.Add(v.transform);
                else if (v.Kind == VehicleKind.Helicopter) helis.Add(v.transform);
            }
            _boats = boats.ToArray();
            _helicopters = helis.ToArray();

            BuildPool(_policePool, PoliceBlipTemplate, MaxPoliceBlips);
            BuildPool(_giverPool, GiverBlipTemplate, MaxGiverBlips);
            BuildPool(_weaponPool, WeaponBlipTemplate, MaxWeaponBlips);
            BuildPool(_shopPool, ShopBlipTemplate, MaxShopBlips);
            BuildPool(_propertyPool, PropertyBlipTemplate, MaxPropertyBlips);
            BuildPool(_boatPool, BoatBlipTemplate, MaxVehicleBlips);
            BuildPool(_heliPool, HelicopterBlipTemplate, MaxVehicleBlips);
            BuildPool(_doorPool, DoorBlipTemplate, MaxDoorBlips);

            HideTemplate(PoliceBlipTemplate);
            HideTemplate(GiverBlipTemplate);
            HideTemplate(WeaponBlipTemplate);
            HideTemplate(ShopBlipTemplate);
            HideTemplate(PropertyBlipTemplate);
            HideTemplate(BoatBlipTemplate);
            HideTemplate(HelicopterBlipTemplate);
            HideTemplate(DoorBlipTemplate);

            Rescan();
        }

        static void HideTemplate(RectTransform t)
        {
            if (t != null) t.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rebuilds the sets of things that come and go.
        ///
        /// <b>This used to be a FindObjectsByType every single frame.</b> UpdatePolice called
        /// it unconditionally in Update, which is a full scene scan plus an array allocation
        /// at 60 Hz, in a scene that carries 300 pedestrians and 2,000 prefab instances.
        /// Twice a second is far more often than a police car or a dropped pistol needs, and
        /// the blips themselves still follow their targets every frame.
        /// </summary>
        void Rescan()
        {
            _policeUnits.Clear();
            foreach (var officer in FindObjectsByType<PoliceOfficer>())
                if (officer != null) _policeUnits.Add(officer.transform);
            foreach (var car in FindObjectsByType<PoliceVehicle>())
                if (car != null) _policeUnits.Add(car.transform);

            _weapons.Clear();
            foreach (var w in FindObjectsByType<WeaponPickup>())
                if (w != null) _weapons.Add(w);
        }

        void BuildPool(List<RectTransform> pool, RectTransform template, int count)
        {
            if (template == null) return;

            for (int i = 0; i < count; i++)
            {
                var copy = Instantiate(template, template.parent);
                copy.name = template.name + "_" + i;
                copy.gameObject.SetActive(false);
                pool.Add(copy);
            }
        }

        void Update()
        {
            if (MapArea == null) return;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + Mathf.Max(0.1f, RescanInterval);
                Rescan();
            }

            UpdatePlayer();
            UpdateObjective();
            UpdateGivers();
            UpdatePolice();
            UpdateWeapons();
            UpdateFixtures();
        }

        /// <summary>
        /// Weapons lying in the world.
        ///
        /// A pickup that has been collected is disabled rather than destroyed (it respawns on
        /// a timer), so activeInHierarchy is the test for "is this actually there to be
        /// picked up" -- drawing a blip for a collected weapon would send the player to an
        /// empty pavement.
        /// </summary>
        void UpdateWeapons()
        {
            int used = 0;
            for (int i = 0; i < _weapons.Count && used < _weaponPool.Count; i++)
            {
                var w = _weapons[i];
                if (w == null || !w.gameObject.activeInHierarchy) continue;

                var blip = _weaponPool[used++];
                blip.gameObject.SetActive(true);
                blip.anchoredPosition = WorldToMap(w.transform.position);
                Tag(blip, w.transform.position, w);
            }
            for (int i = used; i < _weaponPool.Count; i++) _weaponPool[i].gameObject.SetActive(false);
        }

        /// <summary>Shops and for-sale properties. Fixed positions, so this only places them.</summary>
        void UpdateFixtures()
        {
            PlaceTransforms(_shopPool, _shopPoints);
            PlaceFixed(_propertyPool, _properties);
            PlaceFixed(_doorPool, _doors);
            PlaceTransforms(_boatPool, _boats);
            PlaceTransforms(_heliPool, _helicopters);
        }

        /// <summary>As PlaceFixed, for things tracked by Transform rather than by component.</summary>
        void PlaceTransforms(List<RectTransform> pool, Transform[] items)
        {
            if (items == null) return;

            int used = 0;
            for (int i = 0; i < items.Length && used < pool.Count; i++)
            {
                if (items[i] == null || !items[i].gameObject.activeInHierarchy) continue;
                var blip = pool[used++];
                blip.gameObject.SetActive(true);
                blip.anchoredPosition = WorldToMap(items[i].position);
                Tag(blip, items[i].position, items[i]);
            }
            for (int i = used; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// Records what a placed blip stands for, so a tap on it can become a waypoint.
        ///
        /// The UI position alone is not enough: WorldToMap clamps anything outside the bounds
        /// to the edge, so it is not invertible. Carrying the world position forward is both
        /// simpler and exact.
        /// </summary>
        static void Tag(RectTransform blip, Vector3 world, Object owner)
        {
            var tag = blip.GetComponent<MapBlip>();
            if (tag == null) return;
            tag.World = world;
            tag.Owner = owner;
        }

        void PlaceFixed<T>(List<RectTransform> pool, T[] items) where T : Component
        {
            if (items == null) return;

            int used = 0;
            for (int i = 0; i < items.Length && used < pool.Count; i++)
            {
                if (items[i] == null) continue;
                var blip = pool[used++];
                blip.gameObject.SetActive(true);
                blip.anchoredPosition = WorldToMap(items[i].transform.position);
                Tag(blip, items[i].transform.position, items[i]);
            }
            for (int i = used; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }

        void UpdatePlayer()
        {
            if (PlayerBlip == null || _player == null) return;

            PlayerBlip.anchoredPosition = WorldToMap(_player.position);
            // Rotate the arrow to the player's heading. UI Z is clockwise, world Y is not.
            PlayerBlip.localRotation = Quaternion.Euler(0f, 0f, -_player.eulerAngles.y);
        }

        void UpdateObjective()
        {
            if (ObjectiveBlip == null) return;

            var mission = MissionManager.Instance != null ? MissionManager.Instance.Active : null;
            bool show = mission != null
                        && mission.State == MissionState.Running
                        && mission.HasObjectivePoint;

            ObjectiveBlip.gameObject.SetActive(show);
            if (show) ObjectiveBlip.anchoredPosition = WorldToMap(mission.ObjectivePoint);
        }

        void UpdateGivers()
        {
            if (_givers == null) return;

            int used = 0;
            var manager = MissionManager.Instance;

            for (int i = 0; i < _givers.Length && used < _giverPool.Count; i++)
            {
                var giver = _givers[i];
                if (giver == null || giver.Mission == null) continue;

                // Only advertise jobs the player could actually take.
                if (manager == null || !manager.CanAccept(giver.Mission)) continue;

                var blip = _giverPool[used++];
                blip.gameObject.SetActive(true);
                blip.anchoredPosition = WorldToMap(giver.transform.position);
                Tag(blip, giver.transform.position, giver);
            }

            for (int i = used; i < _giverPool.Count; i++) _giverPool[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// Every police officer and cruiser in the world.
        ///
        /// <b>This used to track PolicePursuit instead</b>, which only exists on a unit that is
        /// actively chasing the player -- so police appeared on the map only once they were
        /// already after you, which is exactly when knowing where they are stops being useful.
        /// Officers and cruisers are now shown whether or not a pursuit is running, so the map
        /// can be read to avoid them.
        /// </summary>
        void UpdatePolice()
        {
            int used = 0;
            for (int i = 0; i < _policeUnits.Count && used < _policePool.Count; i++)
            {
                var unit = _policeUnits[i];
                if (unit == null || !unit.gameObject.activeInHierarchy) continue;

                var blip = _policePool[used++];
                blip.gameObject.SetActive(true);
                blip.anchoredPosition = WorldToMap(unit.position);
                Tag(blip, unit.position, unit);
            }
            for (int i = used; i < _policePool.Count; i++) _policePool[i].gameObject.SetActive(false);
        }

        /// <summary>Maps a world position onto the map rect, clamped to its edges.</summary>
        public Vector2 WorldToMap(Vector3 world)
        {
            Vector2 size = MapArea.rect.size;

            float u = Mathf.InverseLerp(WorldMin.x, WorldMax.x, world.x);
            float v = Mathf.InverseLerp(WorldMin.y, WorldMax.y, world.z);

            // Clamp so something outside the district still shows at the boundary rather than
            // disappearing, which would read as "no police" when there very much are.
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            return new Vector2((u - 0.5f) * size.x, (v - 0.5f) * size.y);
        }
    }
}

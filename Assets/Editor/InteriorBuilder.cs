using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Builds the walk-in interiors, their shopkeepers and stock, and the doorways on the
    /// street that lead to them.
    ///
    /// Rooms are parked in dead air off the west edge of the terrain, above the ocean plane
    /// rather than under the terrain so the player's capsule cannot catch on world geometry
    /// when they arrive.
    ///
    /// <b>They must be beyond the camera's far clip from anywhere the player can reach.</b>
    /// This used to say they were "outside every camera frustum" at x -420, y 200, and that was
    /// simply wrong: the player spawns on the west side of the city facing west out to sea, the
    /// far clip is 900 m, and the farm sat 292 m away. A row of grey rooms hung in the sky over
    /// the ocean in the default view, which is what it looks like in any screenshot taken from
    /// the beach. Distance costs nothing, because the camera teleports into the room with the
    /// player -- being 3 km from the city is only ever visible as not being visible.
    /// </summary>
    public static class InteriorBuilder
    {
        const string MatDir = "Assets/Game/Materials";
        const string PrefabDir = "Assets/Game/Prefabs";

        /// <summary>
        /// Interiors live here: off-terrain, above the ocean plane, and far enough west that
        /// the whole farm is past the 900 m far clip from every reachable point in the world.
        /// The furthest west the player can get is the ocean plane edge at x -700, which leaves
        /// 1,900 m of margin.
        /// </summary>
        static readonly Vector3 InteriorOrigin = new Vector3(-2600f, 200f, 300f);
        const float InteriorSpacing = 60f;


        [MenuItem("Tools/Mini GTA/15. Build Interiors & Shops", priority = 135)]
        public static void Build()
        {
            var old = GameObject.Find("Interiors");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Interiors");
            var rooms = NewChild(root, "Rooms");
            var doors = NewChild(root, "Doorways");

            var mats = LoadMaterials();
            float streetY = CityBuilder.StreetY;

            // Shop doors sit on a named block's kerb rather than at an absolute coordinate.
            // Phase 11 expanded the grid from 5x5 to 9x9, which moved every block; the four
            // literals these used to be would have left three of the four doors inside a wall.
            int c = CityBuilder.CentreBlock;

            // --- 1. Gun shop -----------------------------------------------------------
            var gunRoom = BuildRoom(rooms, mats, 0, "GunShop", "Ammu-Mart",
                                    new Vector3(15f, 4.2f, 13f),
                                    new Color(0.40f, 0.37f, 0.33f));
            var gunShop = CreateGunShop(gunRoom.go);
            gunRoom.interior.Shop = gunShop;
            AddKeeper(gunRoom, CharacterCatalog.Keepers[0], gunShop, InteriorNpc.Role.Shopkeeper);
            BuildDoor(doors, mats, Door(c - 1, c - 2, Heading.South), 180f,
                      gunRoom.interior, "Ammu-Mart", null);

            // --- 2. Garage -------------------------------------------------------------
            var garageRoom = BuildRoom(rooms, mats, 1, "Garage", "Chop Shop",
                                       new Vector3(20f, 5f, 16f),
                                       new Color(0.34f, 0.35f, 0.38f));
            garageRoom.interior.IsGarage = true;
            var garageShop = CreateGarageShop(garageRoom.go);
            garageRoom.interior.Shop = garageShop;
            AddKeeper(garageRoom, CharacterCatalog.Keepers[1], garageShop, InteriorNpc.Role.GarageMechanic);
            BuildGarageBay(garageRoom.go, mats);
            BuildDoor(doors, mats, Door(c + 1, c - 1, Heading.West), 270f,
                      garageRoom.interior, "Chop Shop", null);

            // --- 3. Clothing store -----------------------------------------------------
            var clothesRoom = BuildRoom(rooms, mats, 2, "Clothing", "Threads",
                                        new Vector3(14f, 4.2f, 12f),
                                        new Color(0.45f, 0.42f, 0.46f));
            var clothesShop = CreateClothingShop(clothesRoom.go);
            clothesRoom.interior.Shop = clothesShop;
            AddKeeper(clothesRoom, CharacterCatalog.Keepers[2], clothesShop, InteriorNpc.Role.Shopkeeper);
            AddMannequins(clothesRoom);
            BuildDoor(doors, mats, Door(c - 1, c + 1, Heading.North), 0f,
                      clothesRoom.interior, "Threads", null);

            // --- 4. Safehouse ----------------------------------------------------------
            var safeRoom = BuildRoom(rooms, mats, 3, "Safehouse", "Safehouse",
                                     new Vector3(16f, 4.2f, 14f),
                                     new Color(0.44f, 0.40f, 0.34f));
            safeRoom.interior.SavesOnEntry = true;
            AddKeeper(safeRoom, CharacterCatalog.Keepers[3], null, InteriorNpc.Role.Concierge);

            Vector3 safeDoor = Door(c - 2, c - 1, Heading.West);
            var property = BuildProperty(root, mats, safeDoor + new Vector3(0f, 0f, 6f));
            BuildDoor(doors, mats, safeDoor, 270f,
                      safeRoom.interior, "Safehouse", property);

            // --- 5-7. The other buyable addresses --------------------------------------
            var estates = ExtraEstates();
            foreach (var estate in estates)
            {
                var room = BuildRoom(rooms, mats, estate.RoomIndex, estate.Id.Replace("property.", ""),
                                     estate.Name, estate.Size, estate.Ambient);

                // Owned addresses are save points, the same as the starter safehouse.
                room.interior.SavesOnEntry = true;
                AddKeeper(room, CharacterCatalog.Keepers[estate.RoomIndex % CharacterCatalog.Keepers.Length],
                          null, InteriorNpc.Role.Concierge);

                Vector3 doorAt = Door(estate.Block.x, estate.Block.y, estate.Edge);
                var prop = BuildProperty(root, mats, doorAt + estate.Edge.ToVector() * 6f,
                                         estate.Id, estate.Name, estate.Price);
                BuildDoor(doors, mats, doorAt, estate.Yaw, room.interior, estate.Name, prop);
            }

            Debug.Log("[Interiors] Built " + (4 + estates.Length) + " interiors and doorways, "
                      + "of which " + (1 + estates.Length) + " are purchasable properties.");
        }

        /// <summary>
        /// A doorway on the given edge of a city block, flush with the building line.
        ///
        /// The door frame is a shallow slab standing on the pavement, so it is pushed a
        /// little further out than a mission contact would be -- far enough that the
        /// building behind it never z-fights with the frame.
        /// </summary>
        static Vector3 Door(int bx, int bz, Heading edge)
        {
            Vector3 p = CityBuilder.BlockEdge(bx, bz, edge);
            return p + edge.ToVector() * 0.9f;
        }

        // --------------------------------------------------------------------- rooms

        struct Room
        {
            public GameObject go;
            public Interior interior;
            public Vector3 size;
        }

        static Room BuildRoom(GameObject parent, Mats mats, int index, string id, string name,
                              Vector3 size, Color ambient)
        {
            Vector3 centre = InteriorOrigin + Vector3.forward * (index * InteriorSpacing);

            var go = new GameObject("Interior_" + id);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = centre;

            float hw = size.x * 0.5f;
            float hd = size.z * 0.5f;
            const float wall = 0.4f;

            // Shell.
            Box(go, "Floor", mats.Floor, new Vector3(0f, -0.2f, 0f), new Vector3(size.x, 0.4f, size.z));
            Box(go, "Ceiling", mats.Wall, new Vector3(0f, size.y, 0f), new Vector3(size.x, 0.4f, size.z));
            Box(go, "Wall_N", mats.Wall, new Vector3(0f, size.y * 0.5f, hd), new Vector3(size.x, size.y, wall));
            Box(go, "Wall_S", mats.Wall, new Vector3(0f, size.y * 0.5f, -hd), new Vector3(size.x, size.y, wall));
            Box(go, "Wall_E", mats.Wall, new Vector3(hw, size.y * 0.5f, 0f), new Vector3(wall, size.y, size.z));
            Box(go, "Wall_W", mats.Wall, new Vector3(-hw, size.y * 0.5f, 0f), new Vector3(wall, size.y, size.z));

            // Emissive ceiling strips stand in for real lights: the interior ambient is set by
            // InteriorManager, so these are decoration rather than illumination, and cost
            // nothing on a mobile tier with additional lights disabled.
            for (int i = -1; i <= 1; i++)
                Box(go, "LightPanel", mats.LightPanel,
                    new Vector3(i * (size.x * 0.28f), size.y - 0.25f, 0f),
                    new Vector3(0.7f, 0.08f, size.z * 0.7f), collide: false);

            // Entry and exit both sit at the south wall, where the door would be.
            // Entry sits well clear of the exit radius. Arriving inside the exit trigger would
            // bounce the player straight back onto the street.
            var entry = new GameObject("EntryPoint").transform;
            entry.SetParent(go.transform, false);
            entry.localPosition = new Vector3(0f, 0.1f, -hd + 4.2f);
            entry.localRotation = Quaternion.identity;   // facing into the room

            var exit = new GameObject("ExitTrigger");
            exit.transform.SetParent(go.transform, false);
            exit.transform.localPosition = new Vector3(0f, 0.1f, -hd + 0.9f);
            var exitComponent = exit.AddComponent<InteriorExit>();
            exitComponent.ExitRadius = 1.6f;

            // A visible mat marking the way out.
            Box(go, "ExitMat", mats.ExitMat, new Vector3(0f, 0.02f, -hd + 0.9f),
                new Vector3(3f, 0.06f, 1.6f), collide: false);

            var interior = go.AddComponent<Interior>();
            interior.InteriorId = "interior." + id.ToLowerInvariant();
            interior.DisplayName = name;
            interior.EntryPoint = entry;
            interior.ExitTrigger = exit.transform;
            interior.AmbientColour = ambient;

            return new Room { go = go, interior = interior, size = size };
        }

        static void AddKeeper(Room room, CharacterCatalog.Look look, Shop shop, InteriorNpc.Role role)
        {
            float hd = room.size.z * 0.5f;

            // Counter along the far wall, with the keeper behind it.
            Box(room.go, "Counter", LoadMaterials().Counter,
                new Vector3(0f, 0.55f, hd - 3.2f), new Vector3(room.size.x * 0.6f, 1.1f, 0.8f));

            var go = new GameObject("Keeper");
            go.transform.SetParent(room.go.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, hd - 2.0f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            CharacterCatalog.AttachBody(go, look, CharacterCatalog.LoadController(),
                                        AnimatorCullingMode.CullCompletely);

            var npc = go.AddComponent<InteriorNpc>();
            npc.Job = role;
            npc.Shop = shop;
            npc.InteractRadius = 3.2f;
        }

        /// <summary>
        /// Shop-window dummies along the clothing store's side walls.
        ///
        /// This is the one honest use for the "Low Poly Characters Lite" pack. Those models
        /// have no skeleton at all -- no deformer, no bones, no avatar -- and are modelled in a
        /// T-pose, so they cannot walk, flinch or ragdoll and cannot be a pedestrian (see
        /// PHASE9.md and DECISIONS.md D7). A clothing shop is the one place in the game where a
        /// motionless figure standing with its arms out is not a bug but the point.
        /// </summary>
        static void AddMannequins(Room room)
        {
            string[] models =
            {
                "Assets/PolygonalAssets/Low polyCharactsre lite/Prefab/SM_M_CiviPerson_F01.prefab",
                "Assets/PolygonalAssets/Low polyCharactsre lite/Prefab/SM_M_Thug_F05.prefab",
                "Assets/PolygonalAssets/Low polyCharactsre lite/Prefab/SM_M_factory worker_M02.prefab",
                "Assets/PolygonalAssets/Low polyCharactsre lite/Prefab/SM_M_Prisoner_F04.prefab",
            };

            var plinthMat = LoadMaterials().Counter;
            float hx = room.size.x * 0.5f;
            float hd = room.size.z * 0.5f;

            for (int i = 0; i < models.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(models[i]);
                if (source == null) { Debug.LogWarning("[Interiors] Missing " + models[i]); continue; }

                // Two down each side wall, facing inward across the shop floor.
                bool left = i % 2 == 0;
                float x = (left ? -1f : 1f) * (hx - 1.4f);
                float z = hd - 5.5f - (i / 2) * 3.4f;
                float yaw = left ? 90f : -90f;

                var stand = new GameObject("Mannequin_" + i);
                stand.transform.SetParent(room.go.transform, false);
                stand.transform.localPosition = new Vector3(x, 0f, z);
                stand.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                Box(stand, "Plinth", plinthMat, new Vector3(0f, 0.09f, 0f),
                    new Vector3(0.9f, 0.18f, 0.9f), collide: true);

                var figure = (GameObject)PrefabUtility.InstantiatePrefab(source);
                figure.name = "Figure";
                figure.transform.SetParent(stand.transform, false);
                figure.transform.localPosition = new Vector3(0f, 0.18f, 0f);

                // No collider: the plinth is what the player bumps into, and a per-mannequin
                // mesh collider would be four more static meshes for no gameplay difference.
                foreach (var col in figure.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(col);

                EnvironmentCatalog.MarkStatic(stand);
            }
        }

        static void BuildGarageBay(GameObject room, Mats mats)
        {
            // Raised plinth the display vehicle sits on.
            Box(room, "Plinth", mats.Counter, new Vector3(0f, 0.1f, -1.5f),
                new Vector3(6.5f, 0.2f, 12f), collide: false);

            var bay = new GameObject("DisplayBay").transform;
            bay.SetParent(room.transform, false);
            bay.localPosition = new Vector3(0f, 0.25f, -1.5f);
            bay.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var garage = Object.FindAnyObjectByType<Garage>();
            if (garage != null)
            {
                garage.DisplayBay = bay;
                garage.VehiclePrefabs = LoadVehiclePrefabs();
            }
        }

        static GameObject[] LoadVehiclePrefabs()
        {
            var list = new List<GameObject>();
            foreach (var name in new[] { "Car_0", "Car_1", "Car_2", "Car_3", "Car_4", "Car_5", "Bike", "Boat" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + name + ".prefab");
                if (prefab != null) list.Add(prefab);
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------- doorways

        static void BuildDoor(GameObject parent, Mats mats, Vector3 position, float yaw,
                              Interior target, string signText, Property owningProperty)
        {
            var go = new GameObject("Door_" + target.InteriorId);
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // Frame and a lit sign above it, so the shop reads from across the street.
            Box(go, "Frame", mats.DoorFrame, new Vector3(0f, 1.35f, 0f), new Vector3(2.6f, 2.7f, 0.35f),
                collide: false);
            Box(go, "Threshold", mats.ExitMat, new Vector3(0f, 0.03f, 0.6f), new Vector3(2.2f, 0.06f, 1.2f),
                collide: false);

            var sign = Box(go, "Sign", mats.Sign, new Vector3(0f, 3.1f, 0f), new Vector3(3.2f, 0.7f, 0.25f),
                           collide: false);

            var door = go.AddComponent<Doorway>();
            door.Target = target;
            door.SignRenderer = sign.GetComponent<Renderer>();
            door.OwningProperty = owningProperty;
            door.InteractRadius = 3.4f;

            // Name the sign so it is identifiable in the hierarchy without a text mesh.
            sign.name = "Sign_" + signText;
        }

        static Property BuildProperty(GameObject parent, Mats mats, Vector3 position,
                                      string id = "property.safehouse",
                                      string displayName = "Bayside Safehouse",
                                      int price = 4000)
        {
            var go = new GameObject("Property_" + id.Replace("property.", ""));
            go.transform.SetParent(parent.transform, false);
            go.transform.position = position;

            var sign = Box(go, "ForSaleSign", mats.Sign, new Vector3(0f, 2.2f, 1.6f),
                           new Vector3(2.4f, 1.2f, 0.2f), collide: false);

            var arrival = new GameObject("ArrivalPoint").transform;
            arrival.SetParent(go.transform, false);
            arrival.localPosition = new Vector3(-3f, 0.2f, 0f);

            var property = go.AddComponent<Property>();
            property.PropertyId = id;
            property.DisplayName = displayName;
            property.Price = price;
            property.ArrivalPoint = arrival;
            property.SignRenderer = sign.GetComponent<Renderer>();

            return property;
        }

        /// <summary>One ownable address: a room in dead air, a door on the street, a for-sale sign.</summary>
        struct Estate
        {
            public string Id, Name;
            public int Price, RoomIndex;
            public Vector2Int Block;
            public Heading Edge;
            public float Yaw;
            public Vector3 Size;
            public Color Ambient;
        }

        /// <summary>
        /// The properties beyond the starter safehouse.
        ///
        /// Three of them, because two missions in the Phase 4 list gate on "own 2 interiors"
        /// and one ownable address made those -- and everything chained behind them -- literally
        /// unreachable. Prices escalate from the safehouse's 4,000 so each is a real decision
        /// rather than a formality, and every one is a save point, exactly like the safehouse.
        ///
        /// Blocks avoid the two park blocks and the four blocks the shop doors already use.
        /// </summary>
        static Estate[] ExtraEstates()
        {
            int c = CityBuilder.CentreBlock;
            return new[]
            {
                new Estate
                {
                    Id = "property.dockside", Name = "Dockside Loft", Price = 7500,
                    RoomIndex = 4, Block = new Vector2Int(c + 2, c + 1), Edge = Heading.East,
                    Yaw = 90f, Size = new Vector3(15f, 4.2f, 13f),
                    Ambient = new Color(0.40f, 0.42f, 0.46f),
                },
                new Estate
                {
                    Id = "property.hillside", Name = "Hillside Villa", Price = 12000,
                    RoomIndex = 5, Block = new Vector2Int(c, c + 2), Edge = Heading.North,
                    Yaw = 0f, Size = new Vector3(18f, 4.6f, 15f),
                    Ambient = new Color(0.46f, 0.42f, 0.36f),
                },
                new Estate
                {
                    Id = "property.penthouse", Name = "Downtown Penthouse", Price = 18000,
                    RoomIndex = 6, Block = new Vector2Int(c - 2, c + 2), Edge = Heading.South,
                    Yaw = 180f, Size = new Vector3(17f, 4.8f, 14f),
                    Ambient = new Color(0.48f, 0.44f, 0.42f),
                },
            };
        }

        // ---------------------------------------------------------------------- stock

        static Shop CreateGunShop(GameObject host)
        {
            var shop = host.AddComponent<Shop>();
            shop.ShopId = "shop.guns";
            shop.DisplayName = "Ammu-Mart";
            shop.Greeting = "Protection, ammunition, first aid.";

            shop.Stock = new List<ShopItem>
            {
                Item("shop.health.small", "Med Kit", "Restores 50 health.",
                     ShopCategory.Consumable, ShopEffect.RestoreHealth, 120, 50f),
                Item("shop.armor", "Body Armour", "Adds 100 armour. Soaks most of a hit.",
                     ShopCategory.Consumable, ShopEffect.GiveArmor, 350, 100f),
                Item("shop.ammo.box", "Ammo Box", "24 pistol rounds.",
                     ShopCategory.Consumable, ShopEffect.GiveAmmo, 90, 24f),
                Item("shop.pistol", "Pistol", "Replaces a confiscated sidearm. 36 rounds.",
                     ShopCategory.Equipment, ShopEffect.GivePistol, 600, 36f),
                Item("shop.ammo.capacity", "Extended Pouches", "Raises max ammo by 80.",
                     ShopCategory.Equipment, ShopEffect.ExtendAmmoCapacity, 900, 80f, level: 3),
            };

            return shop;
        }

        static Shop CreateGarageShop(GameObject host)
        {
            var shop = host.AddComponent<Shop>();
            shop.ShopId = "shop.garage";
            shop.DisplayName = "Chop Shop";
            shop.Greeting = "Engine, plate, paint. Your call.";

            shop.Stock = new List<ShopItem>
            {
                Item("garage.engine", "Engine Tune", "+14% top speed and torque per level.",
                     ShopCategory.VehicleUpgrade, ShopEffect.VehicleSpeed, 700, 1f),
                Item("garage.armor", "Reinforced Panels", "+35% vehicle durability per level.",
                     ShopCategory.VehicleUpgrade, ShopEffect.VehicleArmor, 550, 1f),
                Paint("garage.paint.red", "Respray - Crimson", new Color(0.75f, 0.12f, 0.12f), 300),
                Paint("garage.paint.blue", "Respray - Cobalt", new Color(0.13f, 0.28f, 0.70f), 300),
                Paint("garage.paint.gold", "Respray - Gold", new Color(0.85f, 0.68f, 0.18f), 450),
                Paint("garage.paint.black", "Respray - Midnight", new Color(0.07f, 0.07f, 0.09f), 300),
            };

            return shop;
        }

        static Shop CreateClothingShop(GameObject host)
        {
            var shop = host.AddComponent<Shop>();
            shop.ShopId = "shop.clothes";
            shop.DisplayName = "Threads";
            shop.Greeting = "Change how the city sees you.";

            shop.Stock = new List<ShopItem>
            {
                Skin("skin.street", "Street Grey", new Color(0.45f, 0.46f, 0.48f), 200),
                Skin("skin.crimson", "Crimson Jacket", new Color(0.62f, 0.14f, 0.16f), 400),
                Skin("skin.navy", "Navy Suit", new Color(0.16f, 0.22f, 0.40f), 400),
                Skin("skin.emerald", "Emerald Coat", new Color(0.13f, 0.45f, 0.28f), 650, level: 3),
                Skin("skin.white", "Ivory Set", new Color(0.90f, 0.89f, 0.85f), 900, level: 4),
            };

            return shop;
        }

        static ShopItem Item(string id, string name, string description, ShopCategory category,
                             ShopEffect effect, int price, float amount, int level = 1) =>
            new ShopItem
            {
                Id = id, DisplayName = name, Description = description,
                Category = category, Effect = effect,
                Price = price, Amount = amount, RequiredLevel = level,
            };

        static ShopItem Paint(string id, string name, Color colour, int price) =>
            new ShopItem
            {
                Id = id, DisplayName = name, Description = "Full respray.",
                Category = ShopCategory.VehicleUpgrade, Effect = ShopEffect.VehiclePaint,
                Price = price, Colour = colour, RequiredLevel = 1,
            };

        static ShopItem Skin(string id, string name, Color colour, int price, int level = 1) =>
            new ShopItem
            {
                Id = id, DisplayName = name, Description = "Cosmetic outfit.",
                Category = ShopCategory.Skin, Effect = ShopEffect.PlayerSkin,
                Price = price, Colour = colour, RequiredLevel = level,
            };

        // ------------------------------------------------------------------ materials

        class Mats
        {
            public Material Floor, Wall, Counter, LightPanel, Sign, DoorFrame, ExitMat;
        }

        static Mats _cached;

        static Mats LoadMaterials()
        {
            if (_cached != null) return _cached;

            _cached = new Mats
            {
                Floor = Mat("Int_Floor", new Color(0.24f, 0.23f, 0.22f), 0.18f, false),
                Wall = Mat("Int_Wall", new Color(0.40f, 0.39f, 0.37f), 0.10f, false),
                Counter = Mat("Int_Counter", new Color(0.30f, 0.22f, 0.16f), 0.28f, false),
                LightPanel = Mat("Int_LightPanel", new Color(1f, 0.96f, 0.88f), 0.9f, true),
                Sign = Mat("Int_Sign", new Color(0.35f, 0.9f, 1f), 0.6f, true),
                DoorFrame = Mat("Int_DoorFrame", new Color(0.18f, 0.19f, 0.21f), 0.35f, false),
                ExitMat = Mat("Int_ExitMat", new Color(0.28f, 0.55f, 0.35f), 0.1f, false),
            };

            return _cached;
        }

        static Material Mat(string name, Color colour, float smoothness, bool emissive)
        {
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smoothness);
            mat.enableInstancing = true;

            if (emissive)
            {
                // The keyword must be enabled on the shared material or per-object emission set
                // through a property block is ignored entirely.
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", colour * 2.2f);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static GameObject Box(GameObject parent, string name, Material mat,
                              Vector3 localPos, Vector3 size, bool collide = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            if (!collide) Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            return go;
        }

        static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }
    }
}

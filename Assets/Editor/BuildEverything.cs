using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Runs the whole generation pipeline in dependency order.
    ///
    /// The world is fully procedural, so this is the normal way to pick up a change to any
    /// builder: edit the constants, run this, and the scene is rebuilt from scratch. Nothing
    /// in the scene is hand-placed, which is what keeps it reproducible.
    /// </summary>
    public static class BuildEverything
    {
        [MenuItem("Tools/Mini GTA/BUILD EVERYTHING", priority = 0)]
        public static void All()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Pipeline] Exit play mode before rebuilding the world.");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Mini GTA", "Configuring Mixamo assets...", 0.03f);
                MixamoImportSetup.ConfigureAll();

                EditorUtility.DisplayProgressBar("Mini GTA", "Extracting character textures...", 0.18f);
                MixamoImportSetup.ExtractCharacterTextures();

                // Before anything places a pack prefab: two of the seven packs ship built-in
                // pipeline materials, which render magenta under URP until this runs.
                EditorUtility.DisplayProgressBar("Mini GTA", "Converting asset packs to URP...", 0.28f);
                AssetPackSetup.ConvertAll();

                // The UI kit ships every sprite with a zero nine-slice border, so themed
                // panels and buttons smear at any size but their authored one. Must run
                // before anything builds a canvas.
                EditorUtility.DisplayProgressBar("Mini GTA", "Preparing UI kit sprites...", 0.30f);
                UiTheme.PrepareSprites();

                EditorUtility.DisplayProgressBar("Mini GTA", "Building weapon library...", 0.33f);
                WeaponSetup.Build();

                EditorUtility.DisplayProgressBar("Mini GTA", "Generating terrain...", 0.45f);
                TerrainBuilder.BuildTerrain();

                EditorUtility.DisplayProgressBar("Mini GTA", "Building city...", 0.6f);
                CityBuilder.BuildCity();

                EditorUtility.DisplayProgressBar("Mini GTA", "Building road network...", 0.68f);
                RoadNetworkBuilder.Build();

                EditorUtility.DisplayProgressBar("Mini GTA", "Building animator...", 0.74f);
                AnimatorBuilder.Build();

                EditorUtility.DisplayProgressBar("Mini GTA", "Assembling scene...", 0.8f);
                SceneAssembler.Assemble();

                // Vehicles before traffic: the spawner and the parked cars both reference the
                // prefabs, and the pedestrians need the Player the assembler just created.
                EditorUtility.DisplayProgressBar("Mini GTA", "Building vehicles...", 0.88f);
                VehicleBuilder.BuildAll();

                EditorUtility.DisplayProgressBar("Mini GTA", "Building character prefabs...", 0.90f);
                CharacterPrefabBuilder.BuildAll();

                // Both builders above delete and re-save Car_Police.prefab and
                // PoliceOfficer.prefab, which invalidates the references the assembler took at
                // step 8. Re-point the dispatcher now that the final assets exist, or the
                // police force silently does not spawn.
                SceneAssembler.WirePolicePrefabs();

                EditorUtility.DisplayProgressBar("Mini GTA", "Populating traffic...", 0.91f);
                TrafficBuilder.Populate();

                // After the city (it needs the block grid) and after the assembler (the
                // player it arms has to exist). The player now starts unarmed, so if this
                // step is skipped there is no way to get a weapon outside the gun shop.
                EditorUtility.DisplayProgressBar("Mini GTA", "Placing weapon pickups...", 0.92f);
                WeaponSetup.BuildPickups();

                // Wildlife needs the terrain and the park, so it comes after both.
                EditorUtility.DisplayProgressBar("Mini GTA", "Placing wildlife...", 0.93f);
                WildlifeBuilder.Build();

                // Interiors and missions both reference objects the assembler creates, so they
                // must be rebuilt after it, not before.
                EditorUtility.DisplayProgressBar("Mini GTA", "Building interiors...", 0.94f);
                InteriorBuilder.Build();

                EditorUtility.DisplayProgressBar("Mini GTA", "Building missions...", 0.96f);
                MissionBuilder.Build();

                // Culling last: it sorts whatever geometry the steps above ended up creating,
                // so it has to see the finished scene, not a partly-built one.
                EditorUtility.DisplayProgressBar("Mini GTA", "Sorting geometry for culling...", 0.97f);
                PerformanceSetup.SortGeometry();

                EditorUtility.DisplayProgressBar("Mini GTA", "Applying mobile settings...", 0.98f);
                MobileSetup.ConfigureAndroid();
                MobileSetup.ApplyMobileRenderBudgets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log("[Pipeline] Full rebuild complete. Open Assets/Scenes/City.unity and press Play.");
        }

        /// <summary>Faster loop for world tweaks: skips the slow asset reimport steps.</summary>
        [MenuItem("Tools/Mini GTA/Rebuild World Only", priority = 1)]
        public static void WorldOnly()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Pipeline] Exit play mode before rebuilding the world.");
                return;
            }

            TerrainBuilder.BuildTerrain();
            CityBuilder.BuildCity();
            RoadNetworkBuilder.Build();
            SceneAssembler.Assemble();
            TrafficBuilder.Populate();
            WeaponSetup.BuildPickups();
            WildlifeBuilder.Build();
            InteriorBuilder.Build();
            MissionBuilder.Build();
            PerformanceSetup.SortGeometry();
            Debug.Log("[Pipeline] World rebuilt (terrain, city, roads, scene, traffic, "
                      + "pickups, wildlife, interiors, missions, culling).");
        }
    }
}

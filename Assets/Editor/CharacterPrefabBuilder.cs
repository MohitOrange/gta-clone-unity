using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Builds the humanoid NPC prefabs: police officers and civilians.
    ///
    /// Both are assembled from the same parts -- CharacterController, Health, ragdoll and the
    /// shared locomotion controller -- so a civilian and an officer take damage, flinch and die
    /// through identical code. Only the brain component and the model differ.
    /// </summary>
    public static class CharacterPrefabBuilder
    {
        const string PrefabDir = "Assets/Game/Prefabs";

        [MenuItem("Tools/Mini GTA/13. Build Character Prefabs", priority = 133)]
        public static void BuildAll()
        {
            Directory.CreateDirectory(PrefabDir);

            string officer = BuildOfficer();
            AssetDatabase.SaveAssets();

            Debug.Log("[Characters] Built police officer prefab: " + officer);
        }

        static string BuildOfficer()
        {
            var root = new GameObject("PoliceOfficer");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.88f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 50f;

            var health = root.AddComponent<Health>();
            health.MaxHealth = 110f;
            health.MaxArmor = 40f;
            health.StartingArmor = 40f;

            root.AddComponent<DamageReaction>();

            var ragdoll = root.AddComponent<RagdollLite>();
            ragdoll.DespawnAfter = 30f;

            // Officers must keep animating off-screen: they are actively pursuing, and a culled
            // animator would leave them sliding in bind pose when they come back into view.
            var model = CharacterCatalog.AttachBody(root, CharacterCatalog.Officer,
                CharacterCatalog.LoadController(), AnimatorCullingMode.CullUpdateTransforms);

            if (model == null) AddPlaceholderBody(root);

            root.AddComponent<PoliceOfficer>();

            // Police carry the same pistol the player does, through the same socket component.
            // FollowCombatState is off: an officer has no PlayerCombat to follow, so the weapon
            // is simply drawn for as long as the officer exists.
            WeaponSetup.AttachSocket(root, "pistol", followCombat: false);

            return SavePrefab(root, "PoliceOfficer");
        }

        /// <summary>Visible stand-in when the character FBX is missing, rather than an invisible officer.</summary>
        static void AddPlaceholderBody(GameObject root)
        {
            Debug.LogWarning("[Characters] Officer model missing; using a capsule.");
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            placeholder.name = "Body";
            placeholder.transform.SetParent(root.transform, false);
            placeholder.transform.localPosition = new Vector3(0f, 0.88f, 0f);
            placeholder.transform.localScale = new Vector3(0.6f, 0.88f, 0.6f);
            Object.DestroyImmediate(placeholder.GetComponent<Collider>());
        }

        static string SavePrefab(GameObject root, string name)
        {
            string path = PrefabDir + "/" + name + ".prefab";
            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return path;
        }
    }
}

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Unity.AI.Navigation;
using ShadowReckoning.Core;
using ShadowReckoning.Player;
using ShadowReckoning.AI;
using ShadowReckoning.Missions;
using ShadowReckoning.UI;

namespace ShadowReckoning.EditorTools
{
    public static class ShadowReckoningMasterBuilder
    {
        private static readonly string[] RequiredDirectories =
        {
            "Assets/Art/Characters",
            "Assets/Art/Environments",
            "Assets/Art/Animations",
            "Assets/Art/Audio",
            "Assets/Art/Materials",
            "Assets/Art/Prefabs",
            "Assets/Scenes",
            "Assets/Scripts/Core",
            "Assets/Scripts/Player",
            "Assets/Scripts/Camera",
            "Assets/Scripts/AI",
            "Assets/Scripts/Missions",
            "Assets/Scripts/Interaction",
            "Assets/Scripts/UI",
            "Assets/Scripts/SaveSystem",
            "Assets/Scripts/Audio",
            "Assets/Scripts/Settings",
            "Assets/Scripts/Editor",
            "Assets/Settings",
            "Assets/UI"
        };

        [MenuItem("Shadow Reckoning/1. Build Complete Game & Campaign Scene (Milestones 1-7)")]
        public static void BuildCompleteGame()
        {
            foreach (string dir in RequiredDirectories)
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }

            ConfigureAndroidBuildSettings();
            GenerateCompleteCampaignScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SHADOW RECKONING] Complete Campaign Scene (All 5 Zones, 4 Missions, Player, AI, UI) generated successfully!");
        }

        [MenuItem("Shadow Reckoning/2. Build Android APK (Release)")]
        public static void BuildAndroidAPK()
        {
            BuildCompleteGame();
            Directory.CreateDirectory("Builds/Android");
            BuildPlayerOptions opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/CampaignWorld.unity" },
                locationPathName = "Builds/Android/ShadowReckoning.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            EditorUserBuildSettings.buildAppBundle = false;
            BuildPipeline.BuildPlayer(opts);
        }

        [MenuItem("Shadow Reckoning/3. Build Android App Bundle (AAB for Google Play)")]
        public static void BuildAndroidAAB()
        {
            BuildCompleteGame();
            Directory.CreateDirectory("Builds/Android");
            BuildPlayerOptions opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/CampaignWorld.unity" },
                locationPathName = "Builds/Android/ShadowReckoning.aab",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            EditorUserBuildSettings.buildAppBundle = true;
            BuildPipeline.BuildPlayer(opts);
        }

        private static void ConfigureAndroidBuildSettings()
        {
            PlayerSettings.companyName = "ShadowReckoningStudio";
            PlayerSettings.productName = "Shadow Reckoning";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);
        }

        private static void GenerateCompleteCampaignScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Materials
            Material matGround = GetOrCreateMat("M_Ground", new Color(0.20f, 0.22f, 0.18f), 0.0f, 0.2f);
            Material matConcrete = GetOrCreateMat("M_Concrete", new Color(0.36f, 0.37f, 0.38f), 0.05f, 0.3f);
            Material matSteel = GetOrCreateMat("M_Steel", new Color(0.22f, 0.25f, 0.27f), 0.75f, 0.55f);
            Material matFoliage = GetOrCreateMat("M_Foliage", new Color(0.14f, 0.29f, 0.15f), 0.0f, 0.25f);
            Material matWater = GetOrCreateMat("M_Water", new Color(0.08f, 0.25f, 0.32f), 0.1f, 0.85f);
            Material matTerminal = GetOrCreateMat("M_TerminalObjective", new Color(0.95f, 0.42f, 0.08f), 0.3f, 0.8f, true);
            Material matIntel = GetOrCreateMat("M_Intel", new Color(0.15f, 0.75f, 0.95f), 0.3f, 0.8f, true);
            Material matHero = GetOrCreateMat("M_Protagonist", new Color(0.18f, 0.24f, 0.20f), 0.2f, 0.45f);
            Material matEnemy = GetOrCreateMat("M_HostilePatrol", new Color(0.42f, 0.16f, 0.14f), 0.2f, 0.4f);
            Material matSurvivor = GetOrCreateMat("M_Survivor", new Color(0.20f, 0.48f, 0.72f), 0.1f, 0.4f);

            // 1. Core Systems Root
            GameObject coreGO = new GameObject("--- CORE & CAMPAIGN SYSTEMS ---");
            coreGO.AddComponent<GameBootstrapper>();
            coreGO.AddComponent<SaveSystemManager>();
            coreGO.AddComponent<GraphicsQualityManager>();
            coreGO.AddComponent<AudioManager>();
            coreGO.AddComponent<MobileTouchInputManager>();
            coreGO.AddComponent<CampaignMissionManager>();
            coreGO.AddComponent<GameUIManager>();

            // 2. Atmospheric Lighting
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.14f, 0.17f, 0.21f);
            RenderSettings.fogDensity = 0.011f;

            GameObject sunGO = new GameObject("Directional Light (Dusk Sun)");
            Light sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.76f, 0.56f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sunGO.transform.rotation = Quaternion.Euler(26f, -130f, 0f);

            // 3. Interconnected 5-Zone World Architecture + NavMeshSurface
            GameObject worldRoot = new GameObject("--- INTERCONNECTED CAMPAIGN WORLD (5 ZONES) ---");
            NavMeshSurface navSurface = worldRoot.AddComponent<NavMeshSurface>();

            // Main Valley Terrain Floor & River Channel (For Swimming)
            CreateBlock("ValleyFloor_Main", worldRoot.transform, new Vector3(0f, -0.5f, 0f), new Vector3(180f, 1f, 180f), matGround, true);
            CreateBlock("RiverChannel_Water", worldRoot.transform, new Vector3(0f, -0.45f, 0f), new Vector3(18f, 0.2f, 160f), matWater, false);

            // ZONE 1: Abandoned Military Outpost (SW Quadrant)
            CreateBlock("Outpost_CommandBunker", worldRoot.transform, new Vector3(-25f, 2f, -25f), new Vector3(16f, 4f, 12f), matConcrete, true);
            CreateBlock("Outpost_ClimbStep", worldRoot.transform, new Vector3(-16f, 1f, -25f), new Vector3(2.5f, 2f, 4f), matSteel, true);
            CreateBlock("Outpost_VaultCover_1", worldRoot.transform, new Vector3(-12f, 0.5f, -32f), new Vector3(4f, 1.0f, 0.8f), matConcrete, true);
            CreateConcealmentBush("StealthBush_Outpost", worldRoot.transform, new Vector3(-18f, 0.7f, -15f), matFoliage, false);
            CreateConcealmentBush("SafeZone_OutpostTent", worldRoot.transform, new Vector3(-2f, 1.2f, -38f), matSteel, true);

            // ZONE 2: Destroyed Urban Settlement (Center)
            CreateBlock("Urban_RuinedTowerA", worldRoot.transform, new Vector3(4f, 3.5f, 5f), new Vector3(10f, 7f, 10f), matConcrete, true);
            CreateBlock("Urban_RuinedWallB", worldRoot.transform, new Vector3(16f, 1.8f, -5f), new Vector3(8f, 3.6f, 2f), matConcrete, true);
            CreateBlock("Urban_VaultRubble", worldRoot.transform, new Vector3(10f, 0.5f, 12f), new Vector3(3.5f, 1.0f, 1f), matConcrete, true);

            // ZONE 3: Industrial Complex (SE Quadrant)
            CreateBlock("Industrial_MainHangar", worldRoot.transform, new Vector3(35f, 3f, -15f), new Vector3(18f, 6f, 16f), matSteel, true);
            CreateBlock("Industrial_Smokestack", worldRoot.transform, new Vector3(42f, 8f, -20f), new Vector3(3f, 16f, 3f), matConcrete, true);

            // ZONE 4: Dense Forest (NE Quadrant)
            for (int i = 0; i < 8; i++)
            {
                Vector3 treePos = new Vector3(22f + (i % 4) * 9f, 3.5f, 22f + (i / 4) * 12f);
                CreateBlock($"Forest_TreeTrunk_{i}", worldRoot.transform, treePos, new Vector3(1.2f, 7f, 1.2f), matGround, true);
                CreateConcealmentBush($"Forest_CanopyConcealment_{i}", worldRoot.transform, new Vector3(treePos.x + 2f, 0.8f, treePos.z), matFoliage, false);
            }

            // ZONE 5: Underground Research Blacksite (Far NE Bunker) & Extraction Helipad (NW)
            CreateBlock("Blacksite_SubterraneanVault", worldRoot.transform, new Vector3(52f, 2.5f, 42f), new Vector3(16f, 5f, 16f), matSteel, true);
            CreateBlock("Extraction_Helipad_NW", worldRoot.transform, new Vector3(-40f, 0.3f, 40f), new Vector3(18f, 0.6f, 18f), matConcrete, true);

            // Bake NavMesh
            navSurface.BuildNavMesh();

            // 4. Spawn Mission Objectives (Missions 01 - 04) + Intel Logs
            GameObject missionsRoot = new GameObject("--- MISSION OBJECTIVES & INTEL ---");
            CreateInteractable("M1_Step0_AuxGenerator", missionsRoot.transform, new Vector3(-14f, 1f, -20f), matTerminal,
                "Reboot Outpost Aux Power Generator", InteractableRole.MissionObjectiveTerminal, 0, 0,
                "SPEC. VANCE", "Auxiliary generator online. Climbing to the Command Bunker roof to link the satellite dish.");

            CreateInteractable("M1_Step1_SatLink", missionsRoot.transform, new Vector3(-25f, 4.8f, -25f), matTerminal,
                "Establish Satellite Communications Uplink", InteractableRole.MissionObjectiveTerminal, 0, 1,
                "OVERWATCH HQ", "Uplink locked! Picking up an SOS beacon from Dr. Elena Vance inside the Industrial Complex.");

            CreateInteractable("M2_Step0_UrbanGate", missionsRoot.transform, new Vector3(18f, 1f, -2f), matTerminal,
                "Override Industrial Complex Security Gate", InteractableRole.MissionObjectiveTerminal, 1, 0,
                "SPEC. VANCE", "Security gate overridden. Entering the Industrial Hangar to locate Dr. Vance.");

            // M2_Step1 is rescuing Dr. Elena Vance (Spawned below)
            CreateSurvivor("Survivor_DrElenaVance", missionsRoot.transform, new Vector3(32f, 0f, -6f), matSurvivor);

            CreateInteractable("M3_Step0_BlacksiteDoor", missionsRoot.transform, new Vector3(44f, 1f, 34f), matTerminal,
                "Breach Underground Blacksite Airlock", InteractableRole.MissionObjectiveTerminal, 2, 0,
                "DR. VANCE", "That airlock leads straight to the Project ECLIPSE server core. Watch out for patrols!");

            CreateInteractable("M3_Step1_Mainframe", missionsRoot.transform, new Vector3(52f, 5.5f, 42f), matTerminal,
                "Download Project ECLIPSE Experiment Telemetry", InteractableRole.MissionObjectiveTerminal, 2, 1,
                "OVERWATCH HQ", "Telemetry secured! Get all survivors to the North-West Helipad for immediate extraction.");

            CreateInteractable("M4_Step0_HelipadBeacon", missionsRoot.transform, new Vector3(-36f, 1.2f, 36f), matTerminal,
                "Deploy Infrared Extraction Strobe", InteractableRole.MissionObjectiveTerminal, 3, 0,
                "RAZOR-1 PILOT", "Visual on your IR strobe! Touching down at the Helipad now—get aboard!");

            CreateInteractable("M4_Step1_FinalExtraction", missionsRoot.transform, new Vector3(-42f, 1.2f, 42f), matTerminal,
                "Board Razor-1 Extraction Transport", InteractableRole.MissionObjectiveTerminal, 3, 1,
                "OVERWATCH HQ", "All personnel aboard. Mission accomplished, Specialist.");

            // 4 Collectible Intel Terminals
            for (int i = 0; i < 4; i++)
            {
                CreateInteractable($"Intel_Log_{i + 1}", missionsRoot.transform, new Vector3(-8f + i * 14f, 1f, -10f + i * 8f), matIntel,
                    $"Collect Classified Intel Log #{i + 1}", InteractableRole.IntelDocument, i, 0,
                    "INTEL ARCHIVE", $"Classified Blacksite Log #{i + 1} decrypted and stored in tactical database.");
            }

            // 5. Spawn Hostile Patrols with Waypoints
            GameObject aiRoot = new GameObject("--- HOSTILE PATROLS ---");
            CreatePatrolUnit("Patrol_Outpost", aiRoot.transform, new Vector3(-10f, 0f, -16f), new Vector3(-28f, 0f, -16f), matEnemy);
            CreatePatrolUnit("Patrol_Urban", aiRoot.transform, new Vector3(12f, 0f, 14f), new Vector3(26f, 0f, -8f), matEnemy);
            CreatePatrolUnit("Patrol_ForestBlacksite", aiRoot.transform, new Vector3(36f, 0f, 28f), new Vector3(48f, 0f, 28f), matEnemy);

            // 6. Spawn Protagonist & Third-Person Camera
            GameObject playerGO = new GameObject("Protagonist_Specialist");
            playerGO.transform.position = new Vector3(0f, 1.5f, -38f);
            CharacterController cc = playerGO.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.38f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "VisualModel";
            visual.transform.SetParent(playerGO.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial = matHero;

            playerGO.AddComponent<PlayerController>();

            GameObject camGO = new GameObject("MainCamera_ThirdPerson");
            camGO.tag = "MainCamera";
            Camera cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 240f;
            camGO.AddComponent<AudioListener>();
            var tpCam = camGO.AddComponent<ThirdPersonCameraController>();
            tpCam.SetTarget(playerGO.transform);

            // Save Scene & Register in Build Settings
            string scenePath = "Assets/Scenes/CampaignWorld.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        }

        private static Material GetOrCreateMat(string name, Color color, float metallic, float smoothness, bool emissive = false)
        {
            string path = $"Assets/Art/Materials/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(s) { name = name };
                mat.SetColor("_BaseColor", color);
                mat.color = color;
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smoothness);
                mat.enableInstancing = true;
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 2.2f);
                }
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        private static GameObject CreateBlock(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool isStatic)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (isStatic)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
            return go;
        }

        private static void CreateConcealmentBush(string name, Transform parent, Vector3 pos, Material mat, bool isSafeZone)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(3.2f, 1.8f, 3.2f);
            go.GetComponent<Collider>().isTrigger = true;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var trigger = go.AddComponent<StealthZoneTrigger>();
            trigger.isSafeZone = isSafeZone;
            trigger.isFoliageConcealment = !isSafeZone;
        }

        private static void CreateInteractable(string id, Transform parent, Vector3 pos, Material mat, string prompt, InteractableRole role, int missionIdx, int stepIdx, string speaker, string msg)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = id;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(1.1f, 1.6f, 0.8f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var wi = go.AddComponent<WorldInteractable>();
            wi.interactableId = id;
            wi.promptLabel = prompt;
            wi.role = role;
            wi.targetMissionIndex = missionIdx;
            wi.targetObjectiveStep = stepIdx;
            wi.radioSpeaker = speaker;
            wi.radioMessage = msg;
        }

        private static void CreateSurvivor(string name, Transform parent, Vector3 pos, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + Vector3.up * 1f;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<NavMeshAgent>();
            go.AddComponent<SurvivorCompanionAI>();
        }

        private static void CreatePatrolUnit(string name, Transform parent, Vector3 wpA, Vector3 wpB, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = wpA + Vector3.up * 1f;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "AlertIndicator";
            indicator.transform.SetParent(go.transform, false);
            indicator.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            indicator.transform.localScale = Vector3.one * 0.35f;
            Object.DestroyImmediate(indicator.GetComponent<Collider>());

            GameObject wp1 = new GameObject($"{name}_WP1");
            wp1.transform.SetParent(parent, false);
            wp1.transform.localPosition = wpA;

            GameObject wp2 = new GameObject($"{name}_WP2");
            wp2.transform.SetParent(parent, false);
            wp2.transform.localPosition = wpB;

            go.AddComponent<NavMeshAgent>();
            var ai = go.AddComponent<PatrolAIController>();
            ai.waypoints = new[] { wp1.transform, wp2.transform };
        }
    }
}
#endif

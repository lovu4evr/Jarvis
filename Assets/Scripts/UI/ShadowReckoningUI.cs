using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using ShadowReckoning.Core;
using ShadowReckoning.Player;
using ShadowReckoning.Missions;

namespace ShadowReckoning.UI
{
    public class VirtualJoystickUI : MonoBehaviour, IDragHandler, IPointerUpHandler, IPointerDownHandler
    {
        public RectTransform backgroundRect;
        public RectTransform handleRect;
        public float maxRadius = 75f;

        public void OnPointerDown(PointerEventData eventData) => OnDrag(eventData);

        public void OnDrag(PointerEventData eventData)
        {
            if (backgroundRect == null || handleRect == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backgroundRect, eventData.position, eventData.pressEventCamera, out Vector2 localPos))
            {
                Vector2 clamped = Vector2.ClampMagnitude(localPos, maxRadius);
                handleRect.anchoredPosition = clamped;
                if (MobileTouchInputManager.Instance != null)
                {
                    MobileTouchInputManager.Instance.SetVirtualJoystick(clamped / maxRadius);
                }
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (handleRect != null) handleRect.anchoredPosition = Vector2.zero;
            if (MobileTouchInputManager.Instance != null)
            {
                MobileTouchInputManager.Instance.SetVirtualJoystick(Vector2.zero);
            }
        }
    }

    public class SprintHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            if (MobileTouchInputManager.Instance != null) MobileTouchInputManager.Instance.SetSprintButton(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (MobileTouchInputManager.Instance != null) MobileTouchInputManager.Instance.SetSprintButton(false);
        }
    }

    public class GameUIManager : MonoBehaviour
    {
        public static GameUIManager Instance { get; private set; }

        private Canvas rootCanvas;
        private GameObject mainMenuPanel;
        private GameObject hudPanel;
        private GameObject settingsPanel;
        private GameObject missionsPanel;
        private GameObject creditsPanel;
        private GameObject mapOverlayPanel;
        private GameObject modalBannerPanel;

        private Text objectiveText;
        private Text stealthStatusText;
        private Text interactButtonLabel;
        private Button interactButton;
        private Text subtitleText;
        private Text modalTitleText;
        private Text modalBodyText;
        private Text settingsSummaryText;

        private RectTransform joystickContainer;
        private RectTransform actionClusterContainer;

        private float subtitleTimer;
        private float fpsTimer;
        private int fpsFrames;
        private int cachedFps = 30;
        private readonly StringBuilder sb = new StringBuilder(256);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            EnsureEventSystem();
            BuildCompleteUserInterface();
        }

        private void Start()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.OnSubtitleTriggered += ShowSubtitle;
            }
            if (CampaignMissionManager.Instance != null)
            {
                CampaignMissionManager.Instance.OnObjectiveUpdated += (_, __) => RefreshObjectiveDisplay();
                CampaignMissionManager.Instance.OnMissionCompleted += ShowMissionCompleteBanner;
                CampaignMissionManager.Instance.OnMissionFailed += ShowMissionFailedBanner;
                CampaignMissionManager.Instance.OnCampaignVictory += ShowVictoryBanner;
            }
            ApplySavedHUDPositions();
            ShowMainMenu(true);
        }

        private void Update()
        {
            fpsTimer += Time.unscaledDeltaTime;
            fpsFrames++;
            if (fpsTimer >= 0.5f)
            {
                cachedFps = Mathf.RoundToInt(fpsFrames / fpsTimer);
                fpsTimer = 0f;
                fpsFrames = 0;
            }

            if (subtitleTimer > 0f)
            {
                subtitleTimer -= Time.deltaTime;
                if (subtitleTimer <= 0f && subtitleText != null) subtitleText.text = "";
            }

            if (hudPanel != null && hudPanel.activeSelf && PlayerController.Instance != null)
            {
                PlayerController p = PlayerController.Instance;
                sb.Clear();
                string stealthState = p.IsInSafeZone ? "SAFE ZONE" : (p.IsInConcealmentZone ? "CONCEALED" : "EXPOSED");
                sb.AppendFormat("STATE: {0} | STEALTH: {1} | STAMINA: {2:F0}% | {3} FPS",
                    p.CurrentState.ToString().ToUpper(), stealthState, p.CurrentStamina, cachedFps);
                stealthStatusText.text = sb.ToString();

                if (p.ActiveInteractable != null)
                {
                    interactButton.gameObject.SetActive(true);
                    interactButtonLabel.text = $"[ACTIVATE]\n{p.ActiveInteractable.GetInteractionPrompt()}";
                }
                else
                {
                    interactButton.gameObject.SetActive(false);
                }
            }
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }

        private void BuildCompleteUserInterface()
        {
            GameObject canvasGO = new GameObject("ShadowReckoning_Canvas");
            canvasGO.transform.SetParent(transform);
            rootCanvas = canvasGO.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            BuildHUD(canvasGO.transform);
            BuildMainMenu(canvasGO.transform);
            BuildSettingsPanel(canvasGO.transform);
            BuildMissionsPanel(canvasGO.transform);
            BuildCreditsPanel(canvasGO.transform);
            BuildMapOverlay(canvasGO.transform);
            BuildModalBanner(canvasGO.transform);
        }

        private void BuildHUD(Transform parent)
        {
            hudPanel = CreatePanel("InGameHUD", parent, new Color(0, 0, 0, 0));

            // Top-Left Mission Objective Box
            GameObject objBox = CreatePanel("ObjectiveBox", hudPanel.transform, new Color(0.05f, 0.08f, 0.1f, 0.78f));
            SetRect(objBox.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(260f, -95f), new Vector2(480f, 140f));
            objectiveText = CreateText("ObjectiveText", objBox.transform, "MISSION 01: THE SILENT ARRIVAL", 20, TextAnchor.UpperLeft);

            // Top Center Stealth & Stamina Bar
            GameObject statusBox = CreatePanel("StealthStatusBox", hudPanel.transform, new Color(0.05f, 0.08f, 0.1f, 0.75f));
            SetRect(statusBox.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(680f, 52f));
            stealthStatusText = CreateText("StealthText", statusBox.transform, "STEALTH: READY", 18, TextAnchor.MiddleCenter);

            // Top-Right Utility Buttons (Map & Pause)
            CreateButton("Btn_Map", hudPanel.transform, "TACTICAL MAP", new Vector2(1f, 1f), new Vector2(-240f, -55f), new Vector2(180f, 60f), () => mapOverlayPanel.SetActive(!mapOverlayPanel.activeSelf));
            CreateButton("Btn_Pause", hudPanel.transform, "PAUSE / MENU", new Vector2(1f, 1f), new Vector2(-95f, -55f), new Vector2(160f, 60f), () => ShowMainMenu(true));

            // Bottom-Left Virtual Joystick
            GameObject joyBg = CreatePanel("VirtualJoystick_BG", hudPanel.transform, new Color(1f, 1f, 1f, 0.18f));
            joystickContainer = joyBg.GetComponent<RectTransform>();
            SetRect(joystickContainer, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(210f, 210f), new Vector2(220f, 220f));

            GameObject joyHandle = CreatePanel("VirtualJoystick_Knob", joyBg.transform, new Color(0.2f, 0.78f, 0.45f, 0.75f));
            RectTransform handleRect = joyHandle.GetComponent<RectTransform>();
            SetRect(handleRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 90f));

            VirtualJoystickUI vj = joyBg.AddComponent<VirtualJoystickUI>();
            vj.backgroundRect = joystickContainer;
            vj.handleRect = handleRect;

            // Bottom-Right Touch Action Cluster
            GameObject cluster = CreatePanel("ActionCluster", hudPanel.transform, new Color(0, 0, 0, 0));
            actionClusterContainer = cluster.GetComponent<RectTransform>();
            SetRect(actionClusterContainer, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-240f, 210f), new Vector2(380f, 320f));

            Button sprintBtn = CreateButton("Btn_Sprint", cluster.transform, "SPRINT\n(HOLD)", new Vector2(0.5f, 0.5f), new Vector2(-105f, -45f), new Vector2(130f, 95f), null);
            sprintBtn.gameObject.AddComponent<SprintHoldButton>();

            CreateButton("Btn_Jump", cluster.transform, "JUMP /\nCLIMB", new Vector2(0.5f, 0.5f), new Vector2(75f, 45f), new Vector2(130f, 95f),
                () => MobileTouchInputManager.Instance?.TriggerJumpButton());

            CreateButton("Btn_Crouch", cluster.transform, "STEALTH\nCROUCH", new Vector2(0.5f, 0.5f), new Vector2(75f, -65f), new Vector2(130f, 95f),
                () => MobileTouchInputManager.Instance?.TriggerCrouchButton());

            interactButton = CreateButton("Btn_Interact", cluster.transform, "INTERACT", new Vector2(0.5f, 0.5f), new Vector2(-105f, 75f), new Vector2(165f, 95f),
                () => MobileTouchInputManager.Instance?.TriggerInteractButton());
            interactButtonLabel = interactButton.GetComponentInChildren<Text>();
            interactButton.gameObject.SetActive(false);

            // Bottom Center Subtitles Box
            GameObject subBox = CreatePanel("SubtitlePanel", hudPanel.transform, new Color(0f, 0f, 0f, 0.55f));
            SetRect(subBox.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 75f), new Vector2(920f, 70f));
            subtitleText = CreateText("SubtitleText", subBox.transform, "", 22, TextAnchor.MiddleCenter);
        }

        private void BuildMainMenu(Transform parent)
        {
            mainMenuPanel = CreatePanel("MainMenuPanel", parent, new Color(0.04f, 0.06f, 0.08f, 0.94f));
            SetRect(mainMenuPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Text title = CreateText("Title", mainMenuPanel.transform, "SHADOW RECKONING\n<size=24>TACTICAL MILITARY STEALTH & RESCUE OPERATIONS</size>", 48, TextAnchor.MiddleCenter);
            SetRect(title.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1100f, 150f));

            CreateButton("Btn_NewGame", mainMenuPanel.transform, "NEW CAMPAIGN", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(380f, 70f), () =>
            {
                SaveSystemManager.Instance?.ResetCampaignProgress();
                CampaignMissionManager.Instance?.StartMission(0, 0, true);
                ShowMainMenu(false);
            });

            CreateButton("Btn_Continue", mainMenuPanel.transform, "CONTINUE MISSION", new Vector2(0.5f, 0.5f), new Vector2(0f, 35f), new Vector2(380f, 70f), () =>
            {
                ShowMainMenu(false);
            });

            CreateButton("Btn_Missions", mainMenuPanel.transform, "MISSION SELECT (01 - 04)", new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(380f, 70f), () =>
            {
                missionsPanel.SetActive(true);
            });

            CreateButton("Btn_Settings", mainMenuPanel.transform, "SETTINGS & HUD LAYOUT", new Vector2(0.5f, 0.5f), new Vector2(0f, -135f), new Vector2(380f, 70f), () =>
            {
                RefreshSettingsSummary();
                settingsPanel.SetActive(true);
            });

            CreateButton("Btn_Credits", mainMenuPanel.transform, "CREDITS & ARCHITECTURE", new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(380f, 70f), () =>
            {
                creditsPanel.SetActive(true);
            });
        }

        private void BuildSettingsPanel(Transform parent)
        {
            settingsPanel = CreatePanel("SettingsPanel", parent, new Color(0.05f, 0.07f, 0.1f, 0.98f));
            SetRect(settingsPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            settingsSummaryText = CreateText("SettingsHeader", settingsPanel.transform, "SYSTEM SETTINGS", 28, TextAnchor.UpperCenter);
            SetRect(settingsSummaryText.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(950f, 160f));

            CreateButton("Btn_Preset", settingsPanel.transform, "CYCLE GRAPHICS PRESET (LOW / MED / HIGH)", new Vector2(0.5f, 0.5f), new Vector2(-230f, 90f), new Vector2(420f, 70f), () =>
            {
                if (GraphicsQualityManager.Instance != null)
                {
                    int next = ((int)GraphicsQualityManager.Instance.CurrentPreset + 1) % 3;
                    GraphicsQualityManager.Instance.ApplyPreset((GraphicsPreset)next);
                    RefreshSettingsSummary();
                }
            });

            CreateButton("Btn_FPS", settingsPanel.transform, "TOGGLE TARGET FPS (30 / 60 FPS)", new Vector2(0.5f, 0.5f), new Vector2(230f, 90f), new Vector2(420f, 70f), () =>
            {
                if (GraphicsQualityManager.Instance != null)
                {
                    int nextFps = GraphicsQualityManager.Instance.TargetFrameRate == 30 ? 60 : 30;
                    GraphicsQualityManager.Instance.ApplyFrameRate(nextFps);
                    RefreshSettingsSummary();
                }
            });

            CreateButton("Btn_Audio", settingsPanel.transform, "CYCLE MASTER VOLUME (25% / 60% / 100%)", new Vector2(0.5f, 0.5f), new Vector2(-230f, 0f), new Vector2(420f, 70f), () =>
            {
                float v = AudioListener.volume > 0.8f ? 0.25f : (AudioListener.volume < 0.4f ? 0.6f : 1.0f);
                AudioManager.Instance?.SetMasterVolume(v);
                RefreshSettingsSummary();
            });

            CreateButton("Btn_Sens", settingsPanel.transform, "CYCLE TOUCH SENSITIVITY (0.7x / 1.0x / 1.5x)", new Vector2(0.5f, 0.5f), new Vector2(230f, 0f), new Vector2(420f, 70f), () =>
            {
                if (MobileTouchInputManager.Instance != null && SaveSystemManager.Instance != null)
                {
                    float cur = MobileTouchInputManager.Instance.TouchSensitivity;
                    float next = cur < 0.9f ? 1.0f : (cur < 1.2f ? 1.5f : 0.7f);
                    MobileTouchInputManager.Instance.TouchSensitivity = next;
                    SaveSystemManager.Instance.CurrentData.touchSensitivity = next;
                    SaveSystemManager.Instance.SaveGame();
                    RefreshSettingsSummary();
                }
            });

            CreateButton("Btn_HUDLayout", settingsPanel.transform, "TOGGLE TOUCH BUTTON POSITION PRESET (WIDE / COMPACT)", new Vector2(0.5f, 0.5f), new Vector2(-230f, -90f), new Vector2(420f, 70f), () =>
            {
                if (SaveSystemManager.Instance != null)
                {
                    SaveData d = SaveSystemManager.Instance.CurrentData;
                    bool isWide = d.joystickOffsetX < 220f;
                    d.joystickOffsetX = isWide ? 280f : 190f;
                    d.actionButtonsOffsetX = isWide ? -290f : -220f;
                    SaveSystemManager.Instance.SaveGame();
                    ApplySavedHUDPositions();
                    RefreshSettingsSummary();
                }
            });

            CreateButton("Btn_Subtitles", settingsPanel.transform, "TOGGLE RADIO SUBTITLES (ON / OFF)", new Vector2(0.5f, 0.5f), new Vector2(230f, -90f), new Vector2(420f, 70f), () =>
            {
                if (SaveSystemManager.Instance != null)
                {
                    SaveSystemManager.Instance.CurrentData.subtitlesEnabled = !SaveSystemManager.Instance.CurrentData.subtitlesEnabled;
                    SaveSystemManager.Instance.SaveGame();
                    RefreshSettingsSummary();
                }
            });

            CreateButton("Btn_CloseSettings", settingsPanel.transform, "SAVE & RETURN", new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(360f, 70f), () =>
            {
                settingsPanel.SetActive(false);
            });

            settingsPanel.SetActive(false);
        }

        private void BuildMissionsPanel(Transform parent)
        {
            missionsPanel = CreatePanel("MissionsPanel", parent, new Color(0.05f, 0.07f, 0.1f, 0.98f));
            SetRect(missionsPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Text header = CreateText("MissionsHeader", missionsPanel.transform, "SELECT CAMPAIGN MISSION", 36, TextAnchor.MiddleCenter);
            SetRect(header.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(800f, 80f));

            string[] titles =
            {
                "MISSION 01: THE SILENT ARRIVAL (Outpost Alpha)",
                "MISSION 02: LOST SIGNAL (Urban Ruins & Industrial Complex)",
                "MISSION 03: BLACKSITE (Dense Forest & Underground Facility)",
                "MISSION 04: LAST EXTRACTION (North Ridge Helipad)"
            };

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                CreateButton($"Btn_Mission_{idx}", missionsPanel.transform, titles[i], new Vector2(0.5f, 0.5f), new Vector2(0f, 110f - i * 85f), new Vector2(680f, 68f), () =>
                {
                    CampaignMissionManager.Instance?.StartMission(idx, 0, true);
                    missionsPanel.SetActive(false);
                    ShowMainMenu(false);
                });
            }

            CreateButton("Btn_CloseMissions", missionsPanel.transform, "BACK TO MAIN MENU", new Vector2(0.5f, 0.5f), new Vector2(0f, -240f), new Vector2(360f, 68f), () =>
            {
                missionsPanel.SetActive(false);
            });

            missionsPanel.SetActive(false);
        }

        private void BuildCreditsPanel(Transform parent)
        {
            creditsPanel = CreatePanel("CreditsPanel", parent, new Color(0.04f, 0.06f, 0.09f, 0.98f));
            SetRect(creditsPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            string creditCopy = "SHADOW RECKONING — ANDROID TACTICAL ACTION-ADVENTURE\n\n" +
                                "Engine: Unity 6 (Universal Render Pipeline)\n" +
                                "Architecture: Modular C# Systems, Zero-GC Mobile HUD, Procedural Audio Synth\n" +
                                "World Zones: Outpost Alpha • Destroyed Urban Sector • Industrial Compl
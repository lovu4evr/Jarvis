using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShadowReckoning.Core
{
    public enum GraphicsPreset { Low = 0, Medium = 1, High = 2 }
    public enum WeatherState { ClearDusk = 0, DustStorm = 1, HeavyFog = 2 }

    public interface IInteractable
    {
        string GetInteractionPrompt();
        void Interact(GameObject interactor);
        Transform GetTransform();
        bool IsInteractable();
    }

    [Serializable]
    public class SaveData
    {
        public int currentMissionIndex = 0;
        public int currentObjectiveIndex = 0;
        public float[] checkpointPosition = new float[] { 0f, 1.5f, -35f };
        public List<string> collectedIntelIds = new List<string>();
        public int graphicsPreset = 1;
        public int targetFps = 30;
        public float masterVolume = 0.85f;
        public float touchSensitivity = 1.0f;
        public bool subtitlesEnabled = true;
        public float joystickOffsetX = 180f;
        public float joystickOffsetY = 180f;
        public float actionButtonsOffsetX = -180f;
        public float actionButtonsOffsetY = 180f;
    }

    [DefaultExecutionOrder(-100)]
    public class GameBootstrapper : MonoBehaviour
    {
        public static GameBootstrapper Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Time.fixedDeltaTime = 0.025f; // 40Hz physics tick for stable 30FPS Android
            Input.multiTouchEnabled = true;

            EnsureSubsystem<SaveSystemManager>("SaveSystemManager");
            EnsureSubsystem<GraphicsQualityManager>("GraphicsQualityManager");
            EnsureSubsystem<AudioManager>("AudioManager");
        }

        private T EnsureSubsystem<T>(string name) where T : Component
        {
            T existing = FindFirstObjectByType<T>();
            if (existing != null) return existing;
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform);
            return go.AddComponent<T>();
        }
    }

    public class SaveSystemManager : MonoBehaviour
    {
        public static SaveSystemManager Instance { get; private set; }
        public SaveData CurrentData { get; private set; } = new SaveData();
        private string SaveFilePath => Path.Combine(Application.persistentDataPath, "shadow_reckoning_save.json");

        public event Action<SaveData> OnSaveLoaded;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadGame();
        }

        public bool HasSavedCampaign()
        {
            return File.Exists(SaveFilePath) && CurrentData.currentMissionIndex >= 0;
        }

        public void SaveGame()
        {
            try
            {
                string json = JsonUtility.ToJson(CurrentData, true);
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Failed to write save file: {e.Message}");
            }
        }

        public void LoadGame()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    string json = File.ReadAllText(SaveFilePath);
                    CurrentData = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                }
                else
                {
                    CurrentData = new SaveData();
                }
            }
            catch
            {
                CurrentData = new SaveData();
            }
            OnSaveLoaded?.Invoke(CurrentData);
        }

        public void ResetCampaignProgress()
        {
            CurrentData.currentMissionIndex = 0;
            CurrentData.currentObjectiveIndex = 0;
            CurrentData.checkpointPosition = new float[] { 0f, 1.5f, -35f };
            CurrentData.collectedIntelIds.Clear();
            SaveGame();
        }

        public void SaveCheckpoint(int missionIdx, int objectiveIdx, Vector3 pos)
        {
            CurrentData.currentMissionIndex = missionIdx;
            CurrentData.currentObjectiveIndex = objectiveIdx;
            CurrentData.checkpointPosition = new float[] { pos.x, pos.y, pos.z };
            SaveGame();
        }
    }

    public class GraphicsQualityManager : MonoBehaviour
    {
        public static GraphicsQualityManager Instance { get; private set; }

        public GraphicsPreset CurrentPreset { get; private set; } = GraphicsPreset.Medium;
        public int TargetFrameRate { get; private set; } = 30;
        public WeatherState CurrentWeather { get; private set; } = WeatherState.ClearDusk;

        private Light directionalSun;
        private float weatherTimer;

        public event Action<GraphicsPreset> OnPresetChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (SaveSystemManager.Instance != null)
            {
                SaveData d = SaveSystemManager.Instance.CurrentData;
                ApplyFrameRate(d.targetFps);
                ApplyPreset((GraphicsPreset)Mathf.Clamp(d.graphicsPreset, 0, 2));
            }
            else
            {
                ApplyFrameRate(30);
                ApplyPreset(GraphicsPreset.Medium);
            }
        }

        private void Update()
        {
            weatherTimer += Time.deltaTime;
            if (weatherTimer > 90f)
            {
                weatherTimer = 0f;
                CycleWeather((WeatherState)(((int)CurrentWeather + 1) % 3));
            }
        }

        public void ApplyFrameRate(int fps)
        {
            TargetFrameRate = fps >= 45 ? 60 : 30;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;

            if (SaveSystemManager.Instance != null)
            {
                SaveSystemManager.Instance.CurrentData.targetFps = TargetFrameRate;
                SaveSystemManager.Instance.SaveGame();
            }
        }

        public void ApplyPreset(GraphicsPreset preset)
        {
            CurrentPreset = preset;
            int count = QualitySettings.names.Length;
            if ((int)preset < count)
            {
                QualitySettings.SetQualityLevel((int)preset, true);
            }

            UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset
                                               ?? QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

            switch (preset)
            {
                case GraphicsPreset.Low:
                    ConfigureURP(urp, 0.72f, 18f, 1, false);
                    QualitySettings.lodBias = 0.7f;
                    QualitySettings.globalTextureMipmapLimit = 1;
                    break;
                case GraphicsPreset.Medium:
                    ConfigureURP(urp, 0.85f, 32f, 2, true);
                    QualitySettings.lodBias = 1.0f;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    break;
                case GraphicsPreset.High:
                    ConfigureURP(urp, 1.0f, 50f, 4, true);
                    QualitySettings.lodBias = 1.25f;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    break;
            }

            if (SaveSystemManager.Instance != null)
            {
                SaveSystemManager.Instance.CurrentData.graphicsPreset = (int)preset;
                SaveSystemManager.Instance.SaveGame();
            }

            OnPresetChanged?.Invoke(CurrentPreset);
        }

        private void ConfigureURP(UniversalRenderPipelineAsset asset, float scale, float shadowDist, int msaa, bool hdr)
        {
            if (asset == null) return;
            asset.renderScale = scale;
            asset.shadowDistance = shadowDist;
            asset.msaaSampleCount = msaa;
            asset.supportsHDR = hdr;
        }

        public void CycleWeather(WeatherState state)
        {
            CurrentWeather = state;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            if (directionalSun == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional) { directionalSun = l; break; }
                }
            }

            switch (state)
            {
                case WeatherState.ClearDusk:
                    RenderSettings.fogColor = new Color(0.14f, 0.17f, 0.21f);
                    RenderSettings.fogDensity = 0.011f;
                    if (directionalSun != null) directionalSun.intensity = 1.35f;
                    break;
                case WeatherState.DustStorm:
                    RenderSettings.fogColor = new Color(0.38f, 0.28f, 0.18f);
                    RenderSettings.fogDensity = 0.024f;
                    if (directionalSun != null) directionalSun.intensity = 0.85f;
                    break;
                case WeatherState.HeavyFog:
                    RenderSettings.fogColor = new Color(0.16f, 0.20f, 0.22f);
                    RenderSettings.fogDensity = 0.028f;
                    if (directionalSun != null) directionalSun.intensity = 0.65f;
                    break;
            }
        }
    }

    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private AudioSource ambienceSource;
        private AudioSource uiSource;
        private readonly List<AudioSource> spatialPool = new List<AudioSource>();

        private AudioClip footstepClip;
        private AudioClip terminalBeepClip;
        private AudioClip radioChirpClip;
        private AudioClip alertSirenClip;
        private AudioClip ambientWindClip;

        public event Action<string, float> OnSubtitleTriggered;
        public static event Action<Vector3, float> OnSoundEmitted;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            GenerateProceduralAudioClips();

            ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.clip = ambientWindClip;
            ambienceSource.loop = true;
            ambienceSource.volume = 0.25f;
            ambienceSource.Play();

            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.spatialBlend = 0f;

            for (int i = 0; i < 8; i++)
            {
                GameObject child = new GameObject($"PooledAudio_{i}");
                child.transform.SetParent(transform);
                AudioSource src = child.AddComponent<AudioSource>();
                src.spatialBlend = 1.0f;
                src.minDistance = 2f;
                src.maxDistance = 28f;
                spatialPool.Add(src);
            }
        }

        private void Start()
        {
            if (SaveSystemManager.Instance != null)
            {
                SetMasterVolume(SaveSystemManager.Instance.CurrentData.masterVolume);
            }
        }

        public void SetMasterVolume(float volume)
        {
            AudioListener.volume = Mathf.Clamp01(volume);
            if (SaveSystemManager.Instance != null)
            {
                SaveSystemManager.Instance.CurrentData.masterVolume = AudioListener.volume;
                SaveSystemManager.Instance.SaveGame();
            }
        }

        public void PlayFootstep(Vector3 position, float noiseRadius, float volume = 0.45f)
        {
            PlaySpatialClip(footstepClip, position, volume, UnityEngine.Random.Range(0.88f, 1.12f));
            EmitSoundWave(position, noiseRadius);
        }

        public void PlayTerminalInteract(Vector3 position)
        {
            PlaySpatialClip(terminalBeepClip, position, 0.7f, 1f);
        }

        public void PlayAlertSound(Vector3 position)
        {
            PlaySpatialClip(alertSirenClip, position, 0.8f, 1f);
        }

        public void TriggerRadioTransmission(string speaker, string subtitleLine, float duration = 4.5f)
        {
            if (uiSource != null && radioChirpClip != null)
            {
                uiSource.PlayOneShot(radioChirpClip, 0.6f);
            }
            OnSubtitleTriggered?.Invoke($"[{speaker}]: {subtitleLine}", duration);
        }

        public static void EmitSoundWave(Vector3 origin, float radius)
        {
            if (radius > 0.1f)
            {
                OnSoundEmitted?.Invoke(origin, radius);
            }
        }

        private void PlaySpatialClip(AudioClip clip, Vector3 pos, float vol, float pitch)
        {
            if (clip == null) return;
            foreach (var src in spatialPool)
            {
                if (!src.isPlaying)
                {
                    src.transform.position = pos;
                    src.clip = clip;
                    src.volume = vol;
                    src.pitch = pitch;
                    src.Play();
                    return;
                }
            }
        }

        private void GenerateProceduralAudioClips()
        {
            int sampleRate = 22050;

            // 1. Tactical Boot Footstep Thud
            footstepClip = AudioClip.Create("Proc_Footstep", sampleRate / 8, 1, sampleRate, false);
            float[] stepSamples = new float[sampleRate / 8];
            for (int i = 0; i < stepSamples.Length; i++)
            {
                float t = (float)i / stepSamples.Length;
                float env = Mathf.Exp(-t * 14f);
                stepSamples[i] = (Mathf.Sin(2f * Mathf.PI * 85f * t) * 0.6f + UnityEngine.Random.Range(-0.4f, 0.4f)) * env;
            }
            footstepClip.SetData(stepSamples, 0);

            // 2. Comms Terminal Beep
            terminalBeepClip = AudioClip.Create("Proc_TerminalBeep", sampleRate / 5, 1, sampleRate, false);
            float[] beepSamples = new float[sampleRate / 5];
            for (int i = 0; i < beepSamples.Length; i++)
            {
                float t = (float)i / sampleRate;
                float freq = i < beepSamples.Length / 2 ? 880f : 1320f;
                beepSamples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f;
            }
            terminalBeepClip.SetData(beepSamples, 0);

            // 3. Radio Squelch Chirp
            radioChirpClip = AudioClip.Create("Proc_RadioChirp", sampleRate / 6, 1, sampleRate, false);
            float[] radioSamples = new float[sampleRate / 6];
            for (int i = 0; i < radioSamples.Length; i++)
            {
                float t = (float)i / sampleRate;
                radioSamples[i] = (Mathf.Sin(2f * Mathf.PI * 1650f * t) * 0.25f + UnityEngine.Random.Range(-0.15f, 0.15f) * 0.5f);
            }
            radioChirpClip.SetData(radioSamples, 0);

            // 4. Alert Stinger
            alertSirenClip = AudioClip.Create("Proc_Alert", sampleRate / 3, 1, sampleRate, false);
            float[] alertSamples = new float[sampleRate / 3];
            for (int i = 0; i < alertSamples.Length; i++)
            {
                float t = (float)i / sampleRate;
                float freq = Mathf.Lerp(420f, 740f, (float)i / alertSamples.Length);
                alertSamples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.4f;
            }
            alertSirenClip.SetData(alertSamples, 0);

            // 5. Atmospheric Wind Loop (3 Seconds)
            ambientWindClip = AudioClip.Create("Proc_AmbientWind", sampleRate * 3, 1, sampleRate, false);
            float[] windSamples = new float[sampleRate * 3];
            float lastOut = 0f;
            for (int i = 0; i < windSamples.Length; i++)
            {
                float white = UnityEngine.Random.Range(-1f, 1f);
                lastOut = (lastOut + (0.04f * white)) / 1.04f;
                float swell = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * i / windSamples.Length);
                windSamples[i] = lastOut * swell * 0.35f;
            }
            ambientWindClip.SetData(windSamples, 0);
        }
    }
}

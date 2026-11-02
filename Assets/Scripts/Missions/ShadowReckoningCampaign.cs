using System;
using System.Collections.Generic;
using UnityEngine;
using ShadowReckoning.Core;
using ShadowReckoning.Player;
using ShadowReckoning.AI;

namespace ShadowReckoning.Missions
{
    public enum InteractableRole
    {
        MissionObjectiveTerminal,
        IntelDocument,
        PowerGeneratorPuzzle,
        ExtractionZone
    }

    public class WorldInteractable : MonoBehaviour, IInteractable
    {
        public string interactableId = "OBJ_01";
        public string promptLabel = "Activate Communications Terminal";
        public InteractableRole role = InteractableRole.MissionObjectiveTerminal;
        public int targetMissionIndex = 0;
        public int targetObjectiveStep = 0;
        public string radioSpeaker = "COMMAND HQ";
        public string radioMessage = "Uplink established. Proceeding to next phase.";

        private bool hasBeenActivated;

        public static event Action<WorldInteractable> OnWorldInteractableUsed;

        public string GetInteractionPrompt() => promptLabel;

        public bool IsInteractable()
        {
            if (hasBeenActivated && role != InteractableRole.ExtractionZone) return false;
            if (CampaignMissionManager.Instance != null && role == InteractableRole.MissionObjectiveTerminal)
            {
                return CampaignMissionManager.Instance.CurrentMissionIndex == targetMissionIndex &&
                       CampaignMissionManager.Instance.CurrentObjectiveStep == targetObjectiveStep;
            }
            return !hasBeenActivated;
        }

        public void Interact(GameObject interactor)
        {
            hasBeenActivated = true;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayTerminalInteract(transform.position);
                if (!string.IsNullOrEmpty(radioMessage))
                {
                    AudioManager.Instance.TriggerRadioTransmission(radioSpeaker, radioMessage, 5f);
                }
            }

            if (ThirdPersonCameraController.Instance != null)
            {
                Vector3 camPos = transform.position - transform.forward * 3.5f + Vector3.up * 2.2f;
                ThirdPersonCameraController.Instance.TriggerCinematicLook(camPos, transform.position + Vector3.up * 1f, 2.2f);
            }

            OnWorldInteractableUsed?.Invoke(this);
        }

        public Transform GetTransform() => transform;
    }

    public class StealthZoneTrigger : MonoBehaviour
    {
        public bool isSafeZone;
        public bool isFoliageConcealment = true;

        private void OnTriggerEnter(Collider other)
        {
            if (PlayerController.Instance != null && other.gameObject == PlayerController.Instance.gameObject)
            {
                if (isSafeZone) PlayerController.Instance.IsInSafeZone = true;
                if (isFoliageConcealment) PlayerController.Instance.IsInConcealmentZone = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (PlayerController.Instance != null && other.gameObject == PlayerController.Instance.gameObject)
            {
                if (isSafeZone) PlayerController.Instance.IsInSafeZone = false;
                if (isFoliageConcealment) PlayerController.Instance.IsInConcealmentZone = false;
            }
        }
    }

    [Serializable]
    public class MissionDefinition
    {
        public string missionCode;
        public string missionTitle;
        public string[] objectiveDescriptions;
        public Vector3 checkpointSpawnPos;
        public string rewardSummary;
    }

    public class CampaignMissionManager : MonoBehaviour
    {
        public static CampaignMissionManager Instance { get; private set; }

        public List<MissionDefinition> Missions { get; private set; } = new List<MissionDefinition>();
        public int CurrentMissionIndex { get; private set; }
        public int CurrentObjectiveStep { get; private set; }
        public int IntelCollectedCount { get; private set; }

        public event Action<MissionDefinition, int> OnObjectiveUpdated;
        public event Action<MissionDefinition> OnMissionCompleted;
        public event Action<string> OnMissionFailed;
        public event Action OnCampaignVictory;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            InitializeCampaignDefinitions();
        }

        private void OnEnable()
        {
            WorldInteractable.OnWorldInteractableUsed += HandleInteractableUsed;
            SurvivorCompanionAI.OnSurvivorRecruited += HandleSurvivorRecruited;
            PatrolAIController.OnPlayerCapturedByPatrol += HandlePlayerCaptured;
        }

        private void OnDisable()
        {
            WorldInteractable.OnWorldInteractableUsed -= HandleInteractableUsed;
            SurvivorCompanionAI.OnSurvivorRecruited -= HandleSurvivorRecruited;
            PatrolAIController.OnPlayerCapturedByPatrol -= HandlePlayerCaptured;
        }

        private void Start()
        {
            if (SaveSystemManager.Instance != null)
            {
                SaveData d = SaveSystemManager.Instance.CurrentData;
                IntelCollectedCount = d.collectedIntelIds.Count;
                StartMission(Mathf.Clamp(d.currentMissionIndex, 0, Missions.Count - 1), d.currentObjectiveIndex, false);
            }
            else
            {
                StartMission(0, 0, false);
            }
        }

        private void InitializeCampaignDefinitions()
        {
            Missions = new List<MissionDefinition>
            {
                new MissionDefinition
                {
                    missionCode = "MISSION 01",
                    missionTitle = "THE SILENT ARRIVAL",
                    checkpointSpawnPos = new Vector3(0f, 1.5f, -38f),
                    rewardSummary = "Unlocked: Long-Range Tactical Scanner & Industrial Sector Access",
                    objectiveDescriptions = new[]
                    {
                        "Infiltrate Outpost Alpha and reboot the Aux Power Generator.",
                        "Climb the Command Bunker relay and establish the Satellite Comms Link."
                    }
                },
                new MissionDefinition
                {
                    missionCode = "MISSION 02",
                    missionTitle = "LOST SIGNAL",
                    checkpointSpawnPos = new Vector3(12f, 1.5f, 10f),
                    rewardSummary = "Unlocked: Blacksite Security Keycard & Encrypted Frequency Decoder",
                    objectiveDescriptions = new[]
                    {
                        "Navigate the Destroyed Urban Settlement and override the Industrial Gate.",
                        "Locate and rescue missing research lead Dr. Elena Vance inside the Industrial Complex."
                    }
                },
                new MissionDefinition
                {
                    missionCode = "MISSION 03",
                    missionTitle = "BLACKSITE",
                    checkpointSpawnPos = new Vector3(45f, 1.5f, 18f),
                    rewardSummary = "Unlocked: Project ECLIPSE Core Telemetry & Extraction Beacon",
                    objectiveDescriptions = new[]
                    {
                        "Cross the Dense Forest perimeter and enter the Underground Research Facility.",
                        "Download Project ECLIPSE experiment logs from the subterranean mainframe."
                    }
                },
                new MissionDefinition
                {
                    missionCode = "MISSION 04",
                    missionTitle = "LAST EXTRACTION",
                    checkpointSpawnPos = new Vector3(-35f, 1.5f, 32f),
                    rewardSummary = "Campaign Complete: Elite Rescue Specialist Commendation Awarded",
                    objectiveDescriptions = new[]
                    {
                        "Escort survivors and reach the North Ridge Helipad Extraction Zone.",
                        "Signal Razor-1 Extraction Transport and secure the landing zone."
                    }
                }
            };
        }

        public void StartMission(int missionIndex, int stepIndex = 0, bool teleportPlayer = true)
        {
            CurrentMissionIndex = Mathf.Clamp(missionIndex, 0, Missions.Count - 1);
            CurrentObjectiveStep = Mathf.Clamp(stepIndex, 0, Missions[CurrentMissionIndex].objectiveDescriptions.Length - 1);

            MissionDefinition m = Missions[CurrentMissionIndex];
            if (teleportPlayer && PlayerController.Instance != null)
            {
                PlayerController.Instance.TeleportTo(m.checkpointSpawnPos);
            }

            if (SaveSystemManager.Instance != null)
            {
                SaveSystemManager.Instance.SaveCheckpoint(CurrentMissionIndex, CurrentObjectiveStep, m.checkpointSpawnPos);
            }

            OnObjectiveUpdated?.Invoke(m, CurrentObjectiveStep);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.TriggerRadioTransmission(
                    "OVERWATCH HQ",
                    $"{m.missionCode}: {m.missionTitle} — {m.objectiveDescriptions[CurrentObjectiveStep]}",
                    5f
                );
            }
        }

        private void HandleInteractableUsed(WorldInteractable item)
        {
            if (item.role == InteractableRole.IntelDocument)
            {
                IntelCollectedCount++;
                if (SaveSystemManager.Instance != null && !SaveSystemManager.Instance.CurrentData.collectedIntelIds.Contains(item.interactableId))
                {
                    SaveSystemManager.Instance.CurrentData.collectedIntelIds.Add(item.interactableId);
                    SaveSystemManager.Instance.SaveGame();
                }
                return;
            }

            if (item.targetMissionIndex == CurrentMissionIndex && item.targetObjectiveStep == CurrentObjectiveStep)
            {
                AdvanceObjectiveStep();
            }
        }

        private void HandleSurvivorRecruited(SurvivorCompanionAI survivor)
        {
            if (CurrentMissionIndex == 1 && CurrentObjectiveStep == 1)
            {
                AdvanceObjectiveStep();
            }
        }

        public void AdvanceObjectiveStep()
        {
            MissionDefinition currentMission = Missions[CurrentMissionIndex];
            CurrentObjectiveStep++;

            if (CurrentObjectiveStep >= currentMission.objectiveDescriptions.Length)
            {
                OnMissionCompleted?.Invoke(currentMission);

                if (CurrentMissionIndex + 1 < Missions.Count)
                {
                    CurrentMissionIndex++;
                    CurrentObjectiveStep = 0;
                    MissionDefinition next = Missions[CurrentMissionIndex];
                    if (SaveSystemManager.Instance != null)
                    {
                        SaveSystemManager.Instance.SaveCheckpoint(CurrentMissionIndex, 0, next.checkpointSpawnPos);
                    }
                    OnObjectiveUpdated?.Invoke(next, 0);
                }
                else
                {
                    OnCampaignVictory?.Invoke();
                }
            }
            else
            {
                if (SaveSystemManager.Instance != null)
                {
                    Vector3 pos = PlayerController.Instance != null ? PlayerController.Instance.transform.position : currentMission.checkpointSpawnPos;
                    SaveSystemManager.Instance.SaveCheckpoint(CurrentMissionIndex, CurrentObjectiveStep, pos);
                }
                OnObjectiveUpdated?.Invoke(currentMission, CurrentObjectiveStep);
            }
        }

        private void HandlePlayerCaptured(PatrolAIController patrol)
        {
            OnMissionFailed?.Invoke("COMPROMISED: Hostile patrol intercepted you. Respawning at last checkpoint...");
        }

        public void RestartFromLastCheckpoint()
        {
            MissionDefinition m = Missions[CurrentMissionIndex];
            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.TeleportTo(m.checkpointSpawnPos);
            }
            OnObjectiveUpdated?.Invoke(m, CurrentObjectiveStep);
        }

        public string GetCurrentObjectiveText()
        {
            if (Missions.Count == 0) return "";
            MissionDefinition m = Missions[CurrentMissionIndex];
            string step = m.objectiveDescriptions[Mathf.Clamp(CurrentObjectiveStep, 0, m.objectiveDescriptions.Length - 1)];
            return $"{m.missionCode}: {m.missionTitle}\n• {step}\n• Intel Collected: {IntelCollectedCount}/4";
        }
    }
}

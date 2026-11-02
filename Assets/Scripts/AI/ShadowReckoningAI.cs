using System;
using UnityEngine;
using UnityEngine.AI;
using ShadowReckoning.Core;
using ShadowReckoning.Player;

namespace ShadowReckoning.AI
{
    public enum AIAlertState
    {
        Patrol = 0,
        Suspicious = 1,
        Search = 2,
        Alert = 3
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class PatrolAIController : MonoBehaviour
    {
        [Header("Patrol Configuration")]
        public Transform[] waypoints;
        [SerializeField] private float patrolSpeed = 2.4f;
        [SerializeField] private float alertSpeed = 4.6f;
        [SerializeField] private float viewDistance = 20f;
        [SerializeField] private float viewAngle = 85f;
        [SerializeField] private float captureDistance = 1.65f;

        public AIAlertState CurrentAlertState { get; private set; } = AIAlertState.Patrol;
        public float AwarenessMeter { get; private set; } = 0f; // 0 to 100

        private NavMeshAgent agent;
        private int currentWaypointIndex;
        private Vector3 lastKnownPlayerPosition;
        private float searchTimer;
        private Renderer statusIndicatorRenderer;

        public static event Action<PatrolAIController> OnPlayerCapturedByPatrol;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            Transform indicator = transform.Find("AlertIndicator");
            if (indicator != null) statusIndicatorRenderer = indicator.GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            AudioManager.OnSoundEmitted += HandleSoundHeard;
        }

        private void OnDisable()
        {
            AudioManager.OnSoundEmitted -= HandleSoundHeard;
        }

        private void Start()
        {
            AdvanceToNextWaypoint();
        }

        private void Update()
        {
            if (PlayerController.Instance == null) return;

            EvaluateVisualDetection();
            UpdateStateMachine();
            UpdateVisualIndicator();
        }

        private void EvaluateVisualDetection()
        {
            PlayerController player = PlayerController.Instance;
            if (player.IsInSafeZone)
            {
                AwarenessMeter = Mathf.MoveTowards(AwarenessMeter, 0f, 30f * Time.deltaTime);
                return;
            }

            Vector3 eyePos = transform.position + Vector3.up * 1.6f;
            Vector3 playerChest = player.transform.position + Vector3.up * 1.0f;
            Vector3 toPlayer = playerChest - eyePos;
            float dist = toPlayer.magnitude;

            float effectiveRange = viewDistance * player.VisibilityFactor;
            bool canSee = false;

            if (dist <= effectiveRange)
            {
                float angle = Vector3.Angle(transform.forward, toPlayer.normalized);
                if (angle <= viewAngle * 0.5f)
                {
                    if (!Physics.Raycast(eyePos, toPlayer.normalized, dist - 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        canSee = true;
                    }
                }
            }

            if (canSee)
            {
                float detectionRate = Mathf.Lerp(95f, 25f, dist / effectiveRange) * player.VisibilityFactor;
                AwarenessMeter = Mathf.Min(100f, AwarenessMeter + detectionRate * Time.deltaTime);
                lastKnownPlayerPosition = player.transform.position;

                if (AwarenessMeter >= 100f && CurrentAlertState != AIAlertState.Alert)
                {
                    EnterAlertState();
                }
                else if (AwarenessMeter >= 35f && CurrentAlertState == AIAlertState.Patrol)
                {
                    CurrentAlertState = AIAlertState.Suspicious;
                }
            }
            else
            {
                AwarenessMeter = Mathf.MoveTowards(AwarenessMeter, 0f, 16f * Time.deltaTime);
            }
        }

        private void HandleSoundHeard(Vector3 soundOrigin, float soundRadius)
        {
            float dist = Vector3.Distance(transform.position, soundOrigin);
            if (dist <= soundRadius)
            {
                lastKnownPlayerPosition = soundOrigin;
                AwarenessMeter = Mathf.Min(100f, AwarenessMeter + 40f);
                if (CurrentAlertState == AIAlertState.Patrol || CurrentAlertState == AIAlertState.Suspicious)
                {
                    CurrentAlertState = AIAlertState.Search;
                    searchTimer = 9f;
                    if (agent.isOnNavMesh) agent.SetDestination(lastKnownPlayerPosition);
                }
            }
        }

        private void EnterAlertState()
        {
            CurrentAlertState = AIAlertState.Alert;
            searchTimer = 12f;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayAlertSound(transform.position);
            }
        }

        private void UpdateStateMachine()
        {
            if (!agent.isOnNavMesh) return;

            switch (CurrentAlertState)
            {
                case AIAlertState.Patrol:
                    agent.speed = patrolSpeed;
                    if (!agent.pathPending && agent.remainingDistance < 0.8f)
                    {
                        AdvanceToNextWaypoint();
                    }
                    break;

                case AIAlertState.Suspicious:
                    agent.speed = 0.5f;
                    Vector3 lookDir = (lastKnownPlayerPosition - transform.position);
                    lookDir.y = 0f;
                    if (lookDir.sqrMagnitude > 0.1f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 4f);
                    }
                    if (AwarenessMeter <= 5f)
                    {
                        CurrentAlertState = AIAlertState.Patrol;
                        AdvanceToNextWaypoint();
                    }
                    break;

                case AIAlertState.Search:
                    agent.speed = patrolSpeed * 1.25f;
                    searchTimer -= Time.deltaTime;
                    if (searchTimer <= 0f)
                    {
                        CurrentAlertState = AIAlertState.Patrol;
                        AdvanceToNextWaypoint();
                    }
                    break;

                case AIAlertState.Alert:
                    agent.speed = alertSpeed;
                    agent.SetDestination(lastKnownPlayerPosition);

                    float distToPlayer = Vector3.Distance(transform.position, PlayerController.Instance.transform.position);
                    if (distToPlayer <= captureDistance && !PlayerController.Instance.IsInSafeZone)
                    {
                        OnPlayerCapturedByPatrol?.Invoke(this);
                        ResetPatrolAfterCapture();
                        return;
                    }

                    if (AwarenessMeter < 50f)
                    {
                        searchTimer -= Time.deltaTime;
                        if (searchTimer <= 0f)
                        {
                            CurrentAlertState = AIAlertState.Search;
                            searchTimer = 6f;
                        }
                    }
                    break;
            }
        }

        private void AdvanceToNextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0 || !agent.isOnNavMesh) return;
            agent.SetDestination(waypoints[currentWaypointIndex].position);
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }

        public void ResetPatrolAfterCapture()
        {
            AwarenessMeter = 0f;
            CurrentAlertState = AIAlertState.Patrol;
            AdvanceToNextWaypoint();
        }

        private void UpdateVisualIndicator()
        {
            if (statusIndicatorRenderer == null) return;
            Color c = CurrentAlertState switch
            {
                AIAlertState.Alert => Color.red,
                AIAlertState.Search => new Color(1f, 0.5f, 0f),
                AIAlertState.Suspicious => Color.yellow,
                _ => Color.green
            };
            statusIndicatorRenderer.material.color = c;
        }
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class SurvivorCompanionAI : MonoBehaviour, IInteractable
    {
        public string survivorName = "Dr. Elena Vance";
        public string rescueDialogue = "Thank God you found us! Blackwatch locked down the valley after the signal array overloaded. Lead the way to the extraction helipad!";
        public int requiredMissionIndex = 1;

        public bool IsFollowingPlayer { get; private set; }
        public bool HasBeenExtracted { get; set; }

        private NavMeshAgent agent;

        public static event Action<SurvivorCompanionAI> OnSurvivorRecruited;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.stoppingDistance = 2.4f;
            agent.speed = 4.6f;
        }

        private void Update()
        {
            if (IsFollowingPlayer && !HasBeenExtracted && PlayerController.Instance != null && agent.isOnNavMesh)
            {
                agent.SetDestination(PlayerController.Instance.transform.position);
            }
        }

        public string GetInteractionPrompt()
        {
            return IsFollowingPlayer ? $"{survivorName} (Following)" : $"Rescue {survivorName}";
        }

        public bool IsInteractable()
        {
            return !IsFollowingPlayer && !HasBeenExtracted;
        }

        public void Interact(GameObject interactor)
        {
            if (IsFollowingPlayer || HasBeenExtracted) return;
            IsFollowingPlayer = true;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.TriggerRadioTransmission(survivorName, rescueDialogue, 5.5f);
            }
            OnSurvivorRecruited?.Invoke(this);
        }

        public Transform GetTransform() => transform;
    }
}

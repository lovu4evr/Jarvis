using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ShadowReckoning.Core;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace ShadowReckoning.Player
{
    public enum PlayerLocomotionState
    {
        Idle,
        Walking,
        Running,
        Sprinting,
        Crouching,
        Jumping,
        Vaulting,
        Climbing,
        Swimming
    }

    public class MobileTouchInputManager : MonoBehaviour
    {
        public static MobileTouchInputManager Instance { get; private set; }

        public Vector2 MoveInput { get; private set; }
        public Vector2 LookDelta { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool CrouchPressedThisFrame { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }

        private Vector2 virtualJoystickInput;
        private bool uiSprintHeld;
        private bool uiJumpQueued;
        private bool uiCrouchQueued;
        private bool uiInteractQueued;

        private int lookTouchId = -1;
        public float TouchSensitivity { get; set; } = 1.0f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Start()
        {
            if (SaveSystemManager.Instance != null)
            {
                TouchSensitivity = SaveSystemManager.Instance.CurrentData.touchSensitivity;
            }
        }

        public void SetVirtualJoystick(Vector2 value) => virtualJoystickInput = Vector2.ClampMagnitude(value, 1f);
        public void SetSprintButton(bool held) => uiSprintHeld = held;
        public void TriggerJumpButton() => uiJumpQueued = true;
        public void TriggerCrouchButton() => uiCrouchQueued = true;
        public void TriggerInteractButton() => uiInteractQueued = true;

        private void Update()
        {
            Vector2 kbMove = Vector2.zero;
            Vector2 look = Vector2.zero;
            bool kbSprint = false;
            bool kbJump = false;
            bool kbCrouch = false;
            bool kbInteract = false;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed) kbMove.y += 1f;
                if (Keyboard.current.sKey.isPressed) kbMove.y -= 1f;
                if (Keyboard.current.dKey.isPressed) kbMove.x += 1f;
                if (Keyboard.current.aKey.isPressed) kbMove.x -= 1f;
                kbSprint = Keyboard.current.leftShiftKey.isPressed;
                kbJump = Keyboard.current.spaceKey.wasPressedThisFrame;
                kbCrouch = Keyboard.current.cKey.wasPressedThisFrame || Keyboard.current.leftCtrlKey.wasPressedThisFrame;
                kbInteract = Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.fKey.wasPressedThisFrame;
            }

            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                look += Mouse.current.delta.ReadValue() * 0.14f * TouchSensitivity;
            }

            // Right-side screen touch look
            float halfWidth = Screen.width * 0.45f;
            foreach (Touch t in Touch.activeTouches)
            {
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Began && t.screenPosition.x > halfWidth && lookTouchId == -1)
                {
                    lookTouchId = t.finger.index;
                }
                else if (t.finger.index == lookTouchId)
                {
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                    {
                        look += t.delta * 0.16f * TouchSensitivity;
                    }
                    else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    {
                        lookTouchId = -1;
                    }
                }
            }

            MoveInput = virtualJoystickInput.sqrMagnitude > 0.01f ? virtualJoystickInput : Vector2.ClampMagnitude(kbMove, 1f);
            LookDelta = look;
            SprintHeld = uiSprintHeld || kbSprint;
            JumpPressedThisFrame = uiJumpQueued || kbJump;
            CrouchPressedThisFrame = uiCrouchQueued || kbCrouch;
            InteractPressedThisFrame = uiInteractQueued || kbInteract;

            uiJumpQueued = false;
            uiCrouchQueued = false;
            uiInteractQueued = false;
        }
    }

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        [Header("Movement Speeds")]
        [SerializeField] private float walkSpeed = 2.6f;
        [SerializeField] private float runSpeed = 4.8f;
        [SerializeField] private float sprintSpeed = 7.2f;
        [SerializeField] private float crouchSpeed = 2.1f;
        [SerializeField] private float swimSpeed = 3.2f;
        [SerializeField] private float rotationSmoothTime = 0.1f;

        [Header("Vertical & Parkour")]
        [SerializeField] private float jumpHeight = 1.35f;
        [SerializeField] private float gravity = -19.62f;
        [SerializeField] private float waterSurfaceY = -0.35f;

        [Header("Stamina & Stealth")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float interactionRadius = 2.8f;

        public PlayerLocomotionState CurrentState { get; private set; } = PlayerLocomotionState.Idle;
        public float CurrentStamina { get; private set; } = 100f;
        public float MaxStamina => maxStamina;
        public float VisibilityFactor { get; private set; } = 1.0f;
        public float CurrentNoiseRadius { get; private set; } = 0f;
        public bool IsInConcealmentZone { get; set; }
        public bool IsInSafeZone { get; set; }
        public IInteractable ActiveInteractable { get; private set; }

        private CharacterController controller;
        private Transform visualRoot;
        private float verticalVelocity;
        private float turnSmoothVelocity;
        private bool isCrouching;
        private float footstepTimer;

        // Parkour state
        private bool isPerformingParkour;
        private Vector3 parkourStartPos;
        private Vector3 parkourTargetPos;
        private float parkourTimer;
        private float parkourDuration;

        // Zero-allocation interaction buffer
        private readonly Collider[] interactionHits = new Collider[12];

        private void Awake()
        {
            Instance = this;
            controller = GetComponent<CharacterController>();
            CurrentStamina = maxStamina;

            Transform foundVisual = transform.Find("VisualModel");
            visualRoot = foundVisual != null ? foundVisual : transform;
        }

        private void Update()
        {
            if (MobileTouchInputManager.Instance == null) return;

            ScanForInteractables();

            if (MobileTouchInputManager.Instance.InteractPressedThisFrame && ActiveInteractable != null)
            {
                ActiveInteractable.Interact(gameObject);
            }

            if (isPerformingParkour)
            {
                UpdateParkourAction();
                return;
            }

            CheckWaterSubmersion();

            if (CurrentState == PlayerLocomotionState.Swimming)
            {
                UpdateSwimmingMovement();
            }
            else
            {
                UpdateGroundedMovement();
            }

            UpdateStealthMetrics();
        }

        private void CheckWaterSubmersion()
        {
            if (transform.position.y < waterSurfaceY - 0.55f)
            {
                CurrentState = PlayerLocomotionState.Swimming;
                isCrouching = false;
            }
            else if (CurrentState == PlayerLocomotionState.Swimming && transform.position.y >= waterSurfaceY - 0.35f)
            {
                CurrentState = PlayerLocomotionState.Idle;
            }
        }

        private void UpdateSwimmingMovement()
        {
            Vector2 input = MobileTouchInputManager.Instance.MoveInput;
            Transform cam = Camera.main != null ? Camera.main.transform : transform;

            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            Vector3 moveDir = (forward * input.y + right * input.x);

            if (moveDir.sqrMagnitude > 0.01f)
            {
                float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, rotationSmoothTime);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }

            float buoyancy = (waterSurfaceY - 0.65f - transform.position.y) * 4f;
            Vector3 velocity = moveDir * swimSpeed + Vector3.up * buoyancy;
            controller.Move(velocity * Time.deltaTime);

            CurrentStamina = Mathf.MoveTowards(CurrentStamina, maxStamina, 8f * Time.deltaTime);
        }

        private void UpdateGroundedMovement()
        {
            var inputMgr = MobileTouchInputManager.Instance;
            Vector2 input = inputMgr.MoveInput;
            float inputMag = input.magnitude;

            if (inputMgr.CrouchPressedThisFrame && controller.isGrounded)
            {
                isCrouching = !isCrouching;
                controller.height = isCrouching ? 1.15f : 1.8f;
                controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
                if (visualRoot != transform)
                {
                    visualRoot.localScale = new Vector3(1f, isCrouching ? 0.65f : 1f, 1f);
                }
            }

            if (inputMgr.JumpPressedThisFrame && controller.isGrounded)
            {
                if (TryInitiateVaultOrClimb()) return;

                if (isCrouching)
                {
                    isCrouching = false;
                    controller.height = 1.8f;
                    controller.center = new Vector3(0f, 0.9f, 0f);
                    if (visualRoot != transform) visualRoot.localScale = Vector3.one;
                }
                else
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    CurrentState = PlayerLocomotionState.Jumping;
                }
            }

            bool canSprint = inputMgr.SprintHeld && CurrentStamina > 5f && inputMag > 0.5f && !isCrouching;
            float targetSpeed = 0f;

            if (inputMag > 0.05f)
            {
                if (isCrouching)
                {
                    targetSpeed = crouchSpeed;
                    CurrentState = PlayerLocomotionState.Crouching;
                }
                else if (canSprint)
                {
                    targetSpeed = sprintSpeed;
                    CurrentState = PlayerLocomotionState.Sprinting;
                    CurrentStamina = Mathf.Max(0f, CurrentStamina - 18f * Time.deltaTime);
                }
                else if (inputMag > 0.55f)
                {
                    targetSpeed = runSpeed;
                    CurrentState = PlayerLocomotionState.Running;
                }
                else
                {
                    targetSpeed = walkSpeed;
                    CurrentState = PlayerLocomotionState.Walking;
                }
            }
            else
            {
                CurrentState = isCrouching ? PlayerLocomotionState.Crouching : PlayerLocomotionState.Idle;
            }

            if (!canSprint)
            {
                CurrentStamina = Mathf.MoveTowards(CurrentStamina, maxStamina, 14f * Time.deltaTime);
            }

            Transform cam = Camera.main != null ? Camera.main.transform : transform;
            Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            Vector3 moveDir = (camForward * input.y + camRight * input.x).normalized;

            if (inputMag > 0.05f && moveDir.sqrMagnitude > 0.01f)
            {
                float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                float smoothAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, rotationSmoothTime);
                transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);
            }

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2.5f;
            }
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 finalVelocity = moveDir * targetSpeed + Vector3.up * verticalVelocity;
            controller.Move(finalVelocity * Time.deltaTime);

            // Footstep Audio & Sound Emission
            if (controller.isGrounded && inputMag > 0.1f)
            {
                float stepInterval = CurrentState == PlayerLocomotionState.Sprinting ? 0.32f :
                                     CurrentState == PlayerLocomotionState.Running ? 0.45f : 0.65f;
                footstepTimer += Time.deltaTime;
                if (footstepTimer >= stepInterval)
                {
                    footstepTimer = 0f;
                    float noise = CurrentState == PlayerLocomotionState.Sprinting ? 14f :
                                  CurrentState == PlayerLocomotionState.Running ? 7.5f :
                                  CurrentState == PlayerLocomotionState.Crouching ? 1.2f : 3.5f;
                    float vol = CurrentState == PlayerLocomotionState.Crouching ? 0.18f : 0.45f;
                    if (AudioManager.Instance != null)
                    {
                        AudioManager.Instance.PlayFootstep(transform.position, noise, vol);
                    }
                }
            }
        }

        private bool TryInitiateVaultOrClimb()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, transform.forward, out RaycastHit hit, 1.4f))
            {
                if (hit.collider.isTrigger) return false;
                Bounds b = hit.collider.bounds;
                float obstacleTop = b.max.y;
                float heightDiff = obstacleTop - transform.position.y;

                // Waist-high obstacle -> Vault over
                if (heightDiff >= 0.5f && heightDiff <= 1.5f)
                {
                    isPerformingParkour = true;
                    CurrentState = PlayerLocomotionState.Vaulting;
                    parkourStartPos = transform.position;
                    parkourTargetPos = transform.position + transform.forward * 2.1f;
                    parkourTargetPos.y = transform.position.y;
                    parkourDuration = 0.45f;
                    parkourTimer = 0f;
                    controller.enabled = false;
                    return true;
                }
                // High ledge -> Climb onto top
                if (heightDiff > 1.5f && heightDiff <= 3.3f)
                {
                    isPerformingParkour = true;
                    CurrentState = PlayerLocomotionState.Climbing;
                    parkourStartPos = transform.position;
                    parkourTargetPos = hit.point - hit.normal * 0.6f;
                    parkourTargetPos.y = obstacleTop + 0.05f;
                    parkourDuration = 0.65f;
                    parkourTimer = 0f;
                    controller.enabled = false;
                    return true;
                }
            }
            return false;
        }

        private void UpdateParkourAction()
        {
            parkourTimer += Time.deltaTime;
            float t = Mathf.Clamp01(parkourTimer / parkourDuration);
            Vector3 arcPos = Vector3.Lerp(parkourStartPos, parkourTargetPos, t);
            arcPos.y += Mathf.Sin(t * Mathf.PI) * 0.85f;
            transform.position = arcPos;

            if (t >= 1f)
            {
                isPerformingParkour = false;
                controller.enabled = true;
                CurrentState = PlayerLocomotionState.Idle;
            }
        }

        private void UpdateStealthMetrics()
        {
            if (IsInSafeZone)
            {
                VisibilityFactor = 0f;
                CurrentNoiseRadius = 0f;
                return;
            }

            float baseVis = isCrouching ? 0.45f : (CurrentState == PlayerLocomotionState.Sprinting ? 1.35f : 0.9f);
            if (IsInConcealmentZone)
            {
                baseVis *= isCrouching ? 0.2f : 0.5f;
            }
            VisibilityFactor = Mathf.Clamp(baseVis, 0.08f, 1.5f);

            CurrentNoiseRadius = CurrentState switch
            {
                PlayerLocomotionState.Sprinting => 14f,
                PlayerLocomotionState.Running => 7.5f,
                PlayerLocomotionState.Walking => 3.5f,
                PlayerLocomotionState.Crouching => 1.0f,
                _ => 0f
            };
        }

        private void ScanForInteractables()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.9f, interactionRadius, interactionHits);
            IInteractable closest = null;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (interactionHits[i] == null) continue;
                IInteractable interactable = interactionHits[i].GetComponent<IInteractable>();
                if (interactable != null && interactable.IsInteractable())
                {
                    float dSq = (interactionHits[i].transform.position - transform.position).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        closest = interactable;
                    }
                }
            }
            ActiveInteractable = closest;
        }

        public void TeleportTo(Vector3 worldPosition)
        {
            if (controller != null) controller.enabled = false;
            transform.position = worldPosition;
            verticalVelocity = 0f;
            if (controller != null) controller.enabled = true;
        }
    }

    public class ThirdPersonCameraController : MonoBehaviour
    {
        public static ThirdPersonCameraController Instance { get; private set; }

        [SerializeField] private Transform target;
        [SerializeField] private float distance = 4.2f;
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.45f, 1.55f, 0f);
        [SerializeField] private float minPitch = -30f;
        [SerializeField] private float maxPitch = 65f;
        [SerializeField] private float collisionRadius = 0.22f;

        private float yaw;
        private float pitch = 12f;

        // Cinematic override support
        private bool inCinematicOverride;
        private Vector3 cinematicPos;
        private Vector3 cinematicLookAt;
        private float cinematicTimer;

        private void Awake()
        {
            Instance = this;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null) yaw = target.eulerAngles.y;
        }

        public void TriggerCinematicLook(Vector3 camPosition, Vector3 lookTarget, float duration = 3.0f)
        {
            inCinematicOverride = true;
            cinematicPos = camPosition;
        
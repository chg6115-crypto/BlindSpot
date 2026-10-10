using UnityEngine;
using UnityEngine.InputSystem;

namespace Invector.vCharacterController
{
    public class vThirdPersonInput : MonoBehaviour
    {
        #region Variables       

        [Header("New Input System")]
        [SerializeField, Range(0.01f, 1f)] protected float mouseLookScale = 0.1f;
        [SerializeField, Range(0.1f, 10f)] protected float gamepadLookScale = 2f;
        [SerializeField] protected bool lockCursorOnPlay = true;

        [HideInInspector] public vThirdPersonController cc;
        [HideInInspector] public vThirdPersonCamera tpCamera;
        [HideInInspector] public Camera cameraMain;

        protected InputAction moveAction;
        protected InputAction lookAction;
        protected InputAction jumpAction;
        protected InputAction strafeAction;
        protected InputAction sprintAction;

        #endregion

        protected virtual void Awake()
        {
            CreateInputActions();
        }

        protected virtual void OnEnable()
        {
            CreateInputActions();
            moveAction.Enable();
            lookAction.Enable();
            jumpAction.Enable();
            strafeAction.Enable();
            sprintAction.Enable();
        }

        protected virtual void OnDisable()
        {
            moveAction?.Disable();
            lookAction?.Disable();
            jumpAction?.Disable();
            strafeAction?.Disable();
            sprintAction?.Disable();

            if (cc != null)
                cc.Sprint(false);

            if (Application.isPlaying && lockCursorOnPlay)
                SetCursorLock(false);
        }

        protected virtual void OnDestroy()
        {
            moveAction?.Dispose();
            lookAction?.Dispose();
            jumpAction?.Dispose();
            strafeAction?.Dispose();
            sprintAction?.Dispose();
        }

        protected virtual void Start()
        {
            InitilizeController();
            InitializeTpCamera();

            if (lockCursorOnPlay)
                SetCursorLock(true);
        }

        protected virtual void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isPlaying || !lockCursorOnPlay)
                return;

            SetCursorLock(hasFocus);
        }

        protected virtual void SetCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        protected virtual void FixedUpdate()
        {
            cc.UpdateMotor();               // updates the ThirdPersonMotor methods
            cc.ControlLocomotionType();     // handle the controller locomotion type and movespeed
            cc.ControlRotationType();       // handle the controller rotation type
        }

        protected virtual void Update()
        {
            InputHandle();                  // update the input methods
            cc.UpdateAnimator();            // updates the Animator Parameters
        }

        public virtual void OnAnimatorMove()
        {
            cc.ControlAnimatorRootMotion(); // handle root motion animations 
        }

        #region Basic Locomotion Inputs

        protected virtual void InitilizeController()
        {
            cc = GetComponent<vThirdPersonController>();

            if (cc != null)
                cc.Init();
        }

        protected virtual void InitializeTpCamera()
        {
            if (tpCamera == null)
            {
                tpCamera = FindAnyObjectByType<vThirdPersonCamera>();
                if (tpCamera == null)
                    return;
                if (tpCamera)
                {
                    tpCamera.SetMainTarget(this.transform);
                    tpCamera.Init();
                }
            }
        }

        protected virtual void InputHandle()
        {
            MoveInput();
            CameraInput();
            SprintInput();
            StrafeInput();
            JumpInput();
        }

        public virtual void MoveInput()
        {
            Vector2 move = moveAction.ReadValue<Vector2>();
            cc.input.x = move.x;
            cc.input.z = move.y;
        }

        protected virtual void CameraInput()
        {
            if (!cameraMain)
            {
                if (!Camera.main) Debug.Log("Missing a Camera with the tag MainCamera, please add one.");
                else
                {
                    cameraMain = Camera.main;
                    cc.rotateTarget = cameraMain.transform;
                }
            }

            if (cameraMain)
            {
                cc.UpdateMoveDirection(cameraMain.transform);
            }

            if (tpCamera == null)
                return;

            Vector2 look = lookAction.ReadValue<Vector2>();
            float scale = lookAction.activeControl != null && lookAction.activeControl.device is Gamepad
                ? gamepadLookScale
                : mouseLookScale;

            tpCamera.RotateCamera(look.x * scale, look.y * scale);
        }

        protected virtual void StrafeInput()
        {
            if (strafeAction.WasPressedThisFrame())
                cc.Strafe();
        }

        protected virtual void SprintInput()
        {
            if (sprintAction.WasPressedThisFrame())
                cc.Sprint(true);
            else if (sprintAction.WasReleasedThisFrame())
                cc.Sprint(false);
        }

        /// <summary>
        /// Conditions to trigger the Jump animation & behavior
        /// </summary>
        /// <returns></returns>
        protected virtual bool JumpConditions()
        {
            return cc.isGrounded && cc.GroundAngle() < cc.slopeLimit && !cc.isJumping && !cc.stopMove;
        }

        /// <summary>
        /// Input to trigger the Jump 
        /// </summary>
        protected virtual void JumpInput()
        {
            if (jumpAction.WasPressedThisFrame() && JumpConditions())
                cc.Jump();
        }

        protected virtual void CreateInputActions()
        {
            if (moveAction != null)
                return;

            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");

            lookAction = new InputAction("Look", InputActionType.PassThrough);
            lookAction.AddBinding("<Mouse>/delta");
            lookAction.AddBinding("<Gamepad>/rightStick");

            jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");

            strafeAction = new InputAction("Strafe", InputActionType.Button, "<Keyboard>/tab");
            strafeAction.AddBinding("<Gamepad>/rightShoulder");

            sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            sprintAction.AddBinding("<Gamepad>/leftStickPress");
        }

        #endregion       
    }
}

using System.Collections;
using UnityEngine;

namespace ARGame
{
    /// <summary>
    /// Controla el movimiento por joystick, salto, baile y música del personaje en AR.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(AudioSource))]
    public class ARCharacterController : MonoBehaviour
    {
        private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
        private static readonly int IsDancingHash = Animator.StringToHash("IsDancing");
        private static readonly int JumpHash = Animator.StringToHash("Jump");

        [Header("Movimiento")]
        [SerializeField] private float moveSpeed = 0.6f;
        [SerializeField] private float rotationSpeed = 10f;

        [Header("Salto")]
        [SerializeField] private float jumpHeight = 0.25f;
        [SerializeField] private float jumpDuration = 0.65f;

        [Header("Audio")]
        [SerializeField] private AudioClip danceMusic;

        private Animator animator;
        private AudioSource audioSource;
        private VirtualJoystick joystick;
        private Camera arCamera;

        private bool isDancing;
        private bool isJumping;
        private float baseY;

        public bool IsDancing => isDancing;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            audioSource = GetComponent<AudioSource>();

            // Evita que la animación mueva al personaje fuera del plano AR.
            animator.applyRootMotion = false;

            audioSource.playOnAwake = false;
            audioSource.loop = true;
            if (danceMusic != null)
                audioSource.clip = danceMusic;

            arCamera = Camera.main;
            baseY = transform.position.y;
        }

        public void Initialize(VirtualJoystick virtualJoystick, Camera targetCamera = null)
        {
            joystick = virtualJoystick;
            if (targetCamera != null)
                arCamera = targetCamera;
            else if (arCamera == null)
                arCamera = Camera.main;

            baseY = transform.position.y;
        }

        public void SetGroundHeight(float y)
        {
            baseY = y;
            if (!isJumping)
            {
                Vector3 pos = transform.position;
                pos.y = baseY;
                transform.position = pos;
            }
        }

        private void Update()
        {
            if (isDancing || joystick == null)
            {
                animator.SetBool(IsWalkingHash, false);
                return;
            }

            Vector2 input = joystick.Direction;
            bool hasInput = input.sqrMagnitude > 0.01f;
            animator.SetBool(IsWalkingHash, hasInput);

            if (!hasInput)
                return;

            Vector3 moveDirection = GetCameraRelativeDirection(input);
            if (moveDirection.sqrMagnitude < 0.001f)
                return;

            transform.position += moveDirection * (moveSpeed * Time.deltaTime);

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (arCamera == null)
                arCamera = Camera.main;

            if (arCamera == null)
                return new Vector3(input.x, 0f, input.y).normalized;

            Vector3 camForward = arCamera.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Vector3 camRight = arCamera.transform.right;
            camRight.y = 0f;
            camRight.Normalize();

            Vector3 direction = camForward * input.y + camRight * input.x;
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        public void StartDance()
        {
            if (isDancing)
                return;

            isDancing = true;
            animator.SetBool(IsWalkingHash, false);
            animator.SetBool(IsDancingHash, true);

            if (danceMusic != null && audioSource.clip != danceMusic)
                audioSource.clip = danceMusic;

            if (audioSource.clip != null)
                audioSource.Play();
        }

        public void StopDance()
        {
            if (!isDancing)
                return;

            isDancing = false;
            animator.SetBool(IsDancingHash, false);

            if (audioSource.isPlaying)
                audioSource.Stop();
        }

        public void TriggerJump()
        {
            if (isJumping || isDancing)
                return;

            StartCoroutine(JumpRoutine());
        }

        private IEnumerator JumpRoutine()
        {
            isJumping = true;
            animator.SetTrigger(JumpHash);

            float elapsed = 0f;
            while (elapsed < jumpDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / jumpDuration);
                float heightOffset = 4f * jumpHeight * t * (1f - t);

                Vector3 pos = transform.position;
                pos.y = baseY + heightOffset;
                transform.position = pos;

                yield return null;
            }

            Vector3 finalPos = transform.position;
            finalPos.y = baseY;
            transform.position = finalPos;
            isJumping = false;
        }
    }
}

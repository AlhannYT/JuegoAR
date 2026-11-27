using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ARGame
{
    /// <summary>
    /// Coloca o reposiciona el personaje con anclaje AR (ARAnchor) y retícula de suelo.
    /// </summary>
    public class ARCharacterSpawner : MonoBehaviour
    {
        [Header("Referencias AR")]
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;
        [SerializeField] private Camera arCamera;

        [Header("Personaje y UI")]
        [SerializeField] private ARCharacterController characterPrefab;
        [SerializeField] private ARGameUIManager uiManager;

        [Header("Escala y posición por defecto")]
        [SerializeField] private float characterScale = 0.35f;
        [SerializeField] private float fallbackDistance = 1.5f;
        [SerializeField] private float fallbackHeightOffset = -0.9f;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();

        private ARCharacterController spawnedCharacter;
        private ARAnchor currentAnchor;
        private GameObject floorReticle;
        private Material reticleMaterial;

        private bool isPlacementMode = true;
        private bool snappedToPlane;

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Start()
        {
            if (arCamera == null)
                arCamera = Camera.main;

            if (raycastManager == null)
                raycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (planeManager == null)
                planeManager = FindFirstObjectByType<ARPlaneManager>();

            if (anchorManager == null)
            {
                anchorManager = FindFirstObjectByType<ARAnchorManager>();
                if (anchorManager == null)
                {
                    XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
                    if (xrOrigin != null)
                        anchorManager = xrOrigin.gameObject.AddComponent<ARAnchorManager>();
                }
            }

            if (uiManager != null)
                uiManager.SetSpawner(this);

            CreateFloorReticle();
        }

        private void Update()
        {
            if (spawnedCharacter != null && !snappedToPlane)
                TrySnapCharacterToDetectedFloor();

            if (!isPlacementMode || characterPrefab == null)
            {
                if (floorReticle != null && floorReticle.activeSelf)
                    floorReticle.SetActive(false);
                return;
            }

            UpdateFloorReticle();
            UpdatePlacementFeedback();

            if (raycastManager == null || !TryGetTapPosition(out Vector2 screenPosition))
                return;

            if (IsPointerOverUI(screenPosition))
                return;

            if (raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
            {
                PlaceOrMoveCharacter(hits[0].pose.position, true);
            }
        }

        /// <summary>
        /// Coloca al personaje en el punto del suelo detectado frente a la cámara (o en posición estimada si aún no hay plano).
        /// </summary>
        public void PlaceAtCurrentView()
        {
            if (characterPrefab == null)
            {
                if (uiManager != null)
                    uiManager.UpdateStatusText("Error: falta asignar Character Prefab en ARGameManager");
                return;
            }

            if (arCamera == null)
                arCamera = Camera.main;

            Vector2 centerScreen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (raycastManager != null &&
                (raycastManager.Raycast(centerScreen, hits, TrackableType.PlaneWithinPolygon) ||
                 raycastManager.Raycast(centerScreen, hits, TrackableType.PlaneEstimated)))
            {
                bool hitPlane = (hits[0].hitType & TrackableType.PlaneWithinPolygon) != 0;
                PlaceOrMoveCharacter(hits[0].pose.position, hitPlane);
                return;
            }

            Vector3 fallbackPos = GetFallbackGroundPosition(out bool foundPlaneHeight);
            PlaceOrMoveCharacter(fallbackPos, foundPlaneHeight);
        }

        public void EnableRepositionMode()
        {
            isPlacementMode = true;
            snappedToPlane = false;
        }

        private void CreateFloorReticle()
        {
            floorReticle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floorReticle.name = "ARFloorReticle";
            Destroy(floorReticle.GetComponent<Collider>());
            floorReticle.transform.localScale = new Vector3(0.28f, 0.003f, 0.28f);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            reticleMaterial = new Material(shader);
            reticleMaterial.color = new Color(0.1f, 0.9f, 0.4f, 0.75f);
            floorReticle.GetComponent<MeshRenderer>().sharedMaterial = reticleMaterial;
            floorReticle.SetActive(false);
        }

        private void UpdateFloorReticle()
        {
            if (floorReticle == null || raycastManager == null)
                return;

            Vector2 centerScreen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (raycastManager.Raycast(centerScreen, hits, TrackableType.PlaneWithinPolygon))
            {
                floorReticle.SetActive(true);
                floorReticle.transform.SetPositionAndRotation(hits[0].pose.position, Quaternion.identity);
                if (reticleMaterial != null)
                    reticleMaterial.color = new Color(0.1f, 0.95f, 0.4f, 0.8f);
            }
            else if (raycastManager.Raycast(centerScreen, hits, TrackableType.PlaneEstimated))
            {
                floorReticle.SetActive(true);
                floorReticle.transform.SetPositionAndRotation(hits[0].pose.position, Quaternion.identity);
                if (reticleMaterial != null)
                    reticleMaterial.color = new Color(1f, 0.8f, 0.1f, 0.65f);
            }
            else
            {
                floorReticle.SetActive(false);
            }
        }

        private Vector3 GetFallbackGroundPosition(out bool foundPlaneHeight)
        {
            foundPlaneHeight = false;
            Vector3 camPos = arCamera != null ? arCamera.transform.position : Vector3.zero;
            Vector3 camForward = arCamera != null ? arCamera.transform.forward : Vector3.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude < 0.001f)
                camForward = Vector3.forward;
            camForward.Normalize();

            Vector3 targetPos = camPos + camForward * fallbackDistance;
            targetPos.y = camPos.y + fallbackHeightOffset;

            if (TryGetClosestHorizontalPlaneY(targetPos, out float planeY))
            {
                targetPos.y = planeY;
                foundPlaneHeight = true;
            }

            return targetPos;
        }

        private void TrySnapCharacterToDetectedFloor()
        {
            if (spawnedCharacter == null)
                return;

            if (TryGetClosestHorizontalPlaneY(spawnedCharacter.transform.position, out float planeY))
            {
                spawnedCharacter.SetGroundHeight(planeY);
                AttachAnchor(spawnedCharacter.transform.position, spawnedCharacter.transform.rotation);
                snappedToPlane = true;
            }
        }

        private bool TryGetClosestHorizontalPlaneY(Vector3 referencePos, out float planeY)
        {
            planeY = 0f;
            if (planeManager == null || planeManager.trackables.count == 0)
                return false;

            float bestDistance = float.MaxValue;
            bool found = false;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                float dist = Vector2.Distance(
                    new Vector2(referencePos.x, referencePos.z),
                    new Vector2(plane.center.x, plane.center.z));

                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    planeY = plane.center.y;
                    found = true;
                }
            }

            return found;
        }

        private void UpdatePlacementFeedback()
        {
            if (uiManager == null)
                return;

            if (ARSession.notTrackingReason == NotTrackingReason.InsufficientLight)
            {
                uiManager.UpdateStatusText("Poca luz: ilumina un poco más la habitación");
                return;
            }

            if (ARSession.notTrackingReason == NotTrackingReason.InsufficientFeatures)
            {
                uiManager.UpdateStatusText("Mueve el teléfono despacio sobre una zona con detalles");
                return;
            }

            int planeCount = planeManager != null ? planeManager.trackables.count : 0;
            if (planeCount > 0)
                uiManager.UpdateStatusText("¡Suelo detectado! Apunta el círculo verde y presiona +");
            else
                uiManager.UpdateStatusText("Escaneando suelo... mueve el teléfono despacio o presiona +");
        }

        private bool TryGetTapPosition(out Vector2 screenPosition)
        {
            var activeTouches = Touch.activeTouches;
            for (int i = 0; i < activeTouches.Count; i++)
            {
                if (activeTouches[i].phase == TouchPhase.Began)
                {
                    screenPosition = activeTouches[i].screenPosition;
                    return true;
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            screenPosition = default;
            return false;
        }

        private void PlaceOrMoveCharacter(Vector3 position, bool isOnPlane)
        {
            Quaternion lookRotation = GetFaceCameraRotation(position);

            if (spawnedCharacter == null)
            {
                spawnedCharacter = Instantiate(characterPrefab, position, lookRotation);
                spawnedCharacter.transform.localScale = Vector3.one * characterScale;
            }
            else
            {
                spawnedCharacter.transform.SetParent(null, true);
                spawnedCharacter.transform.SetPositionAndRotation(position, lookRotation);
                spawnedCharacter.SetGroundHeight(position.y);
            }

            if (isOnPlane)
                AttachAnchor(position, lookRotation);

            snappedToPlane = isOnPlane;

            if (uiManager != null)
            {
                spawnedCharacter.Initialize(uiManager.Joystick, arCamera);
                uiManager.SetCharacter(spawnedCharacter);
            }

            if (floorReticle != null)
                floorReticle.SetActive(false);

            isPlacementMode = false;
        }

        private async void AttachAnchor(Vector3 position, Quaternion rotation)
        {
            if (spawnedCharacter == null || anchorManager == null)
                return;

            spawnedCharacter.transform.SetParent(null, true);
            if (currentAnchor != null)
            {
                Destroy(currentAnchor.gameObject);
                currentAnchor = null;
            }

            var result = await anchorManager.TryAddAnchorAsync(new Pose(position, rotation));
            if (result.status.IsSuccess() && result.value != null && spawnedCharacter != null)
            {
                currentAnchor = result.value;
                spawnedCharacter.transform.SetParent(currentAnchor.transform, true);
            }
        }

        private Quaternion GetFaceCameraRotation(Vector3 spawnPosition)
        {
            if (arCamera == null)
                return Quaternion.identity;

            Vector3 directionToCamera = arCamera.transform.position - spawnPosition;
            directionToCamera.y = 0f;

            if (directionToCamera.sqrMagnitude < 0.001f)
                return Quaternion.identity;

            return Quaternion.LookRotation(directionToCamera.normalized, Vector3.up);
        }

        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
                return false;

            PointerEventData eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, uiRaycastResults);
            return uiRaycastResults.Count > 0;
        }
    }
}

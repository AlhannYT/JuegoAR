using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARGame.Editor
{
    public static class ARGameSetupEditor
    {
        private const string ModelsDir = "Assets/ARGame/Models";
        private const string AudioPath = "Assets/ARGame/Audio/Salsa.mp3";
        private const string AnimatorPath = "Assets/ARGame/Animations/CharacterAnimator.controller";
        private const string PrefabPath = "Assets/ARGame/Prefabs/ARCharacter.prefab";
        private const string PlanePrefabPath = "Assets/ARGame/Prefabs/ARVisiblePlane.prefab";
        private const string PlaneMatPath = "Assets/ARGame/Materials/ARPlaneVisibleMat.mat";
        private const string ScenePath = "Assets/Scenes/ARJuegoScene.unity";

        [MenuItem("Tools/JuegoAR/Configurar Proyecto Completo")]
        public static void SetupCompleteProject()
        {
            AssetDatabase.Refresh();

            string idlePath = $"{ModelsDir}/Idle.fbx";
            string walkPath = $"{ModelsDir}/Walking.fbx";
            string dancePath = $"{ModelsDir}/Salsa Dancing.fbx";
            string jumpPath = $"{ModelsDir}/Jump.fbx";

            if (!VerifyRequiredAssets(idlePath, walkPath, dancePath, jumpPath, AudioPath))
                return;

            // 1. Configurar FBX como Humanoid y copiar Avatar desde Idle
            ConfigureModel(idlePath, null, true);
            Avatar baseAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(idlePath);
            if (baseAvatar == null)
            {
                EditorUtility.DisplayDialog("Error", "No se pudo obtener el Avatar Humanoid de Idle.fbx.", "OK");
                return;
            }

            ConfigureModel(walkPath, baseAvatar, true);
            ConfigureModel(dancePath, baseAvatar, true);
            ConfigureModel(jumpPath, baseAvatar, false);

            // 2. Crear Animator Controller
            AnimatorController controller = CreateAnimatorController(idlePath, walkPath, dancePath, jumpPath);

            // 3. Crear Prefab del personaje
            ARCharacterController characterPrefab = CreateCharacterPrefab(idlePath, controller);

            // 4. Crear Escena AR completa con UI y Spawner
            CreateARScene(characterPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "JuegoAR Configurado",
                "Se configuraron las animaciones, el Animator, el Prefab y la escena ARJuegoScene.\n\nYa puedes probarla o compilar para tu teléfono.",
                "¡Listo!"
            );
        }

        private static bool VerifyRequiredAssets(params string[] paths)
        {
            foreach (string path in paths)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) == null)
                {
                    EditorUtility.DisplayDialog(
                        "Falta un archivo",
                        $"No se encontró el archivo requerido:\n{path}\n\nVerifica que esté en la carpeta correcta.",
                        "OK"
                    );
                    return false;
                }
            }
            return true;
        }

        private static void ConfigureModel(string assetPath, Avatar sourceAvatar, bool loopTime)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                return;

            importer.animationType = ModelImporterAnimationType.Human;
            if (sourceAvatar == null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }

            importer.SaveAndReimport();

            // Configurar clips de animación (Loop y Bake Into Pose para que no se desplace por Root Motion)
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loopTime;
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimationClip LoadFirstClip(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static AnimatorController CreateAnimatorController(string idlePath, string walkPath, string dancePath, string jumpPath)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorPath);

            controller.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsDancing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimatorState idleState = sm.AddState("Idle");
            idleState.motion = LoadFirstClip(idlePath);
            sm.defaultState = idleState;

            AnimatorState walkState = sm.AddState("Walking");
            walkState.motion = LoadFirstClip(walkPath);

            AnimatorState danceState = sm.AddState("Dancing");
            danceState.motion = LoadFirstClip(dancePath);

            AnimatorState jumpState = sm.AddState("Jump");
            jumpState.motion = LoadFirstClip(jumpPath);

            // Idle <-> Walking
            AnimatorStateTransition idleToWalk = idleState.AddTransition(walkState);
            idleToWalk.AddCondition(AnimatorConditionMode.If, 0, "IsWalking");
            idleToWalk.hasExitTime = false;
            idleToWalk.duration = 0.15f;

            AnimatorStateTransition walkToIdle = walkState.AddTransition(idleState);
            walkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsWalking");
            walkToIdle.hasExitTime = false;
            walkToIdle.duration = 0.15f;

            // AnyState -> Dancing -> Idle
            AnimatorStateTransition anyToDance = sm.AddAnyStateTransition(danceState);
            anyToDance.AddCondition(AnimatorConditionMode.If, 0, "IsDancing");
            anyToDance.hasExitTime = false;
            anyToDance.canTransitionToSelf = false;
            anyToDance.duration = 0.15f;

            AnimatorStateTransition danceToIdle = danceState.AddTransition(idleState);
            danceToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDancing");
            danceToIdle.hasExitTime = false;
            danceToIdle.duration = 0.15f;

            // AnyState -> Jump -> Idle
            AnimatorStateTransition anyToJump = sm.AddAnyStateTransition(jumpState);
            anyToJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            anyToJump.hasExitTime = false;
            anyToJump.canTransitionToSelf = false;
            anyToJump.duration = 0.1f;

            AnimatorStateTransition jumpToIdle = jumpState.AddTransition(idleState);
            jumpToIdle.hasExitTime = true;
            jumpToIdle.exitTime = 0.9f;
            jumpToIdle.duration = 0.15f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static ARCharacterController CreateCharacterPrefab(string idlePath, AnimatorController controller)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(idlePath);
            GameObject instance = Object.Instantiate(modelAsset);
            instance.name = "ARCharacter";

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            AudioSource audioSource = instance.GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = instance.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;

            AudioClip salsaClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPath);
            audioSource.clip = salsaClip;

            ARCharacterController charCtrl = instance.AddComponent<ARCharacterController>();
            SerializedObject so = new SerializedObject(charCtrl);
            so.FindProperty("danceMusic").objectReferenceValue = salsaClip;
            so.ApplyModifiedPropertiesWithoutUndo();

            CreateGroundShadow(instance.transform);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<ARCharacterController>(PrefabPath);
        }

        private static void CreateARScene(ARCharacterController characterPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Luz direccional
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // AR Session
            GameObject arSessionGo = new GameObject("AR Session");
            arSessionGo.AddComponent<ARSession>();
            arSessionGo.AddComponent<ARInputManager>();

            // XR Origin + Main Camera
            GameObject xrOriginGo = new GameObject("XR Origin (AR Rig)");
            XROrigin xrOrigin = xrOriginGo.AddComponent<XROrigin>();
            ARPlaneManager planeManager = xrOriginGo.AddComponent<ARPlaneManager>();
            ARRaycastManager raycastManager = xrOriginGo.AddComponent<ARRaycastManager>();
            ARAnchorManager anchorManager = xrOriginGo.AddComponent<ARAnchorManager>();

            GameObject planePrefab = CreateVisiblePlanePrefab();
            if (planePrefab != null)
            {
                SerializedObject planeSo = new SerializedObject(planeManager);
                SerializedProperty planePrefabProp = planeSo.FindProperty("m_PlanePrefab");
                if (planePrefabProp != null)
                {
                    planePrefabProp.objectReferenceValue = planePrefab;
                    planeSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOriginGo.transform, false);
            xrOrigin.CameraFloorOffsetObject = cameraOffset;

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(cameraOffset.transform, false);
            Camera arCam = camGo.AddComponent<Camera>();
            arCam.clearFlags = CameraClearFlags.SolidColor;
            arCam.backgroundColor = Color.black;
            arCam.nearClipPlane = 0.1f;
            arCam.farClipPlane = 20f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            TrackedPoseDriver poseDriver = camGo.AddComponent<TrackedPoseDriver>();
            poseDriver.ignoreTrackingState = true;
            InputAction posAction = new InputAction("Position", InputActionType.Value, expectedControlType: "Vector3");
            posAction.AddBinding("<XRHMD>/centerEyePosition");
            posAction.AddBinding("<HandheldARInputDevice>/devicePosition");
            poseDriver.positionInput = new InputActionProperty(posAction);

            InputAction rotAction = new InputAction("Rotation", InputActionType.Value, expectedControlType: "Quaternion");
            rotAction.AddBinding("<XRHMD>/centerEyeRotation");
            rotAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
            poseDriver.rotationInput = new InputActionProperty(rotAction);

            xrOrigin.Camera = arCam;

            // EventSystem con InputSystemUIInputModule
            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            // Canvas UI
            GameObject canvasGo = new GameObject("Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Sprite knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // Texto de estado arriba
            TMP_Text statusText = CreateTMPText("StatusText", canvasGo.transform, "Presiona + para colocar al personaje frente a ti", 40);
            RectTransform statusRt = statusText.rectTransform;
            statusRt.anchorMin = new Vector2(0.08f, 0.85f);
            statusRt.anchorMax = new Vector2(0.92f, 0.95f);
            statusRt.offsetMin = Vector2.zero;
            statusRt.offsetMax = Vector2.zero;

            // Botón + para colocar al personaje en la posición actual
            Button addCharacterBtn = CreateButton("AddCharacterButton", canvasGo.transform, "+", knobSprite, new Color(0.15f, 0.62f, 0.95f, 0.95f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 180f), new Vector2(170f, 170f));
            Image addBtnImg = addCharacterBtn.GetComponent<Image>();
            if (addBtnImg != null)
                addBtnImg.type = Image.Type.Simple;
            TMP_Text addBtnTxt = addCharacterBtn.GetComponentInChildren<TMP_Text>();
            if (addBtnTxt != null)
                addBtnTxt.fontSize = 86;

            // Contenedor de controles
            GameObject controlsGo = new GameObject("ControlsContainer");
            RectTransform controlsRt = controlsGo.AddComponent<RectTransform>();
            controlsRt.SetParent(canvasGo.transform, false);
            controlsRt.anchorMin = Vector2.zero;
            controlsRt.anchorMax = Vector2.one;
            controlsRt.offsetMin = Vector2.zero;
            controlsRt.offsetMax = Vector2.zero;

            // Joystick (abajo izquierda)
            VirtualJoystick joystick = CreateJoystick(controlsGo.transform, knobSprite);

            // Botones (abajo derecha)
            Button danceBtn = CreateButton("DanceButton", controlsGo.transform, "Bailar", uiSprite, new Color(0.18f, 0.65f, 0.35f, 0.95f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-190f, 150f), new Vector2(260f, 110f));

            Button stopDanceBtn = CreateButton("StopDanceButton", controlsGo.transform, "Parar", uiSprite, new Color(0.82f, 0.22f, 0.22f, 0.95f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-190f, 150f), new Vector2(260f, 110f));
            stopDanceBtn.gameObject.SetActive(false);

            Button jumpBtn = CreateButton("JumpButton", controlsGo.transform, "Saltar", uiSprite, new Color(0.2f, 0.5f, 0.85f, 0.95f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-190f, 290f), new Vector2(260f, 110f));

            Button repositionBtn = CreateButton("RepositionButton", controlsGo.transform, "Mover Lugar", uiSprite, new Color(0.25f, 0.25f, 0.25f, 0.85f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-170f, -90f), new Vector2(260f, 90f));

            // Manager
            GameObject managerGo = new GameObject("ARGameManager");
            ARGameUIManager uiManager = managerGo.AddComponent<ARGameUIManager>();
            ARCharacterSpawner spawner = managerGo.AddComponent<ARCharacterSpawner>();

            SerializedObject uiSo = new SerializedObject(uiManager);
            uiSo.FindProperty("joystick").objectReferenceValue = joystick;
            uiSo.FindProperty("danceButton").objectReferenceValue = danceBtn;
            uiSo.FindProperty("stopDanceButton").objectReferenceValue = stopDanceBtn;
            uiSo.FindProperty("jumpButton").objectReferenceValue = jumpBtn;
            uiSo.FindProperty("repositionButton").objectReferenceValue = repositionBtn;
            uiSo.FindProperty("addCharacterButton").objectReferenceValue = addCharacterBtn;
            uiSo.FindProperty("controlsContainer").objectReferenceValue = controlsGo;
            uiSo.FindProperty("statusText").objectReferenceValue = statusText;
            uiSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("raycastManager").objectReferenceValue = raycastManager;
            spawnerSo.FindProperty("planeManager").objectReferenceValue = planeManager;
            spawnerSo.FindProperty("anchorManager").objectReferenceValue = anchorManager;
            spawnerSo.FindProperty("arCamera").objectReferenceValue = arCam;
            spawnerSo.FindProperty("characterPrefab").objectReferenceValue = characterPrefab;
            spawnerSo.FindProperty("uiManager").objectReferenceValue = uiManager;
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);

            // Agregar ARJuegoScene al Build Settings al inicio
            List<EditorBuildSettingsScene> buildScenes = EditorBuildSettings.scenes
                .Where(s => s.path != ScenePath)
                .ToList();
            buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }

        private static void CreateGroundShadow(Transform parent)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            string shadowMatPath = "Assets/ARGame/Materials/GroundShadowMat.mat";
            if (!AssetDatabase.IsValidFolder("Assets/ARGame/Materials"))
                AssetDatabase.CreateFolder("Assets/ARGame", "Materials");

            Material shadowMat = AssetDatabase.LoadAssetAtPath<Material>(shadowMatPath);
            if (shadowMat == null)
            {
                shadowMat = new Material(shader);
                shadowMat.SetFloat("_Surface", 1f);
                shadowMat.SetFloat("_Blend", 0f);
                shadowMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                shadowMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                shadowMat.SetInt("_ZWrite", 0);
                shadowMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                shadowMat.renderQueue = 3000;
                shadowMat.color = new Color(0f, 0f, 0f, 0.35f);
                AssetDatabase.CreateAsset(shadowMat, shadowMatPath);
            }

            GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = "GroundShadow";
            Object.DestroyImmediate(shadow.GetComponent<Collider>());
            shadow.transform.SetParent(parent, false);
            shadow.transform.localPosition = new Vector3(0f, 0.005f, 0f);
            shadow.transform.localScale = new Vector3(0.9f, 0.002f, 0.9f);
            shadow.GetComponent<MeshRenderer>().sharedMaterial = shadowMat;
        }

        private static GameObject CreateVisiblePlanePrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ARGame/Materials"))
                AssetDatabase.CreateFolder("Assets/ARGame", "Materials");

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(PlaneMatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.SetFloat("_Surface", 1f); // Transparent en URP
                mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
                mat.color = new Color(0f, 0.85f, 1f, 0.25f);
                AssetDatabase.CreateAsset(mat, PlaneMatPath);
            }

            GameObject planeGo = new GameObject("ARVisiblePlane");
            planeGo.AddComponent<ARPlane>();
            planeGo.AddComponent<ARPlaneMeshVisualizer>();
            planeGo.AddComponent<MeshFilter>();
            MeshRenderer mr = planeGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            planeGo.AddComponent<MeshCollider>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(planeGo, PlanePrefabPath);
            Object.DestroyImmediate(planeGo);
            return saved;
        }

        private static VirtualJoystick CreateJoystick(Transform parent, Sprite knobSprite)
        {
            GameObject bgGo = new GameObject("VirtualJoystick");
            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.SetParent(parent, false);
            bgRt.anchorMin = new Vector2(0f, 0f);
            bgRt.anchorMax = new Vector2(0f, 0f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = new Vector2(210f, 210f);
            bgRt.sizeDelta = new Vector2(260f, 260f);

            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = knobSprite;
            bgImg.color = new Color(1f, 1f, 1f, 0.28f);

            GameObject handleGo = new GameObject("Handle");
            RectTransform handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.SetParent(bgRt, false);
            handleRt.anchorMin = new Vector2(0.5f, 0.5f);
            handleRt.anchorMax = new Vector2(0.5f, 0.5f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.anchoredPosition = Vector2.zero;
            handleRt.sizeDelta = new Vector2(110f, 110f);

            Image handleImg = handleGo.AddComponent<Image>();
            handleImg.sprite = knobSprite;
            handleImg.color = new Color(1f, 1f, 1f, 0.85f);

            VirtualJoystick joystick = bgGo.AddComponent<VirtualJoystick>();
            SerializedObject so = new SerializedObject(joystick);
            so.FindProperty("background").objectReferenceValue = bgRt;
            so.FindProperty("handle").objectReferenceValue = handleRt;
            so.ApplyModifiedPropertiesWithoutUndo();

            return joystick;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label,
            Sprite sprite,
            Color bgColor,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPos,
            Vector2 size)
        {
            GameObject btnGo = new GameObject(name);
            RectTransform rt = btnGo.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            Image img = btnGo.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            Button btn = btnGo.AddComponent<Button>();

            TMP_Text txt = CreateTMPText("Text", rt, label, 36);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = Vector2.zero;
            txt.rectTransform.offsetMax = Vector2.zero;

            return btn;
        }

        private static TMP_Text CreateTMPText(string name, Transform parent, string text, float fontSize)
        {
            GameObject go = new GameObject(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return tmp;
        }
    }
}

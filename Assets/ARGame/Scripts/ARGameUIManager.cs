using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ARGame
{
    /// <summary>
    /// Gestiona la interfaz táctil (Joystick, Bailar, Parar, Saltar) y su visibilidad.
    /// </summary>
    public class ARGameUIManager : MonoBehaviour
    {
        [Header("Controles")]
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField] private Button danceButton;
        [SerializeField] private Button stopDanceButton;
        [SerializeField] private Button jumpButton;
        [SerializeField] private Button repositionButton;
        [SerializeField] private Button addCharacterButton;

        [Header("Paneles y Mensajes")]
        [SerializeField] private GameObject controlsContainer;
        [SerializeField] private TMP_Text statusText;

        private ARCharacterController currentCharacter;
        private ARCharacterSpawner spawner;

        public VirtualJoystick Joystick => joystick;

        private void Awake()
        {
            if (danceButton != null)
                danceButton.onClick.AddListener(OnDanceClicked);

            if (stopDanceButton != null)
                stopDanceButton.onClick.AddListener(OnStopDanceClicked);

            if (jumpButton != null)
                jumpButton.onClick.AddListener(OnJumpClicked);

            if (repositionButton != null)
                repositionButton.onClick.AddListener(OnRepositionClicked);

            if (addCharacterButton == null)
                EnsureAddCharacterButton();

            if (addCharacterButton != null)
                addCharacterButton.onClick.AddListener(OnAddCharacterClicked);

            SetPlacementMode(true);
        }

        public void SetSpawner(ARCharacterSpawner targetSpawner)
        {
            spawner = targetSpawner;
        }

        public void SetCharacter(ARCharacterController character)
        {
            currentCharacter = character;
            SetPlacementMode(false);
        }

        public void SetPlacementMode(bool isPlacing)
        {
            if (controlsContainer != null)
                controlsContainer.SetActive(!isPlacing);

            if (addCharacterButton != null)
                addCharacterButton.gameObject.SetActive(isPlacing);

            if (statusText != null)
            {
                statusText.gameObject.SetActive(isPlacing);
                if (isPlacing)
                    statusText.text = "Presiona + para colocar al personaje frente a ti";
            }

            if (!isPlacing)
                UpdateDanceButtons(false);
        }

        public void UpdateStatusText(string message)
        {
            if (statusText != null && statusText.gameObject.activeSelf)
                statusText.text = message;
        }

        private void OnDanceClicked()
        {
            if (currentCharacter == null)
                return;

            currentCharacter.StartDance();
            UpdateDanceButtons(true);
        }

        private void OnStopDanceClicked()
        {
            if (currentCharacter == null)
                return;

            currentCharacter.StopDance();
            UpdateDanceButtons(false);
        }

        private void OnJumpClicked()
        {
            if (currentCharacter == null)
                return;

            currentCharacter.TriggerJump();
        }

        private void OnRepositionClicked()
        {
            if (currentCharacter != null)
                currentCharacter.StopDance();

            if (spawner != null)
                spawner.EnableRepositionMode();

            SetPlacementMode(true);
        }

        private void OnAddCharacterClicked()
        {
            if (spawner != null)
                spawner.PlaceAtCurrentView();
        }

        private void EnsureAddCharacterButton()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return;

            GameObject btnGo = new GameObject("AddCharacterButton");
            RectTransform rt = btnGo.AddComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 180f);
            rt.sizeDelta = new Vector2(180f, 180f);

            Image img = btnGo.AddComponent<Image>();
            img.color = new Color(0.15f, 0.62f, 0.95f, 0.95f);

            addCharacterButton = btnGo.AddComponent<Button>();

            GameObject txtGo = new GameObject("Text");
            RectTransform txtRt = txtGo.AddComponent<RectTransform>();
            txtRt.SetParent(rt, false);
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "+";
            tmp.fontSize = 90f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        private void UpdateDanceButtons(bool isDancing)
        {
            if (danceButton != null)
                danceButton.gameObject.SetActive(!isDancing);

            if (stopDanceButton != null)
                stopDanceButton.gameObject.SetActive(isDancing);

            if (jumpButton != null)
                jumpButton.gameObject.SetActive(!isDancing);

            if (joystick != null)
                joystick.gameObject.SetActive(!isDancing);
        }
    }
}

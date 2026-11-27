using UnityEngine;
using UnityEngine.EventSystems;

namespace ARGame
{
    /// <summary>
    /// Joystick virtual táctil/mouse para controlar el movimiento del personaje en AR.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("Referencias UI")]
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;

        [Header("Configuración")]
        [SerializeField] private float handleRange = 1f;
        [SerializeField] private float deadZone = 0.12f;

        private Vector2 input = Vector2.zero;

        public Vector2 Direction => input;
        public float Horizontal => input.x;
        public float Vertical => input.y;

        private void Awake()
        {
            if (background == null)
                background = GetComponent<RectTransform>();
        }

        private void OnDisable()
        {
            ResetJoystick();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (background == null || handle == null)
                return;

            Vector2 radius = background.sizeDelta * 0.5f;
            if (radius.x <= 0f || radius.y <= 0f)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
            {
                Vector2 normalized = new Vector2(localPoint.x / radius.x, localPoint.y / radius.y);
                input = normalized.magnitude > 1f ? normalized.normalized : normalized;

                if (input.magnitude < deadZone)
                    input = Vector2.zero;

                handle.anchoredPosition = input * radius * handleRange;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetJoystick();
        }

        public void ResetJoystick()
        {
            input = Vector2.zero;
            if (handle != null)
                handle.anchoredPosition = Vector2.zero;
        }
    }
}

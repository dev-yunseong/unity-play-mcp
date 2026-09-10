using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp
{
    internal sealed class TargetLookup
    {
        public bool TryGetTarget(int id, out ScannedTarget target)
        {
            if (!TryGetGameObject(id, out var gameObject))
            {
                target = null;
                return false;
            }

            target = ScannedTarget.FromGameObject(gameObject);
            return true;
        }

        /// <summary>
        /// scan 이 보고한 instance id 뒤의 GameObject. 없으면 false.
        /// </summary>
        /// <remarks>
        /// <c>ScannedTarget</c> 은 <c>Button</c> 과 <c>InputField</c> 만 꺼내 준다. 포인터로
        /// 겨누는 쪽은 collider 와 renderer 도 읽어야 하므로 GameObject 자체가 필요하다.
        /// <para>
        /// 파괴된 오브젝트와 한 번도 없던 id 는 여기서 갈라지지 않는다.
        /// <c>Resources.InstanceIDToObject</c> 가 둘 다 null 로 답하고, 그 둘을 가르는 런타임
        /// API 는 없다. 부르는 쪽의 에러 문장이 두 경우를 함께 말해야 한다.
        /// </para>
        /// </remarks>
        public bool TryGetGameObject(int id, out GameObject gameObject)
        {
            var found = Resources.InstanceIDToObject(id);
            gameObject = found as GameObject;
            if (gameObject == null)
            {
                var component = found as Component;
                gameObject = component == null ? null : component.gameObject;
            }

            return gameObject != null;
        }
    }

    internal sealed class ScannedTarget
    {
        private readonly Button button;
        private readonly InputField inputField;
        private readonly TMP_InputField tmpInputField;

        public RectTransform RectTransform { get; }
        public bool CanClick { get { return button != null; } }
        public bool CanEnterText { get { return inputField != null || tmpInputField != null; } }
        public bool IsClickInteractable { get { return IsUsable(button); } }
        public bool IsTextEntryInteractable
        {
            get { return inputField != null ? IsUsable(inputField) : IsUsable(tmpInputField); }
        }

        private ScannedTarget(Button button, InputField inputField, TMP_InputField tmpInputField, RectTransform rectTransform)
        {
            this.button = button;
            this.inputField = inputField;
            this.tmpInputField = tmpInputField;
            RectTransform = rectTransform;
        }

        public static ScannedTarget FromGameObject(GameObject gameObject)
        {
            return new ScannedTarget(
                gameObject.GetComponent<Button>(),
                gameObject.GetComponent<InputField>(),
                gameObject.GetComponent<TMP_InputField>(),
                gameObject.GetComponent<RectTransform>());
        }

        private static bool IsUsable(Selectable selectable)
        {
            return selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable();
        }

        public bool Click()
        {
            if (!IsUsable(button))
            {
                return false;
            }

            button.onClick.Invoke();
            return true;
        }

        public bool EnterText(string value)
        {
            if (!IsTextEntryInteractable)
            {
                return false;
            }

            if (inputField != null)
            {
                inputField.text = value;
                inputField.onValueChanged.Invoke(value);
                inputField.onEndEdit.Invoke(value);
                return true;
            }

            tmpInputField.text = value;
            tmpInputField.onValueChanged.Invoke(value);
            tmpInputField.onEndEdit.Invoke(value);
            return true;
        }
    }
}

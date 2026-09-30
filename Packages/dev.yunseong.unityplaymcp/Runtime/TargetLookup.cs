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
        /// scan 이 보고한 instance id 의 GameObject 를 찾는다.
        /// </summary>
        /// <remarks>
        /// <c>ScannedTarget</c> 은 <c>InputField</c> 만 꺼내 준다. 포인터로
        /// 겨누는 쪽은 collider 와 renderer 도 읽어야 하므로 GameObject 자체가 필요하다.
        /// <para>
        /// <c>Resources.InstanceIDToObject</c> 는 파괴된 오브젝트와 없는 id 를 구분하지 못한다.
        /// 호출하는 쪽의 에러 메시지가 두 경우를 함께 말해야 한다.
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
        private readonly InputField inputField;
        private readonly TMP_InputField tmpInputField;

        public RectTransform RectTransform { get; }
        public bool CanEnterText { get { return inputField != null || tmpInputField != null; } }
        public bool IsTextEntryInteractable
        {
            get { return inputField != null ? IsUsable(inputField) : IsUsable(tmpInputField); }
        }

        private ScannedTarget(InputField inputField, TMP_InputField tmpInputField, RectTransform rectTransform)
        {
            this.inputField = inputField;
            this.tmpInputField = tmpInputField;
            RectTransform = rectTransform;
        }

        public static ScannedTarget FromGameObject(GameObject gameObject)
        {
            return new ScannedTarget(
                gameObject.GetComponent<InputField>(),
                gameObject.GetComponent<TMP_InputField>(),
                gameObject.GetComponent<RectTransform>());
        }

        private static bool IsUsable(Selectable selectable)
        {
            return selectable != null && selectable.isActiveAndEnabled && selectable.IsInteractable();
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

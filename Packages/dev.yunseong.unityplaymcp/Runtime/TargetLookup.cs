using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    internal sealed class TargetLookup
    {
        public bool TryGetTarget(int id, out ScannedTarget target)
        {
            var found = Resources.InstanceIDToObject(id);
            var gameObject = found as GameObject;
            if (gameObject == null)
            {
                var component = found as Component;
                gameObject = component == null ? null : component.gameObject;
            }

            if (gameObject == null)
            {
                target = null;
                return false;
            }

            target = ScannedTarget.FromGameObject(gameObject);
            return true;
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

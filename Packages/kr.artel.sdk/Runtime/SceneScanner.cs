using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Artel
{
    internal sealed class SceneScanner
    {
        private readonly Dictionary<int, ScannedTarget> targetsById = new Dictionary<int, ScannedTarget>();
        private int nextId;

        public SceneSnapshot Scan()
        {
            targetsById.Clear();
            nextId = 1;

            var activeScene = SceneManager.GetActiveScene();
            var sceneId = NextId();
            var children = new List<SceneBlock>();

            foreach (var root in activeScene.GetRootGameObjects())
            {
                if (root == null || !root.activeInHierarchy)
                {
                    continue;
                }

                var child = ScanTransform(root.transform);
                if (child != null)
                {
                    children.Add(child);
                }
            }

            return new SceneSnapshot(
                sceneId,
                string.IsNullOrEmpty(activeScene.name) ? "Unity Scene" : activeScene.name,
                children);
        }

        public bool TryGetTarget(int id, out ScannedTarget target)
        {
            return targetsById.TryGetValue(id, out target);
        }

        private SceneBlock ScanTransform(Transform transform)
        {
            if (transform == null || !transform.gameObject.activeInHierarchy)
            {
                return null;
            }

            var id = NextId();
            var target = ScannedTarget.FromGameObject(transform.gameObject);
            targetsById[id] = target;

            var children = new List<SceneBlock>();
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = ScanTransform(transform.GetChild(i));
                if (child != null)
                {
                    children.Add(child);
                }
            }

            return new SceneBlock(
                id,
                transform.gameObject.name,
                target.CreateComponents(transform.gameObject.name),
                children);
        }

        private int NextId()
        {
            return nextId++;
        }
    }

    internal sealed class ScannedTarget
    {
        private static readonly IReadOnlyList<TrackedState> EmptyStates = new List<TrackedState>();
        private static readonly IReadOnlyList<ActionInvocation> EmptyActions = new List<ActionInvocation>();

        private readonly Button button;
        private readonly InputField inputField;
        private readonly TMP_InputField tmpInputField;
        private readonly Text text;
        private readonly TMP_Text tmpText;

        private ScannedTarget(
            Button button,
            InputField inputField,
            TMP_InputField tmpInputField,
            Text text,
            TMP_Text tmpText)
        {
            this.button = button;
            this.inputField = inputField;
            this.tmpInputField = tmpInputField;
            this.text = text;
            this.tmpText = tmpText;
        }

        public static ScannedTarget FromGameObject(GameObject gameObject)
        {
            return new ScannedTarget(
                gameObject.GetComponent<Button>(),
                gameObject.GetComponent<InputField>(),
                gameObject.GetComponent<TMP_InputField>(),
                gameObject.GetComponent<Text>(),
                gameObject.GetComponent<TMP_Text>());
        }

        public IReadOnlyList<SceneComponent> CreateComponents(string gameObjectName)
        {
            var components = new List<SceneComponent>();

            if (button != null)
            {
                components.Add(CreateComponent("button", gameObjectName, null, null));
            }

            if (inputField != null)
            {
                components.Add(CreateComponent("editText", gameObjectName, inputField.text, GetPlaceholder(inputField)));
            }

            if (tmpInputField != null)
            {
                components.Add(CreateComponent("editText", gameObjectName, tmpInputField.text, GetPlaceholder(tmpInputField)));
            }

            if (text != null)
            {
                components.Add(CreateComponent("text", gameObjectName, text.text, null));
            }

            if (tmpText != null)
            {
                components.Add(CreateComponent("text", gameObjectName, tmpText.text, null));
            }

            return components;
        }

        public bool Click()
        {
            if (button == null)
            {
                return false;
            }

            button.onClick.Invoke();
            return true;
        }

        public bool EnterText(string value)
        {
            if (inputField != null)
            {
                inputField.text = value;
                inputField.onValueChanged.Invoke(value);
                inputField.onEndEdit.Invoke(value);
                return true;
            }

            if (tmpInputField != null)
            {
                tmpInputField.text = value;
                tmpInputField.onValueChanged.Invoke(value);
                tmpInputField.onEndEdit.Invoke(value);
                return true;
            }

            return false;
        }

        private static SceneComponent CreateComponent(string type, string name, string content, string placeholder)
        {
            return new SceneComponent(type, name, content, placeholder, EmptyStates, EmptyActions);
        }

        private static string GetPlaceholder(InputField target)
        {
            return target.placeholder is Text placeholderText ? placeholderText.text : null;
        }

        private static string GetPlaceholder(TMP_InputField target)
        {
            if (target.placeholder is TMP_Text tmpPlaceholder)
            {
                return tmpPlaceholder.text;
            }

            return target.placeholder is Text uiPlaceholder ? uiPlaceholder.text : null;
        }
    }
}

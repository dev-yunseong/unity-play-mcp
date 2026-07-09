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

        public SceneNode Scan()
        {
            targetsById.Clear();
            nextId = 1;

            var activeScene = SceneManager.GetActiveScene();
            var scene = new SceneNode
            {
                id = NextId(),
                type = "scene",
                name = string.IsNullOrEmpty(activeScene.name) ? "Unity Scene" : activeScene.name,
                children = new List<SceneNode>()
            };

            foreach (var root in activeScene.GetRootGameObjects())
            {
                if (root == null || !root.activeInHierarchy)
                {
                    continue;
                }

                var child = ScanTransform(root.transform);
                if (child != null)
                {
                    scene.children.Add(child);
                }
            }

            return scene;
        }

        public bool TryGetTarget(int id, out ScannedTarget target)
        {
            return targetsById.TryGetValue(id, out target);
        }

        private SceneNode ScanTransform(Transform transform)
        {
            if (transform == null || !transform.gameObject.activeInHierarchy)
            {
                return null;
            }

            var target = ScannedTarget.FromGameObject(transform.gameObject);
            var node = CreateNode(transform.gameObject, target);
            targetsById[node.id] = target;

            for (var i = 0; i < transform.childCount; i++)
            {
                var child = ScanTransform(transform.GetChild(i));
                if (child != null)
                {
                    node.children.Add(child);
                }
            }

            return node;
        }

        private SceneNode CreateNode(GameObject gameObject, ScannedTarget target)
        {
            var node = new SceneNode
            {
                id = NextId(),
                type = target.Kind,
                name = gameObject.name,
                children = new List<SceneNode>()
            };

            if (target.Kind == "Text")
            {
                node.content = target.GetText();
            }
            else if (target.Kind == "EditText")
            {
                node.content = target.GetText();
                node.placeholder = target.GetPlaceholder();
            }

            return node;
        }

        private int NextId()
        {
            return nextId++;
        }
    }

    internal sealed class ScannedTarget
    {
        private readonly Button button;
        private readonly InputField inputField;
        private readonly TMP_InputField tmpInputField;
        private readonly Text text;
        private readonly TMP_Text tmpText;

        public string Kind { get; private set; }

        private ScannedTarget(
            Button button,
            InputField inputField,
            TMP_InputField tmpInputField,
            Text text,
            TMP_Text tmpText,
            string kind)
        {
            this.button = button;
            this.inputField = inputField;
            this.tmpInputField = tmpInputField;
            this.text = text;
            this.tmpText = tmpText;
            Kind = kind;
        }

        public static ScannedTarget FromGameObject(GameObject gameObject)
        {
            var button = gameObject.GetComponent<Button>();
            var inputField = gameObject.GetComponent<InputField>();
            var tmpInput = gameObject.GetComponent<TMP_InputField>();
            var text = gameObject.GetComponent<Text>();
            var tmpText = gameObject.GetComponent<TMP_Text>();

            var kind = "block";
            if (button != null)
            {
                kind = "Button";
            }
            else if (inputField != null || tmpInput != null)
            {
                kind = "EditText";
            }
            else if (text != null || tmpText != null)
            {
                kind = "Text";
            }

            return new ScannedTarget(button, inputField, tmpInput, text, tmpText, kind);
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

        public string GetText()
        {
            if (inputField != null)
            {
                return inputField.text;
            }

            if (text != null)
            {
                return text.text;
            }

            if (tmpInputField != null)
            {
                return tmpInputField.text;
            }

            if (tmpText != null)
            {
                return tmpText.text;
            }

            return string.Empty;
        }

        public string GetPlaceholder()
        {
            if (inputField != null && inputField.placeholder is Text placeholderText)
            {
                return placeholderText.text;
            }

            if (tmpInputField != null)
            {
                if (tmpInputField.placeholder is TMP_Text tmpPlaceholder)
                {
                    return tmpPlaceholder.text;
                }

                if (tmpInputField.placeholder is Text uiPlaceholder)
                {
                    return uiPlaceholder.text;
                }
            }

            return string.Empty;
        }
    }
}

using System.Collections.Generic;
using Artel.Domain;
using Artel.Tracking;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Artel
{
    internal sealed class SceneScanner : ISceneSnapshotScanner
    {
        private readonly Dictionary<int, ScannedTarget> targetsById = new Dictionary<int, ScannedTarget>();
        private readonly StateReader stateReader = new StateReader();

        public SceneScanResult Scan()
        {
            targetsById.Clear();

            var actionCommits = new List<ActionBatchCommit>();
            return new SceneScanResult(
                ScanScene(SceneManager.GetActiveScene(), actionCommits),
                actionCommits);
        }

        private SceneSnapshot ScanScene(Scene scene, List<ActionBatchCommit> actionCommits)
        {
            var children = new List<SceneBlock>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null || !root.activeInHierarchy)
                {
                    continue;
                }

                var child = ScanTransform(root.transform, actionCommits);
                if (child != null)
                {
                    children.Add(child);
                }
            }

            return new SceneSnapshot(
                scene.handle,
                string.IsNullOrEmpty(scene.name) ? "Unity Scene" : scene.name,
                children);
        }

        public bool TryGetTarget(int id, out ScannedTarget target)
        {
            return targetsById.TryGetValue(id, out target);
        }

        private SceneBlock ScanTransform(Transform transform, List<ActionBatchCommit> actionCommits)
        {
            if (transform == null || !transform.gameObject.activeInHierarchy)
            {
                return null;
            }

            var id = transform.gameObject.GetInstanceID();
            var target = ScannedTarget.FromGameObject(transform.gameObject);
            targetsById[id] = target;

            var children = new List<SceneBlock>();
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = ScanTransform(transform.GetChild(i), actionCommits);
                if (child != null)
                {
                    children.Add(child);
                }
            }

            return new SceneBlock(
                id,
                transform.gameObject.name,
                target.CreateComponents(transform.gameObject, stateReader, actionCommits),
                children);
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

        public RectTransform RectTransform { get; }
        public bool CanClick { get { return button != null; } }
        public bool CanEnterText { get { return inputField != null || tmpInputField != null; } }

        private ScannedTarget(
            Button button,
            InputField inputField,
            TMP_InputField tmpInputField,
            Text text,
            TMP_Text tmpText,
            RectTransform rectTransform)
        {
            this.button = button;
            this.inputField = inputField;
            this.tmpInputField = tmpInputField;
            this.text = text;
            this.tmpText = tmpText;
            RectTransform = rectTransform;
        }

        public static ScannedTarget FromGameObject(GameObject gameObject)
        {
            return new ScannedTarget(
                gameObject.GetComponent<Button>(),
                gameObject.GetComponent<InputField>(),
                gameObject.GetComponent<TMP_InputField>(),
                gameObject.GetComponent<Text>(),
                gameObject.GetComponent<TMP_Text>(),
                gameObject.GetComponent<RectTransform>());
        }

        public IReadOnlyList<SceneComponent> CreateComponents(
            GameObject gameObject,
            StateReader stateReader,
            List<ActionBatchCommit> actionCommits)
        {
            var components = new List<SceneComponent>();
            var gameObjectName = gameObject.name;

            if (button != null)
            {
                components.Add(new ButtonComponent(gameObjectName, EmptyStates, EmptyActions));
            }

            if (inputField != null)
            {
                components.Add(new EditTextComponent(
                    gameObjectName,
                    inputField.text,
                    GetPlaceholder(inputField),
                    EmptyStates,
                    EmptyActions));
            }

            if (tmpInputField != null)
            {
                components.Add(new EditTextComponent(
                    gameObjectName,
                    tmpInputField.text,
                    GetPlaceholder(tmpInputField),
                    EmptyStates,
                    EmptyActions));
            }

            if (text != null)
            {
                components.Add(new TextComponent(gameObjectName, text.text, EmptyStates, EmptyActions));
            }

            if (tmpText != null)
            {
                components.Add(new TextComponent(gameObjectName, tmpText.text, EmptyStates, EmptyActions));
            }

            foreach (var component in gameObject.GetComponents<Component>())
            {
                var actionSource = component as IArtelActionSource;
                if (actionSource == null && !stateReader.HasTrackedState(component.GetType()))
                {
                    continue;
                }

                var actionSnapshot = actionSource?.ArtelActionBuffer.Snapshot();
                var actions = actionSnapshot?.Actions ?? EmptyActions;
                if (actionSnapshot != null && actionSnapshot.Watermark > 0)
                {
                    actionCommits.Add(new ActionBatchCommit(actionSource.ArtelActionBuffer, actionSnapshot.Watermark));
                }

                components.Add(new TrackedComponent(
                    component.GetType().FullName,
                    component.GetType().Name,
                    stateReader.Read(component),
                    actions));
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

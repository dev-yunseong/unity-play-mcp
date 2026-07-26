using System;
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
            return Scan(SceneScanOptions.Default);
        }

        public SceneScanResult Scan(SceneScanOptions options)
        {
            targetsById.Clear();

            var actionCommits = new List<ActionBatchCommit>();
            return new SceneScanResult(
                ScanScene(SceneManager.GetActiveScene(), options, actionCommits),
                actionCommits);
        }

        private SceneSnapshot ScanScene(
            Scene scene,
            SceneScanOptions options,
            List<ActionBatchCommit> actionCommits)
        {
            var children = new List<SceneBlock>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null || (!options.IncludeInactive && !root.activeInHierarchy))
                {
                    continue;
                }

                var child = ScanTransform(root.transform, options, actionCommits);
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

        private SceneBlock ScanTransform(
            Transform transform,
            SceneScanOptions options,
            List<ActionBatchCommit> actionCommits)
        {
            if (transform == null)
            {
                return null;
            }

            var active = transform.gameObject.activeInHierarchy;
            if (!active && !options.IncludeInactive)
            {
                return null;
            }

            var id = transform.gameObject.GetInstanceID();
            var target = ScannedTarget.FromGameObject(transform.gameObject);
            targetsById[id] = target;

            var children = new List<SceneBlock>();
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = ScanTransform(transform.GetChild(i), options, actionCommits);
                if (child != null)
                {
                    children.Add(child);
                }
            }

            return new SceneBlock(
                id,
                transform.gameObject.name,
                active,
                target.CreateComponents(transform.gameObject, stateReader, options, actionCommits),
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
            SceneScanOptions options,
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
                // A GameObject whose script failed to compile or went missing reports a null
                // component here.
                if (component == null)
                {
                    continue;
                }

                var actionSource = component as IArtelActionSource;
                var readAllFields = options.IncludeAllSerializedFields && IsGameBehaviour(component);
                if (actionSource == null && !readAllFields && !stateReader.HasTrackedState(component.GetType()))
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
                    stateReader.Read(component, readAllFields),
                    actions));
            }

            return components;
        }

        /// <summary>
        /// A MonoBehaviour the game wrote, as opposed to one shipped by Unity or by this SDK.
        /// </summary>
        /// <remarks>
        /// Reading every serialized field of the engine's own behaviours — Image, TMP_Text,
        /// EventTrigger — buries the game's own data under hundreds of layout fields nobody asked
        /// for, so the assembly a component comes from is the line.
        /// </remarks>
        private static bool IsGameBehaviour(Component component)
        {
            if (!(component is MonoBehaviour))
            {
                return false;
            }

            var assembly = component.GetType().Assembly.GetName().Name;
            return assembly != "Artel.Runtime" &&
                   !assembly.StartsWith("Unity", StringComparison.Ordinal) &&
                   !assembly.StartsWith("System", StringComparison.Ordinal) &&
                   !assembly.StartsWith("mscorlib", StringComparison.Ordinal);
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

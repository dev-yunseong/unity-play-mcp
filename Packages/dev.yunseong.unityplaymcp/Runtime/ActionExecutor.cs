using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityPlayMcp.Affordances.Scan;
using UnityPlayMcp.Capture;
using UnityPlayMcp.Protocol.Dto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp
{
    internal sealed class ActionExecutor
    {
        private const float ResetSettleSeconds = 0.1f;
        private readonly TargetLookup targetLookup;
        private readonly CursorController cursorController;
        private readonly PointerEventDispatcher pointerEvents;
        private readonly IScreenCapturer capturer;

        /// <summary>
        /// 세션 요청에 따라 live reading 을 켜고 끈다.
        /// </summary>
        /// <remarks>
        /// 테스트에서는 null 일 수 있으므로 사용 전에 확인한다.
        /// </remarks>
        private readonly IReadingChannel readings;

        private readonly Action<Vector2> cursorMoved;
        private readonly Action<Vector2> pointerMoved;

        // The time scale before pause_time, restored by resume_time. Null means not paused.
        private float? scaleBeforePause;

        // The scene the run began in, read at construction; reset_game reloads it.
        private readonly int startupSceneBuildIndex;
        private readonly string startupScenePath;

        public ActionExecutor(
            TargetLookup targetLookup,
            CursorController cursorController,
            PointerEventDispatcher pointerEvents,
            IScreenCapturer capturer = null,
            IReadingChannel readings = null)
        {
            this.readings = readings;
            this.targetLookup = targetLookup;
            this.cursorController = cursorController;
            this.pointerEvents = pointerEvents;
            this.capturer = capturer;

            var startupScene = SceneManager.GetActiveScene();
            startupSceneBuildIndex = startupScene.buildIndex;
            startupScenePath = startupScene.path;

            // enter_text moves the pointer without hover events so existing callers see no change.
            // Only move_mouse sends pointer events.
            cursorMoved = VirtualInput.MoveMouse;
            pointerMoved = position =>
            {
                VirtualInput.MoveMouse(position);
                pointerEvents.MoveTo(position);
            };
        }

        public IEnumerator Execute(
            int actionId,
            string method,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            switch (method)
            {
                case "enter_text":
                    yield return ExecuteEnterText(actionId, parameters, completed);
                    yield break;

                case "move_mouse":
                    yield return ExecuteMoveMouse(actionId, parameters, completed);
                    yield break;

                case "mouse_down":
                    yield return ExecuteMouseButton(actionId, method, parameters, true, completed);
                    yield break;

                case "mouse_up":
                    yield return ExecuteMouseButton(actionId, method, parameters, false, completed);
                    yield break;

                case "key_click":
                    yield return ExecuteKeyClick(actionId, parameters, completed);
                    yield break;

                case "key_down":
                    completed(ExecuteKeyHold(actionId, method, parameters, true));
                    yield break;

                case "key_up":
                    completed(ExecuteKeyHold(actionId, method, parameters, false));
                    yield break;

                case "set_axis":
                    completed(ExecuteSetAxis(actionId, parameters));
                    yield break;

                case "set_button":
                    completed(ExecuteSetButton(actionId, parameters));
                    yield break;

                case "pause_time":
                    completed(ExecutePauseTime(actionId));
                    yield break;

                case "resume_time":
                    completed(ExecuteResumeTime(actionId));
                    yield break;

                case "reset_game":
                    yield return ExecuteResetGame(actionId, parameters, completed);
                    yield break;

                case "start_readings":
                    completed(ExecuteStartReadings(actionId));
                    yield break;

                case "stop_readings":
                    completed(ExecuteStopReadings(actionId));
                    yield break;

                case "capture_screen":
                    yield return ExecuteCaptureScreen(actionId, parameters, completed);
                    yield break;

            }

            completed(ActionResultDto.Failure(actionId, "Unsupported method: " + method));
        }

        private IEnumerator ExecuteEnterText(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (!TryReadId(parameters, 0, out var targetId) || parameters.Count < 2)
            {
                completed(ActionResultDto.Failure(actionId, "enter_text requires params [targetId, value]."));
                yield break;
            }

            if (!targetLookup.TryGetTarget(targetId, out var target))
            {
                completed(ActionResultDto.Failure(actionId, "Unknown target id: " + targetId));
                yield break;
            }

            if (!target.CanEnterText)
            {
                completed(ActionResultDto.Failure(actionId, "Target is not an EditText: " + targetId));
                yield break;
            }

            if (!target.IsTextEntryInteractable)
            {
                completed(ActionResultDto.Failure(actionId, NotInteractable(targetId)));
                yield break;
            }

            yield return cursorController.MoveTo(target.RectTransform, cursorMoved);
            var value = parameters[1] == null ? string.Empty : parameters[1].ToString();
            completed(target.EnterText(value)
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(actionId, NotInteractable(targetId)));
        }

        /// <summary>
        /// Moves the pointer to a screen position, reporting every step so a held button produces a drag.
        /// </summary>
        /// <remarks>
        /// Takes scan coordinates (pixels from the top left) and flips them to Unity's bottom-left space here.
        /// </remarks>
        private IEnumerator ExecuteMoveMouse(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadNumber(parameters[0], out var x) ||
                !TryReadNumber(parameters[1], out var y))
            {
                completed(ActionResultDto.Failure(actionId, "move_mouse requires params [x, y]."));
                yield break;
            }

            yield return cursorController.MoveTo(
                new Vector2(x, Screen.height - y), pointerMoved, glide: true);
            completed(ActionResultDto.Success(actionId));
        }

        /// <summary>
        /// 버튼을 누르거나 놓고, 한 프레임을 넘긴 뒤에 완료를 알린다.
        /// </summary>
        /// <remarks>
        /// 프레임을 넘기는 이유는 <c>click</c> 이 <c>move_mouse</c>, <c>mouse_down</c>, <c>mouse_up</c> 을
        /// 한 batch 로 잇기 때문이다. <c>VirtualMouseState.Press</c> 는 눌린 프레임의 <b>다음</b>
        /// 프레임부터 눌린 것으로 답하므로, 누름 직후 같은 프레임에 놓으면
        /// <see cref="VirtualMouseMessenger"/> 는 눌린 적이 없는 것으로 보고 <c>OnMouseDown</c> 이 빠진다.
        /// 놓은 뒤에도 넘기는 것은 <c>OnMouseUp</c>/<c>OnMouseUpAsButton</c> 이 그 프레임에 배달되고,
        /// 뒤따르는 action 이 그것을 앞지르지 않게 하려는 것이다.
        /// </remarks>
        private IEnumerator ExecuteMouseButton(
            int actionId,
            string method,
            List<object> parameters,
            bool press,
            Action<ActionResultDto> completed)
        {
            if (!TryReadMouseButton(parameters, out var button))
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    method + " requires params [] or [button], where button is 0, 1, or 2."));
                yield break;
            }

            SetButton(button, press);
            yield return null;
            completed(ActionResultDto.Success(actionId));
        }

        /// <summary>
        /// 가상 마우스 상태와 uGUI 이벤트를 함께 갱신한다.
        /// </summary>
        /// <remarks>
        /// <c>mouse_down</c> 과 <c>KeyCode.Mouse0</c> 의 <c>key_down</c> 이 같은 버튼을 가리킨다. 한쪽만 갱신되는
        /// 상태를 막기 위해 버튼 입력은 여기서만 처리한다.
        /// </remarks>
        private void SetButton(int button, bool press)
        {
            if (press)
            {
                VirtualInput.PressMouseButton(button);
                pointerEvents.Press(button);
            }
            else
            {
                VirtualInput.ReleaseMouseButton(button);
                pointerEvents.Release(button);
            }
        }

        private ActionResultDto ExecuteKeyHold(
            int actionId, string method, List<object> parameters, bool press)
        {
            if (parameters == null || parameters.Count == 0 ||
                !TryReadKeyCode(parameters[0], out var key))
            {
                return ActionResultDto.Failure(actionId, method + " requires params [keyCode].");
            }

            // KeyCode.Mouse0 을 가상 키보드에만 넣으면 GetKey 폴링에만 보이고 OnMouseDown 과 uGUI handler 는 부르지 않는다.
            if (MouseButtonKeyCode.TryGetButton(key, out var button))
            {
                SetButton(button, press);
                return ActionResultDto.Success(actionId);
            }

            if (press)
            {
                VirtualInput.PressKey(key);
            }
            else
            {
                VirtualInput.ReleaseKey(key);
            }

            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Freezes game time, leaving the SDK itself running.
        /// </summary>
        /// <remarks>
        /// SDK waits use unscaled time, so a frozen game can still be scanned, clicked and typed into.
        /// </remarks>
        private ActionResultDto ExecutePauseTime(int actionId)
        {
            // Only the first pause records the scale; a second would record 0 and resume to a frozen game.
            if (!scaleBeforePause.HasValue)
            {
                scaleBeforePause = Time.timeScale;
            }

            Time.timeScale = 0f;
            return ActionResultDto.Success(actionId);
        }

        private ActionResultDto ExecuteResumeTime(int actionId)
        {
            if (!scaleBeforePause.HasValue)
            {
                // Restoring an unsaved scale would overwrite the game's own time scale, so fail instead.
                return ActionResultDto.Failure(
                    actionId, "resume_time: game time was not paused by pause_time.");
            }

            Time.timeScale = scaleBeforePause.Value;
            scaleBeforePause = null;
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Undoes a pause when the manager shuts down, so the game is not left frozen.
        /// </summary>
        public void RestoreTimeScale()
        {
            if (scaleBeforePause.HasValue)
            {
                Time.timeScale = scaleBeforePause.Value;
                scaleBeforePause = null;
            }
        }

        /// <summary>
        /// 시작 씬을 다시 열어 게임을 되돌린다. 요청하면 <c>PlayerPrefs</c> 도 비운다.
        /// </summary>
        /// <remarks>
        /// 게임의 <c>DontDestroyOnLoad</c> 오브젝트도 함께 제거해, 다시 열린 씬이 manager 를 새로 만들게 한다.
        ///
        /// <c>PlayerPrefs</c> 는 <c>clearPlayerPrefs</c> 일 때만 비우고, SDK 의 theme 키는 보존한다.
        /// static field 와 디스크 파일은 초기화하지 않는다.
        ///
        /// 저장소를 비운다는 것까지만 보장한다. reload 로 파괴되는 manager 가 <c>OnDestroy</c> 에서 키를 다시 쓸 수 있다.
        /// </remarks>
        private IEnumerator ExecuteResetGame(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            // params 를 Build Settings 확인보다 먼저 읽어야 잘못된 호출이 Build Settings 오류로 보고되지 않는다.
            if (!ResetRequestReader.TryRead(parameters, out var request, out var error))
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            if (startupSceneBuildIndex < 0)
            {
                // Loading by path fails the same way; the scene must be in Build Settings.
                completed(ActionResultDto.Failure(
                    actionId,
                    "reset_game: the scene the game started in is not in Build Settings: " +
                    startupScenePath));
                yield break;
            }

            // Pause and held buttons belong to the run; carried across the reload they would leave the new
            // scene frozen or with a press it never saw begin.
            RestoreTimeScale();
            pointerEvents.ReleaseAll();
            VirtualInput.ReleaseAllVirtualInput();

            // 게임은 시작 씬의 Awake/Start 에서 세이브를 읽으므로 로드 전에 지운다. 사이에 yield 를 두지 않는다.
            if (request.ClearPlayerPrefs)
            {
                OwnedPlayerPrefs.DeleteAllExceptOwn();
            }

            DoomPersistentObjects();
            yield return SceneManager.LoadSceneAsync(startupSceneBuildIndex, LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(ResetSettleSeconds);

            completed(ActionResultDto.Success(actionId));
        }

        /// <summary>
        /// 세션 요청에 따라 live reading 을 켠다.
        /// </summary>
        /// <remarks>
        /// 연결 시점에는 모든 씬을 도는 순회도 시작하므로, reading 시작은 연결과 분리해 세션이 정한다.
        /// </remarks>
        private ActionResultDto ExecuteStartReadings(int actionId)
        {
            if (readings == null)
            {
                return ActionResultDto.Failure(actionId, "This build cannot take live readings.");
            }

            return readings.StartReadings()
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(
                    actionId, "Live readings could not start. A release build does not take them.");
        }

        /// <summary>reading 을 끈다. 돌고 있지 않아도 성공한다.</summary>
        private ActionResultDto ExecuteStopReadings(int actionId)
        {
            if (readings == null)
            {
                return ActionResultDto.Failure(actionId, "This build cannot take live readings.");
            }

            readings.StopReadings();
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Moves the game's <c>DontDestroyOnLoad</c> objects into the scene about to be unloaded, so the
        /// reload destroys them.
        /// </summary>
        /// <remarks>
        /// <c>Destroy</c> takes effect at frame end, after the new scene's <c>Awake</c> has already looked for
        /// existing managers. The SDK root stays because it runs this coroutine and owns the socket.
        /// </remarks>
        private static void DoomPersistentObjects()
        {
            var doomed = SceneManager.GetActiveScene();
            var dropped = new List<string>();
            foreach (var root in StraySpawnTracker.DontDestroyOnLoadScene().GetRootGameObjects())
            {
                if (root.GetComponentInChildren<UnityPlayMcpHost>(true) != null)
                {
                    continue;
                }

                SceneManager.MoveGameObjectToScene(root, doomed);
                dropped.Add(root.name);
            }

            if (dropped.Count > 0)
            {
                // Logged because a game whose bootstrap lives outside the start scene loses these for good.
                Debug.Log("[Unity Play MCP] reset_game dropped persistent object(s): " +
                          string.Join(", ", dropped));
            }
        }

        /// <summary>
        /// Captures the screen, or one element's area of it, and returns its encoded bytes.
        /// </summary>
        /// <remarks>
        /// Runs in the same batch as the preceding actions, so it sees their result.
        /// </remarks>
        private IEnumerator ExecuteCaptureScreen(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (capturer == null)
            {
                completed(ActionResultDto.Failure(
                    actionId, "This build cannot capture the screen."));
                yield break;
            }

            if (!CaptureRequestReader.TryRead(parameters, out var request, out var paramsError))
            {
                completed(ActionResultDto.Failure(actionId, paramsError));
                yield break;
            }

            Rect? pixelRect = null;
            Rect? requestedRect = null;
            var clipped = false;
            if (!request.IsFullScreen)
            {
                var targetId = request.TargetId.Value;
                if (!targetLookup.TryGetTarget(targetId, out var target))
                {
                    completed(ActionResultDto.Failure(actionId, "Unknown target id: " + targetId));
                    yield break;
                }

                var screen = new Rect(0f, 0f, Mathf.Max(2, Screen.width), Mathf.Max(2, Screen.height));
                if (!CaptureRect.TryResolve(target.RectTransform, request.Padding, screen, out var region))
                {
                    completed(ActionResultDto.Failure(
                        actionId, "Target is entirely off screen: " + targetId));
                    yield break;
                }

                pixelRect = region.PixelRect;
                requestedRect = region.Requested;
                clipped = region.Clipped;
            }

            var image = default(CapturedImage);
            yield return capturer.Capture(request, pixelRect, captured => image = captured);
            if (!image.IsSuccess)
            {
                completed(ActionResultDto.Failure(actionId, image.Error));
                yield break;
            }

            completed(ActionResultDto.Success(actionId, new CaptureResultDto
            {
                MimeType = request.ContentType,
                Width = image.Width,
                Height = image.Height,
                TargetId = request.TargetId,
                Clipped = clipped,
                Screen = new CaptureScreenSizeDto { Width = image.ScreenWidth, Height = image.ScreenHeight },
                Region = CaptureRect.TopLeft(image.Source, image.ScreenHeight),
                RequestedRegion = clipped && requestedRect.HasValue
                    ? CaptureRect.TopLeft(requestedRect.Value, image.ScreenHeight)
                    : null,
                Scale = CaptureRect.Scale(image.Source, image.Width, image.Height),
                Frame = image.Frame,
                Scene = image.Scene,
                Data = Convert.ToBase64String(image.Bytes)
            }));
        }

        private static string NotInteractable(int targetId)
        {
            return "Target is not interactable: " + targetId;
        }

        /// <summary>
        /// 키를 지정 시간만큼 눌렀다 놓는다. 마우스 버튼은 여기서 기다렸다 직접 놓는다.
        /// </summary>
        /// <remarks>
        /// 가상 키보드는 만료를 스스로 처리하지만 가상 마우스는 그렇지 않아 <c>pointerUp</c> 과 <c>OnMouseUp</c>
        /// 이 빠진다. 그래서 마우스만 coroutine 으로 처리한다.
        ///
        /// <c>pause_time</c> 중에도 끝나도록 unscaled time 으로 기다린다.
        ///
        /// 기다리는 중 연결이 끊기면 <c>ReleaseAllVirtualInput</c> 과 <c>pointerEvents.ReleaseAll</c> 이 이미
        /// 버튼을 놓고, 뒤늦은 놓음은 아무 일도 하지 않는다.
        /// </remarks>
        private IEnumerator ExecuteKeyClick(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadKeyCode(parameters[0], out var key) ||
                !TryReadDuration(parameters[1], out var durationSeconds))
            {
                completed(ActionResultDto.Failure(
                    actionId,
                    "key_click requires params [keyCode, positiveDurationSeconds]."));
                yield break;
            }

            if (!MouseButtonKeyCode.TryGetButton(key, out var button))
            {
                VirtualInput.ClickKey(key, durationSeconds);
                completed(ActionResultDto.Success(actionId));
                yield break;
            }

            SetButton(button, true);
            yield return new WaitForSecondsRealtime(durationSeconds);
            SetButton(button, false);
            completed(ActionResultDto.Success(actionId));
        }

        /// <summary>
        /// Drives an Input Manager axis by name. The legacy Input Manager has no runtime API for axis key
        /// bindings, so the caller names the axis and value instead of pressing keys.
        /// </summary>
        private static ActionResultDto ExecuteSetAxis(int actionId, List<object> parameters)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadAxisName(parameters[0], out var axisName) ||
                !TryReadAxisValue(parameters[1], out var value))
            {
                return ActionResultDto.Failure(
                    actionId, "set_axis requires params [axisName, valueBetweenMinusOneAndOne].");
            }

            if (!TryConfirmAxisExists(axisName, out var error))
            {
                return ActionResultDto.Failure(actionId, error);
            }

            VirtualInput.SetAxis(axisName, value);
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Writes the button's axis. Releasing hands the axis back to real input instead of pinning it
        /// at zero, so <c>GetButtonUp</c> reports the edge.
        /// </summary>
        private static ActionResultDto ExecuteSetButton(int actionId, List<object> parameters)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadAxisName(parameters[0], out var axisName) ||
                !TryReadFlag(parameters[1], out var pressed))
            {
                return ActionResultDto.Failure(
                    actionId, "set_button requires params [axisName, pressed].");
            }

            if (!TryConfirmAxisExists(axisName, out var error))
            {
                return ActionResultDto.Failure(actionId, error);
            }

            if (pressed)
            {
                VirtualInput.SetAxis(axisName, 1f);
            }
            else
            {
                VirtualInput.ReleaseAxis(axisName);
            }

            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// The engine throws for an undefined axis, which is the only runtime way to validate a name.
        /// Without this a misspelled axis would report success and do nothing.
        /// </summary>
        /// <remarks>
        /// Reads the real <see cref="UnityEngine.Input"/>, not the proxy, which answers from a held value.
        /// </remarks>
        private static bool TryConfirmAxisExists(string axisName, out string error)
        {
            try
            {
                UnityEngine.Input.GetAxis(axisName);
                error = null;
                return true;
            }
            catch (ArgumentException)
            {
                error = "No input axis named '" + axisName + "' is set up in the Input Manager.";
                return false;
            }
        }

        private static bool TryReadAxisName(object value, out string axisName)
        {
            axisName = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            return !string.IsNullOrEmpty(axisName);
        }

        /// <summary>
        /// Out-of-range values fail instead of being clamped, since they indicate a misread axis.
        /// </summary>
        private static bool TryReadAxisValue(object value, out float axisValue)
        {
            return TryReadNumber(value, out axisValue) && axisValue >= -1f && axisValue <= 1f;
        }

        private static bool TryReadFlag(object value, out bool flag)
        {
            if (value is bool booleanValue)
            {
                flag = booleanValue;
                return true;
            }

            flag = false;
            return value != null && bool.TryParse(value.ToString(), out flag);
        }

        private static bool TryReadKeyCode(object value, out KeyCode key)
        {
            key = KeyCode.None;
            if (value == null)
            {
                return false;
            }

            if (value is long longValue)
            {
                if (longValue < int.MinValue || longValue > int.MaxValue)
                {
                    return false;
                }

                return TryReadKeyCode((int)longValue, out key);
            }

            if (value is int intValue)
            {
                if (!Enum.IsDefined(typeof(KeyCode), intValue))
                {
                    return false;
                }

                key = (KeyCode)intValue;
                return key != KeyCode.None;
            }

            return Enum.TryParse(value.ToString(), true, out key) &&
                   key != KeyCode.None &&
                   Enum.IsDefined(typeof(KeyCode), key);
        }

        /// <summary>
        /// An omitted button means the left one, so the common case reads as <c>"params": []</c>.
        /// </summary>
        private static bool TryReadMouseButton(List<object> parameters, out int button)
        {
            button = 0;
            if (parameters == null || parameters.Count == 0)
            {
                return true;
            }

            return TryReadId(parameters, 0, out button) && VirtualMouseState.IsButton(button);
        }

        private static bool TryReadDuration(object value, out float durationSeconds)
        {
            return TryReadNumber(value, out durationSeconds) && durationSeconds > 0f;
        }

        private static bool TryReadNumber(object value, out float number)
        {
            number = 0f;
            if (value == null ||
                !float.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return false;
            }

            return !float.IsInfinity(number) && !float.IsNaN(number);
        }

        /// <summary>
        /// 위치 인자 하나를 정수 id 로 읽는다.
        /// </summary>
        /// <remarks>
        /// wire 는 위치 인자라 "몇 번째 자리를 어떻게 정수로 읽는가" 가 계약의 일부이고, 그 계약이 두
        /// 벌이면 한쪽만 <c>long</c> 을 받는 식으로 갈라진다.
        /// </remarks>
        private static bool TryReadId(List<object> parameters, int index, out int id)
        {
            id = 0;
            if (parameters == null || index < 0 || parameters.Count <= index ||
                parameters[index] == null)
            {
                return false;
            }

            if (parameters[index] is long longValue)
            {
                id = (int)longValue;
                return true;
            }

            if (parameters[index] is int intValue)
            {
                id = intValue;
                return true;
            }

            return int.TryParse(parameters[index].ToString(), out id);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Artel.Capture;
using Artel.Protocol.Dto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel
{
    internal sealed class ActionExecutor
    {
        private readonly SceneScanner scanner;
        private readonly CursorController cursorController;
        private readonly PointerEventDispatcher pointerEvents;
        private readonly IScreenCapturer capturer;

        /// <summary>
        /// 세션이 그렇게 말할 때 라이브 판독을 켜고 끄는 것.
        /// </summary>
        /// <remarks>
        /// 매니저가 아니라 이음매로 쥔다. 이 클래스가 하는 다른 모든 일은 게임에 대고 하는 것이고 이것은 SDK 에 대고 하는 유일한
        /// 것이기 때문이다. 이것 없이 executor 를 만드는 테스트에서는 null 이고, 아래의 모든 사용이 먼저 묻는 이유가 그것이다.
        /// </remarks>
        private readonly IReadingChannel readings;
        private readonly ICaptureUploader uploader;
        private readonly Action<Vector2> cursorMoved;
        private readonly Action<Vector2> pointerMoved;

        // The time scale as it was when pause_time froze the game, so resume_time gives back the
        // speed the game was actually running at rather than assuming 1. Null means not paused.
        private float? scaleBeforePause;

        // The scene the run began in, read while it is still the one on screen. reset_game reloads
        // it, so "initial" means where this session started rather than a build index guessed later.
        private readonly int startupSceneBuildIndex;
        private readonly string startupScenePath;

        public ActionExecutor(
            SceneScanner scanner,
            CursorController cursorController,
            PointerEventDispatcher pointerEvents,
            IScreenCapturer capturer = null,
            ICaptureUploader uploader = null,
            IReadingChannel readings = null)
        {
            this.readings = readings;
            this.scanner = scanner;
            this.cursorController = cursorController;
            this.pointerEvents = pointerEvents;
            this.capturer = capturer;
            this.uploader = uploader;

            var startupScene = SceneManager.GetActiveScene();
            startupSceneBuildIndex = startupScene.buildIndex;
            startupScenePath = startupScene.path;

            // Moving onto a target keeps the reported pointer under the drawn cursor, but stays a
            // silent move: firing hover events out of button_click would change what an existing
            // caller does to the game. Only move_mouse claims the pointer outright.
            cursorMoved = ArtelInput.MoveMouse;
            pointerMoved = position =>
            {
                ArtelInput.MoveMouse(position);
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
                case "button_click":
                    yield return ExecuteButtonClick(actionId, parameters, completed);
                    yield break;

                case "enter_text":
                    yield return ExecuteEnterText(actionId, parameters, completed);
                    yield break;

                case "move_mouse":
                    yield return ExecuteMoveMouse(actionId, parameters, completed);
                    yield break;

                case "mouse_down":
                    completed(ExecuteMouseButton(actionId, method, parameters, true));
                    yield break;

                case "mouse_up":
                    completed(ExecuteMouseButton(actionId, method, parameters, false));
                    yield break;

                case "key_click":
                    completed(ExecuteKeyClick(actionId, parameters));
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
                    yield return ExecuteResetGame(actionId, completed);
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

        private IEnumerator ExecuteButtonClick(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (!TryReadId(parameters, out var targetId))
            {
                completed(ActionResultDto.Failure(actionId, "button_click requires params [targetId]."));
                yield break;
            }

            if (!scanner.TryGetTarget(targetId, out var target))
            {
                completed(ActionResultDto.Failure(actionId, "Unknown target id: " + targetId));
                yield break;
            }

            if (!target.CanClick)
            {
                completed(ActionResultDto.Failure(actionId, "Target is not a Button: " + targetId));
                yield break;
            }

            if (!target.IsClickInteractable)
            {
                completed(ActionResultDto.Failure(actionId, NotInteractable(targetId)));
                yield break;
            }

            yield return cursorController.MoveTo(target.RectTransform, cursorMoved);

            // The target was a live Button before the cursor moved, so a refusal now means the game
            // locked or tore it down while the cursor was on its way.
            completed(target.Click()
                ? ActionResultDto.Success(actionId)
                : ActionResultDto.Failure(actionId, NotInteractable(targetId)));
        }

        private IEnumerator ExecuteEnterText(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (!TryReadId(parameters, out var targetId) || parameters.Count < 2)
            {
                completed(ActionResultDto.Failure(actionId, "enter_text requires params [targetId, value]."));
                yield break;
            }

            if (!scanner.TryGetTarget(targetId, out var target))
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
        /// Walks the pointer to a screen position, reporting every step on the way. A held button
        /// turns those steps into a drag, which is why this cannot simply jump to the destination.
        /// </summary>
        /// <remarks>
        /// The coordinates are the ones a scan reports: pixels from the top left. Unity's screen
        /// space counts up from the bottom instead, and that flip lives here — once, out of sight —
        /// rather than in every caller that read a block's rect and wants to aim at it.
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

        private ActionResultDto ExecuteMouseButton(
            int actionId, string method, List<object> parameters, bool press)
        {
            if (!TryReadMouseButton(parameters, out var button))
            {
                return ActionResultDto.Failure(
                    actionId,
                    method + " requires params [] or [button], where button is 0, 1, or 2.");
            }

            if (press)
            {
                ArtelInput.PressMouseButton(button);
                pointerEvents.Press(button);
            }
            else
            {
                ArtelInput.ReleaseMouseButton(button);
                pointerEvents.Release(button);
            }

            return ActionResultDto.Success(actionId);
        }

        private static ActionResultDto ExecuteKeyHold(
            int actionId, string method, List<object> parameters, bool press)
        {
            if (parameters == null || parameters.Count == 0 ||
                !TryReadKeyCode(parameters[0], out var key))
            {
                return ActionResultDto.Failure(actionId, method + " requires params [keyCode].");
            }

            if (press)
            {
                ArtelInput.PressKey(key);
            }
            else
            {
                ArtelInput.ReleaseKey(key);
            }

            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Freezes game time, leaving the SDK itself running.
        /// </summary>
        /// <remarks>
        /// Everything this SDK waits on is already unscaled — the cursor walk, the scene settle,
        /// the key hold — so a frozen game can still be scanned, clicked and typed into. That is
        /// the point: it holds an animation, a countdown or a timed prompt still long enough to be
        /// read, without the game moving on underneath the reading.
        /// </remarks>
        private ActionResultDto ExecutePauseTime(int actionId)
        {
            // Only the first pause records anything. A second one would record the frozen 0 and
            // resume_time would then "resume" to a game that never moves again.
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
                // Restoring a scale nobody saved would silently overwrite whatever the game chose
                // for itself — a slow-motion sequence, a difficulty modifier — so say so instead.
                return ActionResultDto.Failure(
                    actionId, "resume_time: game time was not paused by pause_time.");
            }

            Time.timeScale = scaleBeforePause.Value;
            scaleBeforePause = null;
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Undoes a pause the SDK is about to stop being able to undo.
        /// </summary>
        /// <remarks>
        /// A run that dies while the game is paused would otherwise leave it frozen with the one
        /// thing that could unfreeze it gone. Called when the manager shuts down.
        /// </remarks>
        public void RestoreTimeScale()
        {
            if (scaleBeforePause.HasValue)
            {
                Time.timeScale = scaleBeforePause.Value;
                scaleBeforePause = null;
            }
        }

        /// <summary>
        /// Puts the game back where the run found it by reloading the scene it started in.
        /// </summary>
        /// <remarks>
        /// A single load tears down every loaded scene and rebuilds the start one from the same
        /// serialized data the launch used, and the game's <c>DontDestroyOnLoad</c> objects are
        /// dropped with it — a manager holding the score, the inventory or the run's progress is
        /// exactly what a reset has to clear, and the reloaded scene builds its own again through
        /// the same singleton guard that let the old one live. What no reload can reach: static
        /// fields, and anything already written to <c>PlayerPrefs</c> or disk.
        /// ponytail: scene state only, add save-data wiping when a game needs it.
        /// </remarks>
        private IEnumerator ExecuteResetGame(int actionId, Action<ActionResultDto> completed)
        {
            if (startupSceneBuildIndex < 0)
            {
                // Loading by path fails the same way, so there is nothing to try: the scene has to
                // be in Build Settings for the player to ever reach it again.
                completed(ActionResultDto.Failure(
                    actionId,
                    "reset_game: the scene the game started in is not in Build Settings: " +
                    startupScenePath));
                yield break;
            }

            // A pause and a held button belong to the run, not to the game. Carried across the
            // reload they would hand the fresh scene a frozen clock and a press it never saw begin.
            RestoreTimeScale();
            pointerEvents.ReleaseAll();
            ArtelInput.ReleaseAllVirtualInput();

            DoomPersistentObjects();
            yield return SceneManager.LoadSceneAsync(startupSceneBuildIndex, LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(AllSceneScanner.SettleSeconds);

            // Every target id named an object of the scene that just died, so a button_click later
            // in this batch would otherwise address a corpse.
            scanner.Scan();
            completed(ActionResultDto.Success(actionId));
        }

        /// <summary>
        /// 청한 그 실행을 위해 라이브 판독을 켠다.
        /// </summary>
        /// <remarks>
        /// 실행은 그것이 언제 시작하는지를 말하지만 연결은 그러지 않는다. 연결은 모든 씬을 도는 순회도 함께 시작시키는데, 그 순회
        /// 동안 찍은 판독은 플레이어가 한 번도 걸어가지 않은 화면을 서술한다 — 그래서 둘을 갈랐고, 이쪽이 세션이 다스리는 절반이다.
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

        /// <summary>다시 끈다. 돌고 있었든 아니든 성공한다.</summary>
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
        /// Hands the game's <c>DontDestroyOnLoad</c> objects to the scene that is about to be
        /// unloaded, so the reload destroys them the way it destroys everything else.
        /// </summary>
        /// <remarks>
        /// Moving them beats destroying them outright: <c>Destroy</c> only takes effect at the end
        /// of the frame, which is after the new scene's <c>Awake</c> has already asked whether a
        /// manager exists. Moved objects die with the unload, before anything in the new scene runs.
        /// This SDK is the one root that stays — it is running the coroutine that does this, and it
        /// owns the socket the result goes out on.
        /// </remarks>
        private static void DoomPersistentObjects()
        {
            var doomed = SceneManager.GetActiveScene();
            var dropped = new List<string>();
            foreach (var root in StraySpawnTracker.DontDestroyOnLoadScene().GetRootGameObjects())
            {
                if (root.GetComponentInChildren<ArtelManager>(true) != null)
                {
                    continue;
                }

                SceneManager.MoveGameObjectToScene(root, doomed);
                dropped.Add(root.name);
            }

            if (dropped.Count > 0)
            {
                // Named, because a game whose bootstrap lives outside the start scene loses these
                // for good — the one way reset_game can leave it worse off than it found it.
                Debug.Log("[Artel] reset_game dropped persistent object(s): " +
                          string.Join(", ", dropped));
            }
        }

        /// <summary>
        /// Captures the screen, or one element's area of it, and uploads it.
        /// </summary>
        /// <remarks>
        /// Runs inside the same batch as the actions before it, so a capture asked for after a
        /// click sees the screen that click produced — the ordering `scan_scene` already relies on.
        /// </remarks>
        private IEnumerator ExecuteCaptureScreen(
            int actionId,
            List<object> parameters,
            Action<ActionResultDto> completed)
        {
            if (capturer == null || uploader == null)
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
            var clipped = false;
            if (!request.IsFullScreen)
            {
                var targetId = request.TargetId.Value;
                if (!scanner.TryGetTarget(targetId, out var target))
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
                clipped = region.Clipped;
            }

            var image = default(CapturedImage);
            yield return capturer.Capture(request, pixelRect, captured => image = captured);
            if (!image.IsSuccess)
            {
                completed(ActionResultDto.Failure(actionId, image.Error));
                yield break;
            }

            var upload = default(CaptureUpload);
            yield return uploader.Upload(image, request, uploaded => upload = uploaded);
            if (!upload.IsSuccess)
            {
                completed(ActionResultDto.Failure(actionId, upload.Error));
                yield break;
            }

            completed(ActionResultDto.Success(actionId, new CaptureResultDto
            {
                CaptureId = upload.CaptureId,
                Url = upload.Url,
                ExpiresAt = upload.ExpiresAt,
                MimeType = request.ContentType,
                Width = image.Width,
                Height = image.Height,
                TargetId = request.TargetId,
                Clipped = clipped
            }));
        }

        private static string NotInteractable(int targetId)
        {
            return "Target is not interactable: " + targetId;
        }

        private static ActionResultDto ExecuteKeyClick(int actionId, List<object> parameters)
        {
            if (parameters == null || parameters.Count < 2 ||
                !TryReadKeyCode(parameters[0], out var key) ||
                !TryReadDuration(parameters[1], out var durationSeconds))
            {
                return ActionResultDto.Failure(
                    actionId,
                    "key_click requires params [keyCode, positiveDurationSeconds].");
            }

            ArtelInput.ClickKey(key, durationSeconds);
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// Drives an Input Manager axis by name. The legacy Input Manager exposes no runtime API
        /// for its axis-to-key bindings, so a virtual key press cannot reach <c>GetAxis</c> — the
        /// caller names the axis and states the value instead.
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

            ArtelInput.SetAxis(axisName, value);
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// A button is an axis in Unity, so this writes the same axis the caller would drive with
        /// <c>set_axis</c>. Releasing hands the axis back to the real input rather than pinning it
        /// at zero, which is what makes <c>GetButtonUp</c> report the edge.
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
                ArtelInput.SetAxis(axisName, 1f);
            }
            else
            {
                ArtelInput.ReleaseAxis(axisName);
            }

            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// The engine throws for an axis nobody set up, and that exception is the only runtime
        /// signal that an axis name is real — the bindings themselves are not readable. Without
        /// this check a misspelled name would report success and move nothing, which is the exact
        /// failure this action exists to avoid.
        /// </summary>
        /// <remarks>
        /// Reads the real <see cref="UnityEngine.Input"/> rather than the proxy on purpose: the
        /// proxy answers from a held value once one exists, so it would pass a name it never
        /// verified.
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
        /// Out-of-range values fail instead of being clamped: a caller asking for 5 has misread the
        /// axis, and clamping would report success for a request nobody made.
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

            return TryReadId(parameters, out button) && VirtualMouseState.IsButton(button);
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

        private static bool TryReadId(List<object> parameters, out int id)
        {
            id = 0;
            if (parameters == null || parameters.Count == 0 || parameters[0] == null)
            {
                return false;
            }

            if (parameters[0] is long longValue)
            {
                id = (int)longValue;
                return true;
            }

            if (parameters[0] is int intValue)
            {
                id = intValue;
                return true;
            }

            return int.TryParse(parameters[0].ToString(), out id);
        }
    }
}

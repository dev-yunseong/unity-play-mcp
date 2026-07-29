using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Artel.Capture;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    internal sealed class ActionExecutor
    {
        private readonly SceneScanner scanner;
        private readonly CursorController cursorController;
        private readonly PointerEventDispatcher pointerEvents;
        private readonly IScreenCapturer capturer;
        private readonly ICaptureUploader uploader;
        private readonly Action<Vector2> cursorMoved;
        private readonly Action<Vector2> pointerMoved;

        public ActionExecutor(
            SceneScanner scanner,
            CursorController cursorController,
            PointerEventDispatcher pointerEvents,
            IScreenCapturer capturer = null,
            ICaptureUploader uploader = null)
        {
            this.scanner = scanner;
            this.cursorController = cursorController;
            this.pointerEvents = pointerEvents;
            this.capturer = capturer;
            this.uploader = uploader;

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

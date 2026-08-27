using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Artel.Affordances.Scan;
using Artel.Capture;
using Artel.Evidence;
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

        /// <summary>원격 스캔 명령이 부르는 두 이음매. 이것들 없이 만들어진 실행기에서는 null 이고, 그러면 그 액션은 거절된다.</summary>
        private readonly IEvidenceScan evidenceScan;
        private readonly IEvidenceUploader evidenceUploader;

        private readonly Action<Vector2> cursorMoved;
        private readonly Action<Vector2> pointerMoved;

        /// <summary>서버와 맞춘 액션 이름. 결과에도 이 이름을 그대로 실어 서버가 짝을 맞춘다.</summary>
        private const string ScanEvidence = "scan_evidence";

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
            IReadingChannel readings = null,
            IEvidenceScan evidenceScan = null,
            IEvidenceUploader evidenceUploader = null)
        {
            this.readings = readings;
            this.scanner = scanner;
            this.cursorController = cursorController;
            this.pointerEvents = pointerEvents;
            this.capturer = capturer;
            this.uploader = uploader;
            this.evidenceScan = evidenceScan;
            this.evidenceUploader = evidenceUploader;

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

                case "scan_evidence":
                    yield return ExecuteScanEvidence(actionId, completed);
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

            SetButton(button, press);
            return ActionResultDto.Success(actionId);
        }

        /// <summary>
        /// 가상 마우스 상태와 uGUI 이벤트를 한 번에 민다.
        /// </summary>
        /// <remarks>
        /// <c>mouse_down</c> 과 <c>KeyCode.Mouse0</c> 을 실은 <c>key_down</c> 이 같은 버튼을
        /// 가리킨다. 두 경로가 각자 밀면 언젠가 한쪽만 절반을 밀어 "폴링에는 잡히는데 버튼은
        /// 안 눌리는" 상태가 되므로, 미는 일은 여기 한 자리에만 둔다.
        /// </remarks>
        private void SetButton(int button, bool press)
        {
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
        }

        private ActionResultDto ExecuteKeyHold(
            int actionId, string method, List<object> parameters, bool press)
        {
            if (parameters == null || parameters.Count == 0 ||
                !TryReadKeyCode(parameters[0], out var key))
            {
                return ActionResultDto.Failure(actionId, method + " requires params [keyCode].");
            }

            // KeyCode.Mouse0 은 마우스 왼쪽 버튼 그 자체다. 가상 키보드에만 넣으면 GetKey 로
            // 폴링하는 게임에만 닿고, 포인터 아래 오브젝트의 OnMouseDown 도 uGUI 핸들러도
            // 부르지 못한다 — 액션은 성공으로 보고되는데 게임은 아무 일도 없는 그 형태가 된다.
            if (MouseButtonKeyCode.TryGetButton(key, out var button))
            {
                SetButton(button, press);
                return ActionResultDto.Success(actionId);
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
        /// 실행이 처음 만난 자리로 게임을 되돌린다. 시작 씬을 다시 열고, 호출이 그렇게 말하면
        /// 게임의 <c>PlayerPrefs</c> 도 함께 비운다.
        /// </summary>
        /// <remarks>
        /// 로드 한 번이 열려 있는 모든 씬을 허물고, 실행이 처음 썼던 것과 같은 직렬화 데이터로
        /// 시작 씬을 다시 세운다. 게임의 <c>DontDestroyOnLoad</c> 오브젝트도 함께 사라진다 —
        /// 점수·인벤토리·진행도를 쥐고 있는 매니저야말로 리셋이 지워야 할 것이고, 다시 열린
        /// 씬이 예전 것을 살려 두었던 바로 그 싱글턴 가드를 통해 자기 것을 새로 만든다.
        ///
        /// 씬 상태는 언제나 사라진다. <c>PlayerPrefs</c> 는 <c>clearPlayerPrefs</c> 가 그렇게
        /// 말할 때만 사라지고, 그때도 SDK 자신의 <c>Artel.*</c> 항목은 지우기 앞뒤로 꺼냈다가
        /// 되쓴다 — 그러지 않으면 리셋을 시킨 서버로부터 이 세션이 스스로 로그아웃한다.
        /// 정적 필드와 디스크의 파일은 어느 쪽으로도 사라지지 않는다.
        ///
        /// 약속하는 것은 저장소를 비웠다는 것까지다. 게임이 첫 실행 상태라는 뜻은 아니다 —
        /// 리로드로 죽는 매니저가 <c>OnDestroy</c> 에서 자기 키를 다시 쓸 수 있고, 이 코루틴
        /// 안의 어떤 순서도 그것을 막지 못한다.
        /// </remarks>
        private IEnumerator ExecuteResetGame(
            int actionId, List<object> parameters, Action<ActionResultDto> completed)
        {
            // params 를 먼저 읽는다. Build Settings 가드보다 뒤에 두면 잘못 만든 호출이
            // "씬이 Build Settings 에 없다" 로 잘못 진단되어 돌아간다.
            if (!ResetRequestReader.TryRead(parameters, out var request, out var error))
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

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

            // 리로드보다 먼저 지운다. 게임이 세이브 데이터를 처음 읽는 자리는 시작 씬의
            // Awake/Start 이므로, 로드한 뒤에 지우면 구조적으로 한 프레임 늦어 이미 읽힌
            // 진행도를 남긴 채 Success 를 돌려주게 된다. 사이에 yield 를 두지 않는다.
            if (request.ClearPlayerPrefs)
            {
                ArtelOwnedPlayerPrefs.DeleteAllExceptOwn();
            }

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

        /// <summary>
        /// 서버가 보낸 원격 스캔 명령. 근거를 스캔하고, 그 문서를 올리고, 무엇이 되었는지 답한다.
        /// </summary>
        /// <remarks>
        /// 파라미터가 없다. 어느 빌드에 올릴지는 SDK 가 등록 응답에서 받아 쥐고 있는 gameBuildId 가 정하고, 서버는 그것을
        /// 다시 말해 줄 필요가 없다 — 말해 준다면 그것이 어긋날 수 있는 두 번째 사실이 된다.
        ///
        /// 받았다와 끝났다를 나누지 않는다. 이 실행기가 돌려주는 결과는 액션 큐가 다 비었을 때 ACTION_RESULT 한 프레임으로
        /// 나가고, 서버는 그것을 붙잡고 기다리지 않는다 — 화면은 ingested_at 이 바뀌는 것으로 완료를 안다. 이 답은 무엇이
        /// 잘못됐는지를 사람이 볼 수 있게 하는 쪽이다.
        ///
        /// 실패는 어느 걸음의 것이든 결과에 실린다. 조용히 삼키면 서버는 "보냈다"까지만 알고 화면은 영원히 기다린다.
        /// </remarks>
        private IEnumerator ExecuteScanEvidence(int actionId, Action<ActionResultDto> completed)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (evidenceScan == null || evidenceUploader == null)
            {
                completed(ActionResultDto.Failure(
                    actionId, ScanEvidence, "This build cannot scan and upload evidence."));
                yield break;
            }

            var scanned = default(ScannedEvidence);
            yield return evidenceScan.Run(result => scanned = result);
            if (!scanned.IsSuccess)
            {
                completed(ActionResultDto.Failure(actionId, ScanEvidence, scanned.Error));
                yield break;
            }

            var upload = default(EvidenceUpload);
            yield return evidenceUploader.Upload(scanned.Document, scanned.Thumbnails, uploaded => upload = uploaded);
            if (!upload.IsSuccess)
            {
                completed(ActionResultDto.Failure(actionId, ScanEvidence, upload.Error));
                yield break;
            }

            completed(ActionResultDto.Success(actionId, ScanEvidence, new EvidenceScanResultDto
            {
                ObjectKey = upload.ObjectKey,
                EvidenceDigest = upload.EvidenceDigest,
                ByteSize = upload.ByteSize,
                SchemaVersion = upload.SchemaVersion,
                SceneCount = scanned.SceneCount,
                SceneCapturesRegistered = upload.SceneCapturesRegistered,
                AlreadyRegistered = upload.AlreadyRegistered
            }));
#else
            // 출시된 빌드에는 읽을 근거가 애초에 구워지지 않는다. 빈 문서를 올려 서버의 표를 지우는 것보다 거절이 정직하다.
            // 같은 심볼 쌍을 AffordanceBootstrap.Follow 가 읽고, 그쪽이 씬 로드를 따라갈지를 정한다.
            completed(ActionResultDto.Failure(
                actionId, ScanEvidence, "Evidence is not baked into a release build, so there is nothing to scan."));
            yield break;
#endif
        }

        private static string NotInteractable(int targetId)
        {
            return "Target is not interactable: " + targetId;
        }

        /// <summary>
        /// 키를 그 시간만큼 눌렀다 놓는다. 마우스 버튼이면 놓는 일을 여기서 기다렸다 직접 한다.
        /// </summary>
        /// <remarks>
        /// 가상 키보드는 만료를 스스로 안다. 가상 마우스는 그러지 않고, 만료를 그쪽 상태에 넣어도
        /// 놓이는 순간을 아무도 몰라 <c>pointerUp</c> 과 <c>OnMouseUp</c> 이 빠진다. 그래서
        /// 마우스만 코루틴으로 갈라, 누름과 놓음 양쪽 모두가 이벤트를 내게 한다.
        ///
        /// 기다림은 scaled time 이 아니다. <c>pause_time</c> 이 걸린 게임에서 scaled 로 재면
        /// 영영 끝나지 않는다 — 가상 키보드도 같은 이유로 <c>unscaledTime</c> 으로 잰다.
        ///
        /// 기다리는 사이에 연결이 끊겨도 따로 정리할 것이 없다.
        /// <c>ReleaseAllVirtualInput</c> 과 <c>pointerEvents.ReleaseAll</c> 이 이미 버튼을
        /// 놓았고, 뒤늦은 놓음은 양쪽 모두에서 아무 일도 하지 않는다.
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
                ArtelInput.ClickKey(key, durationSeconds);
                completed(ActionResultDto.Success(actionId));
                yield break;
            }

            SetButton(button, true);
            yield return new WaitForSecondsRealtime(durationSeconds);
            SetButton(button, false);
            completed(ActionResultDto.Success(actionId));
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

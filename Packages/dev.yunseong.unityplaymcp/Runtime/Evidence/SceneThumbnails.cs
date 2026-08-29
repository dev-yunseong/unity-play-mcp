using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using Artel.Capture;
using UnityEngine;

namespace Artel.Evidence
{
    /// <summary>
    /// 씬 하나의 대표 이미지, 또는 만들지 못한 이유.
    /// </summary>
    /// <remarks>
    /// 성공과 실패가 한 타입에 산다. 못 찍었다는 사실도 서버가 받아 화면에 내보내는 값이라, 실패를 버리면 화면은 "아직 안
    /// 올렸다"와 "이 씬은 못 찍는다"를 구분할 수 없다.
    /// </remarks>
    internal struct SceneThumbnail
    {
        public string SceneName;

        /// <summary>JPEG 바이트. 실패면 null.</summary>
        public byte[] Jpeg;

        public int Width;
        public int Height;

        /// <summary>성공했으면 null. 실패면 서버가 그대로 저장하는 짧은 코드.</summary>
        public string FailureCode;

        public bool IsSuccess { get { return FailureCode == null && Jpeg != null && Jpeg.Length > 0; } }

        public static SceneThumbnail Failed(string sceneName, string failureCode)
        {
            return new SceneThumbnail { SceneName = sceneName, FailureCode = failureCode };
        }
    }

    /// <summary>
    /// 순회가 씬을 하나씩 읽는 동안 그 씬의 화면을 한 장씩 모은다.
    /// </summary>
    /// <remarks>
    /// <b>back buffer 를 읽는다.</b> 카메라를 골라 <c>RenderTexture</c> 로 직접 렌더하지 않는 이유가 둘이다. 하나는 Screen
    /// Space Overlay 캔버스가 카메라 렌더에 안 담긴다는 것 — 게임 화면에서 사람이 알아보는 것 대부분이 그 UI 다. 다른 하나는
    /// render pipeline 이다. 카메라를 직접 돌리면 Built-in 과 URP·HDRP 가 서로 다르게 굴지만, back buffer 는 무엇으로
    /// 그렸든 그려진 결과 하나다. <see cref="ScreenCapturer"/> 가 <c>capture_screen</c> 액션에서 이미 그 경로를 쓰고 있고,
    /// 여기서 새로 쓰는 대신 그것을 부른다.
    ///
    /// 그래서 계획에 적었던 "Built-in 카메라와 Overlay 캔버스를 함께 렌더한다"를 하지 않는다. 같은 결과를 더 적은 코드로,
    /// pipeline 을 가리지 않고 얻는다.
    ///
    /// 순회는 씬을 <c>Single</c> 로 띄우고 두 프레임을 준 뒤 이 자리를 부른다. 그 시점의 back buffer 에는 방금 올라온 씬만
    /// 있다 — 앞 씬은 이미 버려졌고, 이번 로드가 만든 <c>DontDestroyOnLoad</c> root 는 아직 화면에 있다. 그것이 이 이슈가
    /// 말하는 "그 씬과 그 로드가 만든 것만"이다.
    ///
    /// 캡처 실패가 순회를 멈추지 않는다. 어떤 실패든 이 씬 한 장의 실패로 적고 다음 씬으로 간다.
    ///
    /// 실패 이유를 여기서 갈래내지 않는 이유: 화면을 못 뜨는 사정(배치 실행이라 프레임버퍼가 없다, 인코딩이 실패했다)은
    /// <see cref="ScreenCapturer"/> 가 이미 문장으로 로그에 남긴다. 그 문장을 여기서 다시 코드로 분류하려면 남의 문자열을
    /// 패턴으로 읽어야 하고, 그쪽이 문구를 고치는 날 조용히 틀린 코드가 서버로 간다.
    /// </remarks>
    internal sealed class SceneThumbnailCollector
    {
        /// <summary>한 변의 최대 픽셀. 목록 카드에 들어갈 크기면 충분하고, 크면 씬 수백 개에서 업로드가 그만큼 길어진다.</summary>
        internal const int MaxEdge = 480;

        /// <summary>서버가 한 문서에 받는 캡처 수. 넘으면 티켓 요청 자체가 400 이라 여기서 멈춘다.</summary>
        internal const int MaxCaptures = 256;

        private readonly IScreenCapturer capturer;
        private readonly List<SceneThumbnail> thumbnails = new List<SceneThumbnail>();
        private readonly HashSet<string> seen = new HashSet<string>();

        public SceneThumbnailCollector(IScreenCapturer capturer)
        {
            this.capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
        }

        public IReadOnlyList<SceneThumbnail> Thumbnails { get { return thumbnails; } }

        /// <summary>순회가 도는 동안 이 수집기를 붙인다. 반드시 <see cref="Detach"/> 와 짝으로 쓴다.</summary>
        public void Attach()
        {
            SceneWalkHooks.SceneRead = CaptureScene;
        }

        /// <summary>
        /// 걸어 둔 자리를 뗀다.
        /// </summary>
        /// <remarks>
        /// 자기가 건 것일 때만 뗀다. 순회 둘이 겹칠 일은 없지만, 남이 건 것을 지우면 그쪽은 아무 말 없이 캡처를 잃는다.
        /// </remarks>
        public void Detach()
        {
            if (SceneWalkHooks.SceneRead == (Func<string, IEnumerator>)CaptureScene)
            {
                SceneWalkHooks.SceneRead = null;
            }
        }

        private IEnumerator CaptureScene(string sceneName)
        {
            var name = string.IsNullOrWhiteSpace(sceneName) ? string.Empty : sceneName.Trim();

            if (name.Length == 0)
            {
                // 이름이 없으면 서버가 어느 씬 행에 붙일지 정할 수 없다. 실패로도 적지 않는다 — 붙일 자리가 없는 실패는
                // 화면에서 영원히 안 읽히는 행이 된다.
                yield break;
            }

            // 씬 하나에 한 장이다. 순회가 같은 씬을 두 번 밟는 경우(빌드 인덱스와 주소로 각각 한 번)에 두 장을 보내면 서버가
            // 그 등록 전체를 400 으로 돌려준다.
            if (!seen.Add(name))
            {
                yield break;
            }

            if (thumbnails.Count >= MaxCaptures)
            {
                yield break;
            }

            var captured = default(CapturedImage);
            var threw = false;

            IEnumerator capture;

            try
            {
                // TargetId 가 null 이면 전체 화면이고, 전체 화면은 JPEG 다 — `UsePng` 는 그 둘에서 계산되는 값이라
                // 여기서 따로 정하지 않는다.
                capture = capturer.Capture(
                    new CaptureRequest { TargetId = null, MaxEdge = MaxEdge, Padding = 0f },
                    null,
                    image => captured = image);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Artel] The screen capture for " + name + " could not start: " + exception.Message);
                thumbnails.Add(SceneThumbnail.Failed(name, "capture-failed"));
                yield break;
            }

            // 코루틴이 도는 도중의 예외는 `yield return` 을 감쌀 수 없어 여기서 잡지 못한다. 한 걸음씩 밀면서 잡는다 —
            // 캡처 하나가 터져서 순회 전체가 멎으면 근거 문서가 아예 안 나온다.
            while (true)
            {
                object step;

                try
                {
                    if (!capture.MoveNext())
                    {
                        break;
                    }

                    step = capture.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Artel] The screen capture for " + name + " failed: " + exception.Message);
                    threw = true;
                    break;
                }

                yield return step;
            }

            if (threw)
            {
                thumbnails.Add(SceneThumbnail.Failed(name, "capture-failed"));
                yield break;
            }

            if (!captured.IsSuccess)
            {
                thumbnails.Add(SceneThumbnail.Failed(name, "capture-failed"));
                yield break;
            }

            thumbnails.Add(new SceneThumbnail
            {
                SceneName = name,
                Jpeg = captured.Bytes,
                Width = captured.Width,
                Height = captured.Height
            });
        }
    }
}

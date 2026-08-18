using System;
using System.Collections;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>바뀐 판독이 가는 자리.</summary>
    /// <remarks>
    /// 소켓이 아니라 이음매다. 이 패키지는 JSON 을 손으로 써서 그것을 싣고 나가는 게임이 직렬화 의존성을 지지 않게 하는데,
    /// 여기에 전송을 넣으면 원하든 원하지 않든 모든 게임에 대해 그것을 되돌리게 된다. 도착하는 것은 완성된 문서이고,
    /// 그것을 나르는 일은 다른 쪽의 결정이다.
    /// </remarks>
    public interface IPulseSink
    {
        void Send(string document);
    }

    /// <summary>
    /// 박자에 맞춰 감시 대상 멤버를 읽고, 답이 바뀌었을 때 건넨다.
    /// </summary>
    /// <remarks>
    /// 게임이 시작하기 전에 쓰인 리포트에 대고 명세를 돌릴 수는 없다. 근거는 무엇이 참이어야 하고 무엇이 바뀔지를 말하고,
    /// 지금 무엇이 참인지는 도는 게임만이 말하며, 이것이 그것을 나르는 채널이다.
    ///
    /// 박자마다가 아니라 바뀔 때 보낸다. 게임이 있는 상태는 대개 한 프레임 전에 있던 그 상태이고, 같은 문서를 초당 예순 번
    /// 받은 독자는 그중 무엇이 중요했는지를 스스로 알아내야 한다.
    ///
    /// 결정하는 것은 문서의 다이제스트가 아니라 움직인 값의 목록이다. 다이제스트를 먼저 시도했는데, 실행해 봐야만 드러나는
    /// 방식으로 틀렸다: 문서는 그것이 몇 번째 판독인지와 어느 프레임에 찍혔는지를 나르고 둘 다 매번 다르므로, 모든 판독이
    /// 새것으로 보였고 게이트는 단 한 번도 닫히지 않았다. 샘플 게임에서 실측하니 판독 33 건 중 33 건이 나갔고 그중 22 건이
    /// 제 텍스트로 아무것도 바뀌지 않았다고 말하고 있었다. 값 자체를 비교하는 것은 판독이 주장하는 바에서 어긋날 수 없다.
    /// 판독이 발행하는 바로 그 비교이기 때문이다.
    ///
    /// 게이트가 일하게 만드는 것은 그 위쪽에 있다. 경계 없는 씬 덤프를 해싱하면 매 프레임 변화를 보고하게 되고 — 숨 쉬는
    /// idle 애니메이션 하나면 충분하다 — 그래서 다른 SDK 의 전부 읽기 모드는 플레이 중에 쓸 수 있었던 적이 없다. 이쪽은
    /// 근거가 실제로 이름 댄 멤버를 해싱하므로, 값이 움직였다는 것은 어딘가의 조건이 이제 다르게 읽힐 수 있다는 뜻이다.
    ///
    /// 일부러 시작하고 저절로 시작하는 일은 없다. 박자마다 필드 백 개를 읽는 값은 게임이 동의해야 하는 것이고, 설치되는
    /// 순간부터 폴링을 시작하는 패키지는 리포트만 원했던 프로젝트에 그 값을 치르게 한다.
    /// </remarks>
    public sealed class Pulse : MonoBehaviour
    {
        /// <summary>
        /// 하나: 씬 이름, static 들, 그리고 감시 대상 멤버를 나르는 객체들.
        /// </summary>
        /// <remarks>
        /// 리포트 자신의 버전과 별개다. 둘은 서로 다른 코드가 서로 다른 순간에 읽고 어느 쪽도 다른 쪽의 모양에 대해 의견이 없다 —
        /// 이것을 읽는 쪽은 기록이 쓸모없고, 리포트를 읽는 쪽은 폴링할 수 없다.
        /// </remarks>
        /// <remarks>
        /// 둘. 객체들이 한 목록이기를 그만두었기 때문이다. 그것들은 <c>active</c> 와 <c>deactive</c> 로 나뉘고 어느 쪽인지를
        /// 말하는 플래그를 더는 나르지 않는다 — 같은 사실이 두 자리가 아니라 한 자리에 있다. 한 모양에 대고 쓰인 독자는 다른
        /// 모양을 읽을 수 없으므로, 그것을 발견하도록 두는 대신 번호가 움직인다.
        /// </remarks>
        internal const int SchemaVersion = 2;

        /// <summary>판독 사이의 초.</summary>
        /// <remarks>
        /// 초당 열. 테스터가 그것을 일으킨 것을 아직 보고 있는 동안 변화가 도착할 만큼 빠르고, 판독 자체가 프로파일러에 잡히는
        /// 것이 되지 않을 만큼 느리다. 트래픽을 낮게 유지하는 것은 이것이 아니라 게이트다.
        /// </remarks>
        private const float DefaultInterval = 0.1f;

        private static Pulse _beating;

        private IPulseSink _sink;
        private float _interval = DefaultInterval;
        private bool _read;

        /// <summary>직전 판독이 sink 에 닿지 못했는지.</summary>
        private bool _lost;

        /// <summary>
        /// 감시가 시작된 순간부터 세어 이것이 몇 번째 판독인지.
        /// </summary>
        /// <remarks>
        /// 보낸 것마다가 아니라 찍은 판독마다 센다. 그래서 번호의 빈자리 자체가 소식이다: 그 구간 동안 상태가 가만히 있었다는
        /// 말인데, 그것이 없으면 독자는 타임스탬프 둘과 간격에 대한 추측으로 그것을 유추해야 한다.
        /// </remarks>
        private long _reading;

        /// <summary>아직 아무 데도 가지 않은 좌표들과, 그것들이 마지막으로 한 말.</summary>
        private readonly Restless _restless = new Restless();

        /// <summary>직전 판독이 한 말. 이번 판독이 그 차이를 말할 수 있도록.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _since =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

        internal static bool InProgress => _beating != null;

        /// <summary>이것이 시작된 뒤로 나간 판독의 수.</summary>
        internal static int Sent { get; private set; }

        /// <summary>찍었으나 바뀌지 않은 것으로 판명된 판독의 수.</summary>
        /// <remarks>
        /// 말해 두는 것은 두 숫자가 함께여야 게이트가 무슨 일이든 하고 있음을 보이기 때문이다. 모든 판독이 나가는 게임은 어떤
        /// 조건도 언급하지 않는 이유로 움직이는 멤버를 watch list 에 가진 것이고, 그것은 우회해 조율할 것이 아니라 가서 봐야
        /// 할 것이다.
        /// </remarks>
        internal static int Held { get; private set; }

        /// <summary>읽기를 시작하거나, 이미 돌고 있어서 false 로 답한다.</summary>
        internal static bool Begin(IPulseSink sink, float interval = DefaultInterval)
        {
            if (_beating != null || sink == null || interval <= 0f)
            {
                return false;
            }

            var carrier = new GameObject("Artel Pulse") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(carrier);

            _beating = carrier.AddComponent<Pulse>();
            _beating._sink = sink;
            _beating._interval = interval;
            Sent = 0;
            Held = 0;

            _beating.StartCoroutine(_beating.Beat());
            return true;
        }

        internal static void Stop()
        {
            if (_beating == null)
            {
                return;
            }

            var carrier = _beating.gameObject;

            _beating._sink = null;
            _beating = null;
            Destroy(carrier);
        }

        private IEnumerator Beat()
        {
            // 첫 판독은 언제나 나간다. 아직 아무 말도 하지 않았으므로 "바뀌지 않음" 은 이것이 그것에 대해 할 수 있는 주장이
            // 아니다.
            while (_beating == this)
            {
                Take();
                yield return new WaitForSecondsRealtime(_interval);
            }
        }

        private void Take()
        {
            string document;
            var settled = false;

            try
            {
                // carrier 는 씬 로드보다 오래 살도록 만들어졌으므로, 그것을 쥔 씬은 Unity 가 그렇게 오래 사는 나머지 전부를 두는
                // 바로 그 씬이다. 스스로 설치되는 패키지가 그 씬에 대해 가진 유일한 손잡이이고, 스캔 자신의 순회도 같은 방식으로
                // 그것을 잡는다.
                document = LiveState.Compose(
                    ++_reading, gameObject.scene, _restless, _since, _lost, out settled);
            }
            catch (Exception exception)
            {
                // 나쁜 판독 하나는 건너뛸 판독이지 감시를 멈출 이유가 아니다. 던지는 필드는 이미 문서 안에서 읽지 못한 것으로
                // 보고된다. 이것은 걷기 자체가 무너진 경우이고, 씬이 헐리는 중이면 그런 일이 생길 수 있다.
                Debug.LogWarning("[Artel] A reading could not be taken: " + exception.Message);
                return;
            }

            if (_read && settled)
            {
                Held++;
                return;
            }

            _read = true;

            try
            {
                _sink.Send(document);
            }
            catch (Exception exception)
            {
                // 판독은 도착했든 아니든 유효하다. 해시를 잊으면 다음 박자에 같은 문서를 다시 보내고 sink 가 언짢은 동안 계속 그러게
                // 되는데, 그것이 소켓 하나가 망가진 것을 홍수로 바꾸는 모양이다.
                //
                // 대신 다음 것이 전량으로 나간다. 판독은 움직인 것만 나르므로 잃어버린 하나는 아무도 다시 듣지 못할 차이다 — 독자는
                // 무언가 그 값들을 움직이기 전까지 그것들에 대해 틀린 채로 있고, 그런 일은 영영 없을 수도 있다. 전량 판독 하나가 그것을
                // 고치고 그다음부터 차이가 다시 이어지는데, 그것은 재전송이 만들 홍수가 아니다.
                _lost = true;
                Debug.LogWarning("[Artel] A reading could not be delivered: " + exception.Message);
                return;
            }

            _lost = false;
            Sent++;
        }

        private void OnDestroy()
        {
            if (_beating == this)
            {
                _beating = null;
            }
        }
    }
}

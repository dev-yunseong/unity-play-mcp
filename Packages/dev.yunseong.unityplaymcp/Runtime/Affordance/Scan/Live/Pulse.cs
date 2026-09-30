using System;
using System.Collections;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>바뀐 pulse 가 가는 자리.</summary>
    /// <remarks>
    /// 완성된 JSON 문서를 받는다. 전송 방식은 구현이 정해서, 게임이 직렬화나 전송 의존성을 지지 않게 한다.
    /// </remarks>
    public interface IPulseSink
    {
        void Send(string document);
    }

    /// <summary>
    /// <c>pulse</c> 간격마다 감시 대상 멤버를 읽고, 값이 바뀌었을 때만 sink 로 보낸다.
    /// </summary>
    /// <remarks>
    /// 변화 판정은 문서 다이제스트가 아니라 움직인 값의 목록으로 한다. 문서에는 매번 다른 reading 번호와 frame 이 들어 있어
    /// 다이제스트로는 모든 pulse 가 바뀐 것으로 보인다.
    ///
    /// 자동으로 시작하지 않는다. 매 pulse 필드를 읽는 비용은 게임이 명시적으로 켰을 때만 치른다.
    /// </remarks>
    public sealed class Pulse : MonoBehaviour
    {
        /// <summary>
        /// pulse 문서의 schema 버전. 리포트의 버전과 별개다.
        /// </summary>
        /// <remarks>
        /// 2 부터 객체가 플래그 대신 <c>active</c>/<c>deactive</c> 목록으로 나뉜다. 형태가 바뀌면 읽는 쪽이 알 수 있게 올린다.
        /// </remarks>
        internal const int SchemaVersion = 2;

        /// <summary>pulse 사이의 초.</summary>
        /// <remarks>
        /// 더 자주 읽으면 플레이 모드에서 게임이 눈에 띄게 느려진다. 이 간격보다 짧게 바뀌었다 돌아온 값은 보이지 않으므로,
        /// 필요하면 Project Settings 에서 간격을 줄인다(<c>PulseIntervalPreference</c> 가 저장하고 <see cref="Begin"/> 이 받는다).
        /// </remarks>
        internal const float DefaultInterval = 1f;

        /// <summary>전달 사이의 초.</summary>
        /// <remarks>
        /// 읽기 간격을 줄여도 소켓 메시지는 1초에 한 번 모아 보낸다.
        ///
        /// 모아도 병합하지 않는다. 읽는 쪽이 reading 번호와 frame 순서로 구간을 복원하므로 <c>pulse</c> 는 각각 그대로 보낸다.
        /// </remarks>
        private const float DefaultDelivery = 1f;

        private static Pulse _beating;

        private IPulseSink _sink;
        private float _interval = DefaultInterval;
        private float _delivery = DefaultDelivery;

        /// <summary>읽었으나 아직 보내지 않은 pulse.</summary>
        private readonly System.Collections.Generic.List<string> _pending =
            new System.Collections.Generic.List<string>();
        private bool _read;

        /// <summary>직전 pulse 가 sink 에 닿지 못했는지.</summary>
        private bool _lost;

        /// <summary>다음 pulse 를 차이가 아니라 <c>whole</c> 로 찍으라는 요청이 왔는지.</summary>
        /// <remarks>
        /// 읽는 쪽이 다시 붙거나 <c>start_readings</c> 를 다시 보낸 경우다. 소켓은 client 가 없어도 보내기에 실패하지 않아
        /// <see cref="_lost"/> 만으로는 이 경우를 알 수 없다 (#69).
        /// </remarks>
        private bool _wholeRequested;

        /// <summary>이 인스턴스가 찍는 reading 들의 run.</summary>
        /// <remarks>
        /// <see cref="_reading"/> 은 <see cref="Begin"/> 마다 1부터 다시 센다. 새 run 의 번호가 이전 run 보다 작아 MCP server 가
        /// 지난 reading 으로 버리지 않도록, 번호를 비교해도 되는지 알 수 있게 run 을 싣는다 (#69).
        /// </remarks>
        private string _run;

        /// <summary>
        /// 감시 시작부터 센 pulse 번호.
        /// </summary>
        /// <remarks>
        /// 보낸 것이 아니라 읽은 pulse 마다 센다. 번호가 비면 그 구간에 값이 바뀌지 않았다는 뜻이다.
        /// </remarks>
        private long _reading;

        /// <summary>월드 좌표 값의 흔들림을 거른다.</summary>
        private readonly Restless _restless = new Restless();

        /// <summary>화면 사각형 값의 흔들림을 거른다.</summary>
        /// <remarks>
        /// 경계는 1 픽셀이다. 그 아래로는 그려지는 모습도 포인터가 닿는 대상도 달라지지 않는다. 월드 경계 0.001 은 픽셀에서는
        /// 아무것도 거르지 못한다.
        /// </remarks>
        private readonly Restless _pixels = new Restless(1f);

        /// <summary>직전 pulse 의 값. 이번 pulse 의 차이를 구하는 데 쓴다.</summary>
        private readonly System.Collections.Generic.Dictionary<string, string> _since =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);

        internal static bool InProgress => _beating != null;

        /// <summary>돌고 있는 reading 의 run. 돌고 있지 않으면 null 이다.</summary>
        internal static string CurrentRun => _beating == null ? null : _beating._run;

        /// <summary>시작 뒤 보낸 pulse 수.</summary>
        internal static int Sent { get; private set; }

        /// <summary>읽었으나 바뀌지 않아 보내지 않은 pulse 수.</summary>
        /// <remarks>
        /// <see cref="Sent"/> 와 함께 변화 판정이 동작하는지 보여 준다. 이 값이 늘지 않으면 watch list 에 계속 움직이는 멤버가 있다.
        /// </remarks>
        internal static int Held { get; private set; }

        /// <summary>읽기를 시작하거나, 이미 돌고 있어서 false 로 답한다.</summary>
        internal static bool Begin(IPulseSink sink, float interval = DefaultInterval)
        {
            if (_beating != null || sink == null || interval <= 0f)
            {
                return false;
            }

            var carrier = new GameObject("Unity Play MCP Pulse") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(carrier);

            _beating = carrier.AddComponent<Pulse>();
            _beating._sink = sink;
            _beating._interval = interval;
            _beating._run = Guid.NewGuid().ToString("N");
            Sent = 0;
            Held = 0;

            _beating.StartCoroutine(_beating.Beat());
            return true;
        }

        /// <summary>채널이 돌고 있으면 다음 pulse 를 <c>whole</c> 로 찍게 한다.</summary>
        internal static void RequestWhole()
        {
            if (_beating != null)
            {
                _beating._wholeRequested = true;
            }
        }

        internal static void Stop()
        {
            if (_beating == null)
            {
                return;
            }

            // carrier 를 파괴하면 coroutine 이 그 자리에서 멈추므로 남은 reading 은 여기서 보낸다.
            _beating.Deliver();

            var carrier = _beating.gameObject;

            _beating._sink = null;
            _beating = null;
            Destroy(carrier);
        }

        private IEnumerator Beat()
        {
            var untilDelivery = _delivery;

            // 첫 pulse 는 비교할 이전 값이 없으므로 항상 나간다.
            while (_beating == this)
            {
                Take();

                yield return new WaitForSecondsRealtime(_interval);

                untilDelivery -= _interval;

                if (untilDelivery > 0f)
                {
                    continue;
                }

                untilDelivery = _delivery;
                Deliver();
            }
        }

        /// <summary>지난 전달 이후 읽은 pulse 를 모두 보낸다.</summary>
        private void Deliver()
        {
            for (var at = 0; at < _pending.Count; at++)
            {
                try
                {
                    _sink.Send(_pending[at]);
                }
                catch (Exception exception)
                {
                    // 재전송하면 sink 가 실패하는 동안 쌓여 폭주하므로 다시 보내지 않는다. 대신 다음 pulse 를 whole 로 보내
                    // 잃은 차이를 복구한다. 배치의 나머지는 도착하지 않은 pulse 에 대한 차이라 버린다.
                    _lost = true;
                    _pending.Clear();
                    Debug.LogWarning("[Unity Play MCP] A reading could not be delivered: " + exception.Message);
                    return;
                }

                _lost = false;
                Sent++;
            }

            _pending.Clear();
        }

        private void Take()
        {
            string document;
            var settled = false;
            var forced = _wholeRequested;

            try
            {
                // carrier 는 DontDestroyOnLoad 이므로 gameObject.scene 은 DontDestroyOnLoad 씬이다. 스캔도 같은 방식으로
                // 그 씬을 얻는다.
                document = WithRun(LiveState.Compose(
                    ++_reading, gameObject.scene, _restless, _pixels, _since, _lost || forced, out settled));
            }
            catch (Exception exception)
            {
                // walk 자체가 실패한 경우다(씬 unload 중 등). 이 pulse 만 건너뛰고 감시는 계속한다. 필드 하나의 예외는
                // 문서 안에서 따로 보고된다.
                Debug.LogWarning("[Unity Play MCP] A reading could not be taken: " + exception.Message);
                return;
            }

            _wholeRequested = false;

            // 요청된 whole pulse 는 값이 그대로여도 보낸다. 요청한 쪽이 기다리고 있다.
            if (_read && settled && !forced)
            {
                Held++;
                return;
            }

            _read = true;

            // 읽기와 전달은 간격이 달라 전달 시점까지 모아 둔다.
            _pending.Add(document);
        }

        /// <summary>문서 맨 앞에 run 을 끼운다.</summary>
        /// <remarks>
        /// <see cref="LiveState"/> 가 문서를 <c>{"schema":</c> 로 시작한다고 가정한다. 다시 파싱해 직렬화하면 whole pulse 를
        /// 한 번 더 훑게 되므로 문자열로 끼운다(<c>WebSocketPulseSink</c> 와 같다).
        /// </remarks>
        private string WithRun(string document)
        {
            return "{\"run\":\"" + _run + "\"," + document.Substring(1);
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

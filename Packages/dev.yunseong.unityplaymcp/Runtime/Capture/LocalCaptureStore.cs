using System.Collections.Generic;

namespace Artel.Capture
{
    /// <summary>
    /// The last few captures, held in memory for the local test page to serve.
    /// </summary>
    /// <remarks>
    /// 캡처가 오케스트레이션으로 못 가는 자리를 메운다. 티켓 엔드포인트는 실행 중인 QA 가 없는 인스턴스를
    /// 거절하는데, 테스트 페이지에서 찍는 캡처는 전부 그 경우다.
    ///
    /// 개수를 묶어 둔 것은 에디터 한 세션이 캡처를 수백 장 찍기 때문이다. 브라우저가 실제로 되묻는 것은
    /// 화면에 걸린 한 장뿐이고, 페이지를 새로 고치면 그것마저 다시 요청된다 — 몇 장의 여유는 그 몫이다.
    /// </remarks>
    internal sealed class LocalCaptureStore
    {
        private const int Kept = 8;

        /// <summary>
        /// 캡처를 넣는 쪽은 유니티 메인 스레드고, 꺼내는 쪽은 <see cref="ArtelTestPageServer"/> 의 수신
        /// 스레드다. 두 스레드가 같은 사전을 만지므로 잠근다.
        /// </summary>
        private readonly object gate = new object();

        private readonly Dictionary<string, StoredCapture> byId =
            new Dictionary<string, StoredCapture>();

        private readonly Queue<string> order = new Queue<string>();

        private int issued;

        /// <summary>Stores one capture and returns the id the page asks for it by.</summary>
        public string Add(byte[] bytes, string contentType)
        {
            lock (gate)
            {
                var id = "capture-" + (++issued);
                byId[id] = new StoredCapture { Bytes = bytes, ContentType = contentType };
                order.Enqueue(id);

                while (order.Count > Kept)
                {
                    byId.Remove(order.Dequeue());
                }

                return id;
            }
        }

        public bool TryGet(string id, out StoredCapture capture)
        {
            lock (gate)
            {
                return byId.TryGetValue(id, out capture);
            }
        }

        internal struct StoredCapture
        {
            public byte[] Bytes;
            public string ContentType;
        }
    }
}

using System.IO;
using System.Text;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 바뀐 판독마다 한 줄씩 쓴다. 소켓이 전혀 없어도 판독을 지켜볼 수 있도록.
    /// </summary>
    /// <remarks>
    /// 연결이 아니라 파일인 것은, 아무도 듣고 있지 않은 동안에도 패키지가 쓸모 있어야 하기 때문이다. 이것을 tail 하는
    /// 테스터는 에이전트가 볼 것과 같은 문서를 같은 순서로 보고, <c>tail -f</c> 로 읽을 수 있는 채널은 그 결함이 눈에
    /// 보이는 채널이다.
    ///
    /// 한 줄에 문서 하나이고 다시 서식을 입히지 않으므로, 파일은 그것에 대한 진술이 아니라 도착한 그것이다. 갈아치우지
    /// 않고 덧붙인다: 입력 전의 상태가 입력 후의 상태를 뜻 있게 만드는 것이고, 최신 판독만 쥔 파일은 무엇이 바뀌었는지를
    /// 말하는 모든 쌍의 절반을 버린다.
    ///
    /// 핸들은 열어 둔다. 판독마다 열고 닫는 것이 판독 하나를 짓는 것보다 비싸고, 줄마다 하는 flush 가 독자로 하여금
    /// 버퍼를 기다리지 않고 그것을 보게 한다.
    /// </remarks>
    public sealed class PulseFile : IPulseSink, System.IDisposable
    {
        private const string FileName = "artel-pulse.jsonl";

        /// <summary>판독이 쓰이는 자리.</summary>
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        private StreamWriter _writer;

        /// <summary>파일을 열거나, 왜 열 수 없었는지 말하고 null 로 답한다.</summary>
        /// <remarks>
        /// 쓰기마다 실패하는 객체가 아니라 null 이다. 열 수 없는 sink 는 한 박자 뒤가 아니라 게임이 시작하기 전에 알아야 할
        /// 것이다.
        ///
        /// 쓰이는 동안 다른 쪽이 읽을 수 있도록 연다. 한 줄에 문서 하나인 파일은 자라는 대로 따라 읽으라고 존재하고 — 그것이
        /// 이것이 기본 sink 인 이유 전체다 — 독자를 잠가 내는 핸들은 그것이 만들어진 목적을 스스로 무너뜨린다. 실측: 1초마다
        /// 폴링하는 독자가 읽는 순간 파일을 차지했고, 다음번 감시 시작 시도는 공유 위반으로 실패해 채널이 아예 없었다.
        ///
        /// <see cref="StreamWriter"/> 자신의 생성자로는 이것을 말할 방법이 없으므로 스트림을 먼저 만들어 건넨다.
        /// </remarks>
        public static PulseFile Open(bool append = true)
        {
            try
            {
                var stream = new FileStream(
                    Path,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                return new PulseFile
                {
                    _writer = new StreamWriter(stream, new UTF8Encoding(false))
                };
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Artel] Could not open " + Path + ": " + exception.Message);
                return null;
            }
        }

        public void Send(string document)
        {
            if (_writer == null)
            {
                return;
            }

            _writer.Write(document);
            _writer.Write('\n');
            _writer.Flush();
        }

        public void Dispose()
        {
            if (_writer == null)
            {
                return;
            }

            _writer.Dispose();
            _writer = null;
        }
    }
}

using System.IO;
using System.Text;
using UnityEngine;

namespace UnityPlayMcp.Affordances.Live
{
    /// <summary>
    /// 바뀐 pulse 마다 JSON 한 줄을 파일에 쓴다. 소켓 연결 없이도 pulse 를 볼 수 있다.
    /// </summary>
    /// <remarks>
    /// 받은 문서를 서식 변경 없이 그대로 쓴다. 이전 상태와 비교할 수 있도록 덮어쓰지 않고 덧붙인다.
    ///
    /// pulse 마다 열고 닫는 비용을 피하려고 핸들을 열어 두고, 읽는 쪽이 바로 보도록 줄마다 flush 한다.
    /// </remarks>
    public sealed class PulseFile : IPulseSink, System.IDisposable
    {
        private const string FileName = "unity-play-mcp-pulse.jsonl";

        /// <summary>pulse 가 쓰이는 자리.</summary>
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        private StreamWriter _writer;

        /// <summary>파일을 열거나, 왜 열 수 없었는지 말하고 null 로 답한다.</summary>
        /// <remarks>
        /// 열 수 없는 sink 는 쓰기마다 실패하는 대신 시작 전에 null 로 드러낸다.
        ///
        /// 쓰는 동안 다른 쪽이 읽을 수 있게 <c>FileShare.ReadWrite</c> 로 연다. 막으면 읽는 쪽이 파일을 잡고 있을 때 다음
        /// 열기가 공유 위반으로 실패한다. <see cref="StreamWriter"/> 생성자로는 공유 모드를 줄 수 없어 스트림을 먼저 만든다.
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
                Debug.LogWarning("[Unity Play MCP] Could not open " + Path + ": " + exception.Message);
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

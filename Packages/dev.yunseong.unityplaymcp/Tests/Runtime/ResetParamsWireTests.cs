using System.Collections.Generic;
using UnityPlayMcp.Capture;
using UnityPlayMcp.Protocol.Dto;
using UnityPlayMcp.Serialization;
using NUnit.Framework;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 실제 wire 를 거친 options 오브젝트를 reader 가 읽는지 확인한다.
    /// </summary>
    /// <remarks>
    /// 다른 params test 는 <c>Dictionary&lt;string, object&gt;</c> 를 직접 넘겨 <c>JObject</c> 캐스트 문제를 우회한다.
    /// 여기서는 서버와 같은 JSON 을 <see cref="NewtonsoftJsonCodec"/> 로 역직렬화한 <c>Parameters</c> 를 넘긴다.
    /// </remarks>
    public sealed class ResetParamsWireTests
    {
        private static readonly IJsonCodec Codec = new NewtonsoftJsonCodec();

        [Test]
        public void ResetReadsClearPlayerPrefsFromTheWire()
        {
            const string json =
                "{\"type\":\"ACTION\",\"id\":5,\"actions\":[" +
                "{\"id\":1,\"method\":\"reset_game\"," +
                "\"params\":[{\"clearPlayerPrefs\":true}]}]}";

            var parameters = ReadFirstActionParameters(json);

            Assert.That(
                ResetRequestReader.TryRead(parameters, out var request, out var error),
                Is.True,
                error);
            Assert.That(request.ClearPlayerPrefs, Is.True);
        }

        /// <summary>
        /// 명시적 false 는 기본값과 결과가 같으므로 파싱 성공 여부 자체를 확인한다.
        /// </summary>
        [Test]
        public void ResetReadsAnExplicitFalseFromTheWire()
        {
            const string json =
                "{\"type\":\"ACTION\",\"id\":5,\"actions\":[" +
                "{\"id\":1,\"method\":\"reset_game\"," +
                "\"params\":[{\"clearPlayerPrefs\":false}]}]}";

            var parameters = ReadFirstActionParameters(json);

            Assert.That(
                ResetRequestReader.TryRead(parameters, out var request, out var error),
                Is.True,
                error);
            Assert.That(request.ClearPlayerPrefs, Is.False);
        }

        /// <summary>
        /// wire 를 거친 문자열은 bool 이 아니므로 거절된다.
        /// </summary>
        [Test]
        public void ResetRejectsAStringClearFlagFromTheWire()
        {
            const string json =
                "{\"type\":\"ACTION\",\"id\":5,\"actions\":[" +
                "{\"id\":1,\"method\":\"reset_game\"," +
                "\"params\":[{\"clearPlayerPrefs\":\"true\"}]}]}";

            var parameters = ReadFirstActionParameters(json);

            Assert.That(
                ResetRequestReader.TryRead(parameters, out _, out var error),
                Is.False);
            Assert.That(error, Does.Contain("clearPlayerPrefs"));
        }

        /// <summary>
        /// 같은 codec 을 거치는 <c>capture_screen</c> options 도 확인한다. 서버가 아직 보내지 않는 값이다.
        /// </summary>
        [Test]
        public void CaptureReadsMaxEdgeFromTheWire()
        {
            const string json =
                "{\"type\":\"ACTION\",\"id\":6,\"actions\":[" +
                "{\"id\":1,\"method\":\"capture_screen\"," +
                "\"params\":[42,{\"maxEdge\":256,\"padding\":4}]}]}";

            var parameters = ReadFirstActionParameters(json);

            Assert.That(
                CaptureRequestReader.TryRead(parameters, out var request, out var error),
                Is.True,
                error);
            Assert.That(request.TargetId, Is.EqualTo(42));
            Assert.That(request.MaxEdge, Is.EqualTo(256));
            Assert.That(request.Padding, Is.EqualTo(4f));
        }

        private static List<object> ReadFirstActionParameters(string json)
        {
            var request = Codec.Deserialize<AgentRequestDto>(json);
            Assert.That(request, Is.Not.Null);
            Assert.That(request.Actions, Is.Not.Empty);
            return request.Actions[0].Parameters;
        }
    }
}

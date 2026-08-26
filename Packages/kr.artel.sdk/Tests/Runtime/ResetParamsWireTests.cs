using System.Collections.Generic;
using Artel.Capture;
using Artel.Protocol.Dto;
using Artel.Serialization;
using NUnit.Framework;

namespace Artel.Tests
{
    /// <summary>
    /// 실제 wire 를 지나온 options 오브젝트를 리더가 읽는지 본다.
    /// </summary>
    /// <remarks>
    /// 이 파일이 없으면 고른 wire 모양은 전혀 검증되지 않는다. 나머지 params 테스트는
    /// <c>Dictionary&lt;string, object&gt;</c> 를 손으로 만들어 리더에 바로 넘기는데,
    /// 그 모양은 <c>JObject</c> 문제를 우회하기 때문에 캐스트가 언제나 null 이 되는 코드에서도
    /// 그대로 통과한다. 그래서 여기서는 서버가 보내는 것과 같은 JSON 문자열을
    /// <see cref="NewtonsoftJsonCodec"/> 로 통과시켜 DTO 의 <c>Parameters</c> 를 얻은 뒤,
    /// 그 값을 리더에 먹인다.
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
        /// false 를 명시적으로 보낸 경우도 같은 길을 지난다 — 읽히지 않아 기본값으로 떨어진
        /// 것과 구분되지 않으므로, 파싱이 성공했다는 사실 자체가 확인할 값이다.
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
        /// wire 를 지나온 문자열은 bool 이 아니다. 손으로 만든 사전에서와 똑같이 거절된다.
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
        /// 같은 코덱을 지나는 <c>capture_screen</c> options. 이 액션의 options 는 서버가 아직
        /// 보내지 않아 잠복해 있던 자리이고, 여기서 함께 못을 박는다.
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
            var request = Codec.Deserialize<ArtelRequestDto>(json);
            Assert.That(request, Is.Not.Null);
            Assert.That(request.Actions, Is.Not.Empty);
            return request.Actions[0].Parameters;
        }
    }
}

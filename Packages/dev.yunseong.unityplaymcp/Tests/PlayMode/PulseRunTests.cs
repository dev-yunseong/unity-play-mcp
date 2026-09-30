using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityPlayMcp.Affordances.Live;
using UnityPlayMcp.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 읽는 쪽이 reading 번호를 비교해도 되는지 알 수 있는지, 요청하면 whole reading 을 받는지 확인한다 (#69).
    /// </summary>
    /// <remarks>
    /// <see cref="Pulse"/> 는 coroutine 으로 돌고 <c>DontDestroyOnLoad</c> carrier 에 있으므로 play mode 에서만 돈다.
    /// </remarks>
    public sealed class PulseRunTests
    {
        private sealed class CapturingSink : IPulseSink
        {
            internal readonly List<string> Documents = new List<string>();

            public void Send(string document)
            {
                Documents.Add(document);
            }
        }

        /// <summary>`pulse` 간격이다. 전달은 1초마다라 test 는 그만큼 기다린다.</summary>
        private const float Interval = 0.05f;

        [TearDown]
        public void TearDown()
        {
            Pulse.Stop();
        }

        [Test]
        public void 전량_청은_채널이_없으면_아무것도_하지_않는다()
        {
            Assert.That(Pulse.InProgress, Is.False);

            Assert.DoesNotThrow(AffordanceBootstrap.RequestWholeReading);
            Assert.That(Pulse.InProgress, Is.False);
        }

        [Test]
        public void 다시_시작한_채널은_새_run_에서_1번부터_센다()
        {
            // Begin 이 첫 pulse 를 바로 찍고 Stop 이 그것을 전달하므로 기다리지 않는다.
            var first = new CapturingSink();
            Assert.That(Pulse.Begin(first, Interval), Is.True);
            Pulse.Stop();

            var second = new CapturingSink();
            Assert.That(Pulse.Begin(second, Interval), Is.True);
            Pulse.Stop();

            Assert.That(first.Documents, Has.Count.EqualTo(1));
            Assert.That(second.Documents, Has.Count.EqualTo(1));

            var firstRun = RunOf(first.Documents[0]);
            var secondRun = RunOf(second.Documents[0]);

            Assert.That(firstRun, Is.Not.Empty);
            Assert.That(secondRun, Is.Not.EqualTo(firstRun));

            // run 이 없으면 읽는 쪽은 같은 번호를 이미 본 reading 으로 버린다.
            Assert.That(first.Documents[0], Does.Contain("\"reading\":1,"));
            Assert.That(second.Documents[0], Does.Contain("\"reading\":1,"));
            Assert.That(second.Documents[0], Does.Contain("\"whole\":true"));
        }

        [Test]
        public void run_은_문서의_맨_앞에_실리고_문서는_여전히_JSON_객체다()
        {
            var sink = new CapturingSink();
            Pulse.Begin(sink, Interval);
            Pulse.Stop();

            Assert.That(sink.Documents[0], Does.StartWith("{\"run\":\""));
            Assert.That(sink.Documents[0], Does.Contain("\",\"schema\":"));
            Assert.That(sink.Documents[0], Does.EndWith("}"));
        }

        [UnityTest]
        public IEnumerator 전량_청은_아무것도_안_움직여도_같은_run_의_전량_reading_을_보낸다()
        {
            var sink = new CapturingSink();
            Pulse.Begin(sink, Interval);

            // 첫 전달을 기다린다. 이후 빈 scene 이라 변화가 없어 pulse 는 전달되지 않는다.
            yield return WaitForDocuments(sink, 1, 3f);
            yield return new WaitForSecondsRealtime(Interval * 4);
            var before = sink.Documents.Count;

            AffordanceBootstrap.RequestWholeReading();

            // 요청 뒤 `pulse` 간격이 한 번 지나야 찍힌다. Stop 이 남은 것을 전달한다.
            yield return new WaitForSecondsRealtime(Interval * 4);
            Pulse.Stop();

            // 요청 전 변화가 먼저 전달될 수 있으므로 위치가 아니라 요청 뒤 문서 중에서 찾는다.
            var requested = sink.Documents.Skip(before).FirstOrDefault(document => document.Contains("\"whole\":true"));
            Assert.That(requested, Is.Not.Null, "the requested whole reading never left");
            Assert.That(RunOf(requested), Is.EqualTo(RunOf(sink.Documents[0])));
            Assert.That(ReadingOf(requested), Is.GreaterThan(ReadingOf(sink.Documents[0])));
        }

        private static IEnumerator WaitForDocuments(CapturingSink sink, int count, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;

            while (sink.Documents.Count < count && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(sink.Documents.Count, Is.GreaterThanOrEqualTo(count), "no reading was delivered in time");
        }

        private static string RunOf(string document)
        {
            var match = Regex.Match(document, "\"run\":\"([0-9a-f]+)\"");
            Assert.That(match.Success, Is.True, "the reading carries no run: " + document);
            return match.Groups[1].Value;
        }

        private static long ReadingOf(string document)
        {
            var match = Regex.Match(document, "\"reading\":([0-9]+),");
            Assert.That(match.Success, Is.True);
            return long.Parse(match.Groups[1].Value);
        }
    }
}

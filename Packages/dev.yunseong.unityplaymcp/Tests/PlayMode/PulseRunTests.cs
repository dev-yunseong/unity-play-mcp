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
    /// 읽는 쪽이 reading 번호를 비교해도 되는지 알 수 있는지, 그리고 청하면 전량 reading 을 받는지 (#69).
    /// </summary>
    /// <remarks>
    /// play mode 여야 한다. <see cref="Pulse"/> 는 coroutine 으로 박자를 세고 <c>DontDestroyOnLoad</c> carrier 에 산다.
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

        /// <summary>박자 간격. 전달은 여전히 1초마다라 test 는 그만큼 기다린다.</summary>
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
            // Begin 이 coroutine 을 시작하는 순간 첫 pulse 를 찍고, Stop 이 그것을 건넨다. 기다릴 것이 없다.
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

            // 번호가 같다는 것이 이 issue 의 전부다. run 이 없으면 읽는 쪽은 이것을 이미 본 reading 으로 버린다.
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

            // 첫 전달을 기다린다. 그 뒤로는 아무것도 안 움직이는 빈 씬이라 박자가 찍은 pulse 는 전부 쥐고 만다.
            yield return WaitForDocuments(sink, 1, 3f);
            yield return new WaitForSecondsRealtime(Interval * 4);
            var before = sink.Documents.Count;

            AffordanceBootstrap.RequestWholeReading();

            // 청한 뒤 한 박자는 지나야 찍힌다. Stop 이 남은 것을 건넨다.
            yield return new WaitForSecondsRealtime(Interval * 4);
            Pulse.Stop();

            // 청하기 전에 무언가 움직였다면 그 차이가 먼저 건네질 수 있다. 그래서 자리가 아니라 청한 뒤의 문서 가운데서 찾는다.
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

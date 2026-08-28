using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using NUnit.Framework;

namespace Artel.Tests
{
    public sealed class ReadingsActionTests
    {
        [Test]
        public void ConstructingExecutor_DoesNotStartReadings()
        {
            var readings = new RecordingReadingChannel();

            CreateExecutor(readings);

            Assert.That(readings.StartCount, Is.Zero);
        }

        [Test]
        public void StartReadings_StartsTheReadingSession()
        {
            var readings = new RecordingReadingChannel();
            var result = Run(CreateExecutor(readings), "start_readings");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(readings.StartCount, Is.EqualTo(1));
        }

        [Test]
        public void StopReadings_StopsTheReadingSession()
        {
            var readings = new RecordingReadingChannel();
            var result = Run(CreateExecutor(readings), "stop_readings");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(readings.StopCount, Is.EqualTo(1));
        }

        private static ActionExecutor CreateExecutor(IReadingChannel readings)
        {
            return new ActionExecutor(
                new TargetLookup(), null, new PointerEventDispatcher(), readings: readings);
        }

        private static ActionResultDto Run(ActionExecutor executor, string method)
        {
            ActionResultDto result = null;
            Drain(executor.Execute(
                1, method, new List<object>(), completed => result = completed));
            return result;
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    Drain(nested);
                }
            }
        }

        private sealed class RecordingReadingChannel : IReadingChannel
        {
            public int StartCount { get; private set; }
            public int StopCount { get; private set; }

            public bool StartReadings()
            {
                StartCount++;
                return true;
            }

            public void StopReadings()
            {
                StopCount++;
            }
        }
    }
}

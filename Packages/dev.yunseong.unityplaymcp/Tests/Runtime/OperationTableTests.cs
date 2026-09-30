using NUnit.Framework;
using UnityPlayMcp.Play;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// 입력 소유 operation 의 lock 과 기록. Unity 객체를 쓰지 않으므로 시간을 직접 넘긴다.
    /// </summary>
    public sealed class OperationTableTests
    {
        [Test]
        public void Begin_GrantsTheInputToTheFirstOperation()
        {
            var table = new OperationTable();

            var outcome = table.Begin("op1", "hash", "clientA", 0d, 30d);

            Assert.That(outcome.Status, Is.EqualTo(BeginStatus.Acquired));
            Assert.That(table.HolderOperationId(1d), Is.EqualTo("op1"));
        }

        [Test]
        public void Begin_ReportsBusyWithTheHolderWhenAnotherOperationOwnsTheInput()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 30d);

            var outcome = table.Begin("op2", "hash", "clientB", 1d, 30d);

            Assert.That(outcome.Status, Is.EqualTo(BeginStatus.Busy));
            Assert.That(outcome.HolderOperationId, Is.EqualTo("op1"));
        }

        [Test]
        public void TryCheckInput_BlocksOtherClientsButNotTheOwner()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 30d);

            string holder;
            Assert.That(table.TryCheckInput("clientA", 1d, out holder), Is.True, "the owner's own steps must pass");
            Assert.That(table.TryCheckInput("clientB", 1d, out holder), Is.False);
            Assert.That(holder, Is.EqualTo("op1"));
        }

        [Test]
        public void End_ReleasesTheInputForOthers()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 30d);

            Assert.That(table.End("op1", "clientA"), Is.True);

            string holder;
            Assert.That(table.TryCheckInput("clientB", 1d, out holder), Is.True);
        }

        [Test]
        public void End_DoesNotLetAnotherClientEndTheOperation()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 30d);

            Assert.That(table.End("op1", "clientB"), Is.False);

            string holder;
            Assert.That(table.TryCheckInput("clientB", 1d, out holder), Is.False);
        }

        [Test]
        public void TheHoldExpiresAfterItsTtlSoADeadClientCannotBlockInputForever()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 5d);

            string holder;
            Assert.That(table.TryCheckInput("clientB", 6d, out holder), Is.True);
            Assert.That(table.HolderOperationId(6d), Is.Null);
        }

        [Test]
        public void Begin_RemembersAFinishedOperationSoARetryIsNotExecutedAgain()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 30d);
            table.End("op1", "clientA");

            var outcome = table.Begin("op1", "hash", "clientA", 2d, 30d);

            Assert.That(outcome.Status, Is.EqualTo(BeginStatus.AlreadyStarted));
            Assert.That(outcome.State, Is.EqualTo("finished"));
        }

        [Test]
        public void Begin_ReportsARunningOperationWhenTheSameIdComesAgain()
        {
            var table = new OperationTable();
            table.Begin("op1", "hash", "clientA", 0d, 30d);

            var outcome = table.Begin("op1", "hash", "clientA", 1d, 30d);

            Assert.That(outcome.Status, Is.EqualTo(BeginStatus.AlreadyStarted));
            Assert.That(outcome.State, Is.EqualTo("running"));
        }

        [Test]
        public void Records_KeepAtLeastTheNewestAndTheLastFiveMinutes()
        {
            var table = new OperationTable();
            for (var i = 0; i < OperationTable.KeepCount + 50; i++)
            {
                table.Begin("op" + i, "hash", "clientA", i * 0.01d, 1d);
                table.End("op" + i, "clientA");
            }

            Assert.That(table.RecordCount, Is.EqualTo(OperationTable.KeepCount + 50), "recent records are all kept");

            table.Begin("late", "hash", "clientA", 10000d, 1d);
            Assert.That(table.RecordCount, Is.LessThanOrEqualTo(OperationTable.KeepCount + 1), "old finished records are evicted");
        }

        [Test]
        public void Records_NeverEvictARunningOperation()
        {
            var table = new OperationTable();
            table.Begin("running", "hash", "clientA", 0d, 100000d);
            for (var i = 0; i < OperationTable.KeepCount + 10; i++)
            {
                // 점유 중이라 새 operation 은 시작되지 않지만 기록 수는 늘지 않는다.
                table.Begin("other" + i, "hash", "clientB", 1000d, 1d);
            }

            var outcome = table.Begin("running", "hash", "clientA", 20000d, 100000d);
            Assert.That(outcome.Status, Is.EqualTo(BeginStatus.AlreadyStarted));
        }
    }
}

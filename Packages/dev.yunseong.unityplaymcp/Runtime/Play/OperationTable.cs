using System.Collections.Generic;

namespace UnityPlayMcp.Play
{
    internal enum BeginStatus
    {
        Acquired,
        AlreadyStarted,
        Busy
    }

    internal readonly struct BeginOutcome
    {
        public BeginOutcome(BeginStatus status, string holderOperationId, string state)
        {
            Status = status;
            HolderOperationId = holderOperationId;
            State = state;
        }

        public BeginStatus Status { get; }

        /// <summary>Busy 일 때 입력을 쥐고 있는 operation.</summary>
        public string HolderOperationId { get; }

        /// <summary>AlreadyStarted 일 때 그 operation 의 상태(running/finished).</summary>
        public string State { get; }
    }

    /// <summary>
    /// 입력을 소유한 operation 하나를 기록하고, 다른 client 의 입력을 막는다.
    /// </summary>
    /// <remarks>
    /// MCP server 는 client 마다 프로세스가 따로이므로 client 사이의 경합은 여기서 막는다. Unity 객체를 쓰지 않아
    /// EditMode 에서 시험한다. 시간은 호출하는 쪽이 넘긴다.
    /// <para>
    /// 점유는 <c>ttlSeconds</c> 뒤 저절로 풀린다. client 가 죽어 <see cref="End"/> 를 부르지 못해도 입력이 영영
    /// 막히지 않게 한다. 기록은 최근 <see cref="KeepCount"/>개와 <see cref="KeepSeconds"/> 중 넓은 쪽을 보관하고, 끝난
    /// 것만 오래된 순서로 버린다. 같은 id 가 다시 오면 MCP server 가 결과를 잃었더라도 다시 실행하지 않게 하는 근거다.
    /// </para>
    /// </remarks>
    internal sealed class OperationTable
    {
        public const int KeepCount = 256;
        public const double KeepSeconds = 300d;

        private sealed class Record
        {
            public string OperationId;
            public string PayloadHash;
            public string ClientId;
            public double StartedAt;
            public bool Finished;
        }

        private readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
        private readonly LinkedList<string> order = new LinkedList<string>();
        private Record holder;
        private double holderExpiresAt;

        public int RecordCount { get { return records.Count; } }

        /// <summary>지금 점유 중인 operation id. 만료됐으면 null.</summary>
        public string HolderOperationId(double now)
        {
            ExpireHolder(now);
            return holder == null ? null : holder.OperationId;
        }

        public BeginOutcome Begin(
            string operationId, string payloadHash, string clientId, double now, double ttlSeconds)
        {
            ExpireHolder(now);
            Record known;
            if (records.TryGetValue(operationId, out known))
            {
                return new BeginOutcome(
                    BeginStatus.AlreadyStarted, null, known.Finished ? "finished" : "running");
            }

            if (holder != null)
            {
                return new BeginOutcome(BeginStatus.Busy, holder.OperationId, null);
            }

            var record = new Record
            {
                OperationId = operationId,
                PayloadHash = payloadHash,
                ClientId = clientId,
                StartedAt = now
            };
            records[operationId] = record;
            order.AddLast(operationId);
            holder = record;
            holderExpiresAt = now + ttlSeconds;
            Evict(now);
            return new BeginOutcome(BeginStatus.Acquired, null, null);
        }

        /// <summary>operation 을 끝내고 점유를 푼다. 다른 client 의 operation 은 끝내지 못한다.</summary>
        public bool End(string operationId, string clientId)
        {
            Record record;
            if (!records.TryGetValue(operationId, out record) || record.ClientId != clientId)
            {
                return false;
            }

            record.Finished = true;
            if (holder == record)
            {
                holder = null;
            }

            return true;
        }

        /// <summary>
        /// 이 client 가 지금 입력을 보내도 되는지 확인한다. 아니면 점유 중인 operation id 를 돌려준다.
        /// </summary>
        public bool TryCheckInput(string clientId, double now, out string busyOperationId)
        {
            ExpireHolder(now);
            busyOperationId = null;
            if (holder == null || holder.ClientId == clientId)
            {
                return true;
            }

            busyOperationId = holder.OperationId;
            return false;
        }

        private void ExpireHolder(double now)
        {
            if (holder != null && now > holderExpiresAt)
            {
                holder.Finished = true;
                holder = null;
            }
        }

        /// <summary>개수가 넘치고 시간도 지난 끝난 기록만 버린다. 진행 중인 기록은 남긴다.</summary>
        private void Evict(double now)
        {
            var node = order.First;
            while (node != null && records.Count > KeepCount)
            {
                var next = node.Next;
                var record = records[node.Value];
                if (record.Finished && now - record.StartedAt > KeepSeconds)
                {
                    records.Remove(node.Value);
                    order.Remove(node);
                }

                node = next;
            }
        }
    }
}

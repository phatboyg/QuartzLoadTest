using System;

namespace Scheduler
{
    public class MessageLatencySettings
    {
        public long MessageCount { get; init; }

        public int ConcurrencyLimit { get; init; }

        public ushort PrefetchCount { get; init; }

        public bool Durable { get; init; }

        public int Clients { get; init; }

        public TimeSpan Delay { get; init; }
    }
}
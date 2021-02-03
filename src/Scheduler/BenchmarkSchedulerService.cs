using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Hosting;

namespace Scheduler
{
    public class BenchmarkSchedulerService :
        BackgroundService
    {
        readonly Uri _address;
        readonly MessageMetricCapture _capture;
        readonly IHostApplicationLifetime _lifetime;
        readonly IMessageScheduler _scheduler;
        readonly MessageLatencySettings _settings;
        TimeSpan _consumeDuration;
        TimeSpan _sendDuration;

        public BenchmarkSchedulerService(MessageMetricCapture capture, MessageLatencySettings settings, IMessageScheduler scheduler,
            IEndpointNameFormatter endpointNameFormatter, IHostApplicationLifetime lifetime)
        {
            _capture = capture;
            _settings = settings;
            _scheduler = scheduler;
            _lifetime = lifetime;

            _address = new Uri($"exchange:{endpointNameFormatter.Consumer<MessageLatencyConsumer>()}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            await Task.Delay(1000, stoppingToken);

            Console.WriteLine("Running Message Latency Benchmark");

            await RunBenchmark();

            Console.WriteLine("Message Count: {0}", _settings.MessageCount);
            Console.WriteLine("Durable: {0}", _settings.Durable);
            Console.WriteLine("Prefetch Count: {0}", _settings.PrefetchCount);
            Console.WriteLine("Concurrency Limit: {0}", _settings.ConcurrencyLimit);

            Console.WriteLine("Total send duration: {0:g}", _sendDuration);
            Console.WriteLine("Send message rate: {0:F2} (msg/s)",
                _settings.MessageCount * 1000 / _sendDuration.TotalMilliseconds);
            Console.WriteLine("Total consume duration: {0:g}", _consumeDuration);
            Console.WriteLine("Consume message rate: {0:F2} (msg/s)",
                _settings.MessageCount * 1000 / _consumeDuration.TotalMilliseconds);
            Console.WriteLine("Concurrent Consumer Count: {0}", MessageLatencyConsumer.MaxConsumerCount);

            var messageMetrics = _capture.GetMessageMetrics();

            Console.WriteLine("Avg Ack Time: {0:F0}ms",
                messageMetrics.Average(x => x.AckLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("Min Ack Time: {0:F0}ms",
                messageMetrics.Min(x => x.AckLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("Max Ack Time: {0:F0}ms",
                messageMetrics.Max(x => x.AckLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("Med Ack Time: {0:F0}ms",
                messageMetrics.Median(x => x.AckLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("95t Ack Time: {0:F0}ms",
                messageMetrics.Percentile(x => x.AckLatency) * 1000 / Stopwatch.Frequency);

            Console.WriteLine("Avg Consume Time: {0:F0}ms",
                messageMetrics.Average(x => x.ConsumeLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("Min Consume Time: {0:F0}ms",
                messageMetrics.Min(x => x.ConsumeLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("Max Consume Time: {0:F0}ms",
                messageMetrics.Max(x => x.ConsumeLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("Med Consume Time: {0:F0}ms",
                messageMetrics.Median(x => x.ConsumeLatency) * 1000 / Stopwatch.Frequency);
            Console.WriteLine("95t Consume Time: {0:F0}ms",
                messageMetrics.Percentile(x => x.ConsumeLatency) * 1000 / Stopwatch.Frequency);

            Console.WriteLine();
            DrawResponseTimeGraph(messageMetrics, x => x.ConsumeLatency);

            _lifetime.StopApplication();
        }

        async Task RunBenchmark()
        {
            await Task.Yield();

            var stripes = new Task[_settings.Clients];

            for (var i = 0; i < _settings.Clients; i++) stripes[i] = RunStripe(_settings.MessageCount / _settings.Clients);

            await Task.WhenAll(stripes).ConfigureAwait(false);

            _sendDuration = await _capture.SendCompleted.ConfigureAwait(false);

            _consumeDuration = await _capture.ConsumeCompleted.ConfigureAwait(false);
        }

        async Task RunStripe(long messageCount)
        {
            await Task.Yield();

            for (long i = 0; i < messageCount; i++)
            {
                var messageId = NewId.NextGuid();
                var task = _scheduler.ScheduleSend(_address, _settings.Delay, new LatencyTestMessage {CorrelationId = messageId});

                await _capture.Sent(messageId, task).ConfigureAwait(false);
            }
        }

        static void DrawResponseTimeGraph(MessageMetric[] metrics, Func<MessageMetric, long> selector)
        {
            var maxTime = metrics.Max(selector);
            var minTime = metrics.Min(selector);

            const int segments = 10;

            var span = maxTime - minTime;
            var increment = span / segments;

            var histogram = (from x in metrics.Select(selector)
                let key = (x - minTime) * segments / span
                where key >= 0 && key < segments
                let groupKey = key
                group x by groupKey
                into segment
                orderby segment.Key
                select new {Value = segment.Key, Count = segment.Count()}).ToList();

            var maxCount = histogram.Max(x => x.Count);

            foreach (var item in histogram)
            {
                var barLength = item.Count * 60 / maxCount;
                Console.WriteLine("{0,5}ms {2,-60} ({1,7})", (minTime + increment * item.Value) * 1000 / Stopwatch.Frequency, item.Count,
                    new string('*', barLength));
            }
        }
    }
}
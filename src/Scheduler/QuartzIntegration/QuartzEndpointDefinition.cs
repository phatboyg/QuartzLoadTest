using GreenPipes.Partitioning;
using MassTransit;
using MassTransit.QuartzIntegration;

namespace Scheduler.QuartzIntegration
{
    public class QuartzEndpointDefinition :
        IEndpointDefinition<ScheduleMessageConsumer>,
        IEndpointDefinition<CancelScheduledMessageConsumer>
    {
        readonly int _concurrentMessageLimit;
        readonly QuartzConfiguration _configuration;

        public QuartzEndpointDefinition(QuartzConfiguration configuration)
        {
            _configuration = configuration;

            _concurrentMessageLimit = _configuration.ConcurrentMessageLimit;

            Partition = new Partitioner(_concurrentMessageLimit, new Murmur3UnsafeHashGenerator());
        }

        public IPartitioner Partition { get; }

        public virtual bool ConfigureConsumeTopology => true;

        public virtual bool IsTemporary => false;

        public virtual int? PrefetchCount => _concurrentMessageLimit;

        public virtual int? ConcurrentMessageLimit => _concurrentMessageLimit;

        string IEndpointDefinition.GetEndpointName(IEndpointNameFormatter formatter)
        {
            return _configuration.Queue;
        }

        public void Configure<T>(T configurator)
            where T : IReceiveEndpointConfigurator
        {
        }
    }
}
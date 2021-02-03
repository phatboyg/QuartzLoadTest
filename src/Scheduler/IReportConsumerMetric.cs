using System;
using System.Threading.Tasks;

namespace Scheduler
{
    public interface IReportConsumerMetric
    {
        Task Consumed<T>(Guid messageId)
            where T : class;

        Task Sent(Guid messageId, Task sendTask);
    }
}
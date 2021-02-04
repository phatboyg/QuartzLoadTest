using System;
using System.Collections.Specialized;
using MassTransit.Transports.InMemory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Impl;

namespace Scheduler.QuartzIntegration
{
    public class QuartzConfiguration
    {
        readonly IOptions<QuartzOptions> _options;
        readonly ILogger<QuartzConfiguration> _logger;

        public QuartzConfiguration(IOptions<QuartzOptions> options, IOptions<OtherOptions> otherOptions, ILogger<QuartzConfiguration> logger)
        {
            _options = options;
            _logger = logger;

            var queueName = otherOptions.Value.Scheduler ?? options.Value.Queue;
            if (string.IsNullOrWhiteSpace(queueName))
                queueName = "quartz";
            else
            {
                if (Uri.IsWellFormedUriString(queueName, UriKind.Absolute))
                    queueName = new Uri(queueName).GetQueueOrExchangeName();
            }

            Queue = queueName;
        }

        public string Queue { get; }

        public int ConcurrentMessageLimit => _options.Value.ConcurrentMessageLimit ?? 32;

        public NameValueCollection Configuration
        {
            get
            {
                var builder = SchedulerBuilder.Create("AUTO", _options.Value.InstanceName ?? "MassTransit-Scheduler");
                builder.MisfireThreshold = TimeSpan.FromSeconds(60);
                var maxConcurrency = _options.Value.ThreadCount ?? 32;
                builder.UseDefaultThreadPool(options =>
                {
                    options.MaxConcurrency = maxConcurrency;
                });
                builder.UsePersistentStore(options =>
                {
                    options.UseProperties = true;
                    options.UseJsonSerializer();
                    var connectionString = _options.Value.ConnectionString ??
                                           "Server=tcp:localhost;Database=quartznet;Persist Security Info=False;User ID=sa;Password=Quartz!DockerP4ss;Encrypt=False;TrustServerCertificate=True;";
                    options.UseSqlServer(connectionString);

                    if (_options.Value.Clustered ?? true)
                    {
                        options.UseClustering();
                    }
                });
                builder.UseTimeZoneConverter();

                var batchSize = maxConcurrency;
                builder.SetProperty(StdSchedulerFactory.PropertySchedulerMaxBatchSize, batchSize.ToString());

                var configuration = builder.Properties;

                foreach (var key in configuration.AllKeys)
                {
                    _logger.LogInformation("{Key} = {Value}", key, configuration[key]);
                }

                return configuration;
            }
        }
    }
}

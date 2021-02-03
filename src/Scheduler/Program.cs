using System;
using System.IO;
using System.Threading.Tasks;
using MassTransit;
using MassTransit.QuartzIntegration;
using MassTransit.QuartzIntegration.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz.Impl;
using Scheduler.QuartzIntegration;
using Serilog;
using Serilog.Events;

namespace Scheduler
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                await CreateHostBuilder(args).Build().RunAsync();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();

            return Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                    configHost.AddCommandLine(args);
                })
                .ConfigureServices((_, services) =>
                {
                    ConfigureMassTransit(services);
                    services.AddSingleton<IHostedService, HostedService>();
                    services.AddSingleton<IHostedService, BenchmarkSchedulerService>();
                })
                .UseSerilog();
        }

        static void ConfigureMassTransit(IServiceCollection services)
        {
            services.AddSingleton(_ => new MessageLatencySettings()
            {
                MessageCount = 1000,
                Durable = true,
                PrefetchCount = 100,
                ConcurrencyLimit = 0,
                Clients = 10,
                Delay = TimeSpan.FromSeconds(5)
            });

            services.AddSingleton<MessageMetricCapture>();
            services.AddSingleton<IReportConsumerMetric>(provider => provider.GetRequiredService<MessageMetricCapture>());

            services.AddSingleton<QuartzConfiguration>();

            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<QuartzConfiguration>();

                return new InMemorySchedulerOptions
                {
                    SchedulerFactory = new StdSchedulerFactory(options.Configuration),
                    QueueName = options.Queue
                };
            });
            services.AddSingleton<SchedulerBusObserver>();
            services.AddSingleton(provider => provider.GetRequiredService<SchedulerBusObserver>().Scheduler);

            services.AddSingleton<QuartzEndpointDefinition>();


            services.AddMassTransit(x =>
            {
                x.AddMessageScheduler(new Uri("exchange:quartz"));
                x.SetKebabCaseEndpointNameFormatter();

                x.AddConsumer<MessageLatencyConsumer>();

                x.AddConsumer<ScheduleMessageConsumer, ScheduleMessageConsumerDefinition>();
                x.AddConsumer<CancelScheduledMessageConsumer, CancelScheduledMessageConsumerDefinition>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.ConnectBusObserver(context.GetRequiredService<SchedulerBusObserver>());

                    cfg.UseMessageScheduler(new Uri("exchange:quartz"));
                    cfg.ConfigureEndpoints(context);
                });
            });
        }
    }
}
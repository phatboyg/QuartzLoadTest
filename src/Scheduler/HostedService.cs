using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MassTransit.Registration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;

namespace Scheduler
{
    class HostedService :
        IHostedService
    {
        readonly IBusDepot _busDepot;
        readonly ILogger<HostedService> _logger;

        public HostedService(IBusDepot busDepot, ILogger<HostedService> logger)
        {
            _busDepot = busDepot;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await ResetRabbitMq();

            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var registration = cancellationToken.Register(() => cancellationTokenSource.Cancel());
            try
            {
                _logger.LogInformation("Starting");

                await _busDepot.Start(cancellationTokenSource.Token);

                _logger.LogInformation("Started");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Start Faulted");
                throw;
            }
            finally
            {
                await registration.DisposeAsync();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _busDepot.Stop(cancellationToken);
        }

        async Task ResetRabbitMq()
        {
            var factory = new ConnectionFactory
            {
                AutomaticRecoveryEnabled = false,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(1),
                TopologyRecoveryEnabled = false,
                HostName = "localhost",
                Port = 5672,
                VirtualHost = "/"
            };
            using var connection = factory.CreateConnection();

            using var model = connection.CreateModel();

            await CleanupVirtualHost(model);
        }

        async Task CleanupVirtualHost(IModel model)
        {
            var cleanVirtualHostEntirely = !bool.TryParse(Environment.GetEnvironmentVariable("CI"), out var isBuildServer) || !isBuildServer;
            if (cleanVirtualHostEntirely)
            {
                var exchanges = await GetVirtualHostEntities("exchanges");
                foreach (var exchange in exchanges)
                    model.ExchangeDelete(exchange);

                var queues = await GetVirtualHostEntities("queues");
                foreach (var queue in queues)
                    model.QueueDelete(queue);
            }
        }

        async Task<IList<string>> GetVirtualHostEntities(string element)
        {
            try
            {
                using var client = new HttpClient();
                var byteArray = Encoding.ASCII.GetBytes("guest:guest");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

                var requestUri = new UriBuilder("http", "localhost", 15672, $"api/{element}").Uri;
                await using var response = await client.GetStreamAsync(requestUri);
                using var reader = new StreamReader(response);
                using var jsonReader = new JsonTextReader(reader);

                var token = await JToken.ReadFromAsync(jsonReader);

                var entities = from elements in token.Children()
                    select elements["name"].ToString();

                return entities.Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("amq.")).ToList();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }

            return new List<string>();
        }
    }
}
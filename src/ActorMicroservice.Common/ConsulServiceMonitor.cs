using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActorMicroservice.Common
{
    public class ConsulServiceMonitor : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConsulClient _consulClient;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<ConsulServiceMonitor> _logger;
        private readonly ServiceInformation _serviceInformation;
        public ConsulServiceMonitor(
            IServiceProvider serviceProvider,
            IConsulClient consulClient,
            IHostApplicationLifetime lifetime,
            ILogger<ConsulServiceMonitor> logger, ServiceInformation serviceInformation)
        {
            _serviceProvider = serviceProvider;
            _consulClient = consulClient;
            _lifetime = lifetime;
            _logger = logger;
            ArgumentNullException.ThrowIfNull(serviceInformation);
            _serviceInformation = serviceInformation;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _lifetime.ApplicationStarted.Register(() =>
            {
                _logger.LogInformation("server is ready now.");
                RegisterApplicationToConsul(cancellationToken).Wait(cancellationToken);
            });
            await Task.CompletedTask;
        }

        private async Task RegisterApplicationToConsul(CancellationToken cancellationToken)
        {
            var runningServerHost = GetRunningHostInformation().Host;
            var runningServerPort = GetRunningHostInformation().Port;
            var agentServiceInformation = new AgentServiceRegistration()
            {
                Name = _serviceInformation.ServiceName,
                ID = _serviceInformation.ServiceNameId,
                Address = runningServerHost,
                Port = runningServerPort,
                Tags = new[] { _serviceInformation.UrlSegment }
            };

            await _consulClient.Agent.ServiceDeregister(_serviceInformation.ServiceNameId, cancellationToken);
            await _consulClient.Agent.ServiceRegister(agentServiceInformation, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _consulClient.Agent.ServiceDeregister(_serviceInformation.ServiceNameId, cancellationToken);
        }

        private Uri GetRunningHostInformation()
        {
            var server = _serviceProvider.GetRequiredService<IServer>();
            var addressFeature = server.Features.Get<IServerAddressesFeature>();
            var address = addressFeature.Addresses.First();
            return new Uri(address);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActorMicroservice.Common
{
    public static class ConsulExtensions
    {
        public static IServiceCollection AddConsulClient(
            this IServiceCollection services,
            Action<ConsulClientConfiguration> options)
        {
            /*
             * CONSUL_HTTP_ADDR
             * CONSUL_HTTP_SSL
             * CONSUL_HTTP_SSL_VERIFY
             * CONSUL_HTTP_AUTH
             * CONSUL_HTTP_TOKEN
             */
            services.TryAddSingleton<IConsulClient>(sp => new ConsulClient(options));

            return services;
        }

        /// <summary>
        /// add consul service must called after <see cref="AddConsulClient"/>
        /// the propose of <see cref="AddConsulClient"/> is initializing the consul client connection
        /// so there are no need to redo the initialization again.
        /// </summary>
        /// <param name="serviceCollection"></param>
        /// <param name="serviceInformation"></param>
        /// <returns></returns>
        public static void AddConsulService(this IServiceCollection serviceCollection,
            Action<ServiceInformation> serviceInformation)
        {
            var instance = new ServiceInformation();
            serviceInformation.Invoke(instance);
            serviceCollection.AddSingleton(instance);
            serviceCollection.AddHostedService<ConsulServiceMonitor>();
        }

        public static async Task LeaveConsulAsync(this IServiceProvider service, CancellationToken cancellationToken = default)
        {
            var consulClient = service.GetRequiredService<IConsulClient>();
            var currentServiceInformation = service.GetRequiredService<ServiceInformation>();
            await consulClient.Agent.ServiceDeregister(currentServiceInformation.ServiceNameId, cancellationToken);
        }
    }
}

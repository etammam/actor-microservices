using System;
using System.Linq;
using Consul;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        public static void UserConsulServiceRegistration(this IApplicationBuilder app,
            string serviceId,
            string serviceName,
            string[] tags)
        {

            var lifetime = app.ApplicationServices.GetService<IHostApplicationLifetime>();

            // Retrieve Consul client from DI
            var consulClient = app.ApplicationServices.GetRequiredService<IConsulClient>();

            // Setup logger
            var loggingFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
            var logger = loggingFactory.CreateLogger<IApplicationBuilder>();

            // Get server IP address
            var features = app.Properties["server.Features"] as FeatureCollection;
            var addresses = features?.Get<IServerAddressesFeature>();
            var address = addresses?.Addresses.First();
            Console.WriteLine($"binned address: {string.Join(",", addresses?.Addresses!)}");

            // Register service with consul
            var uri = new Uri(address!);
            Console.WriteLine($"instance running on: {uri}");
            var registration = new AgentServiceRegistration()
            {
                ID = serviceId,
                Name = serviceName,
                Address = $"{uri.Scheme}://{uri.Host}",
                Port = uri.Port,
                Tags = tags
            };

            logger.LogInformation("Registering with Consul");
            consulClient.Agent.ServiceDeregister(registration.ID).Wait();
            consulClient.Agent.ServiceRegister(registration).Wait();

            lifetime.ApplicationStopping.Register(() =>
            {
                logger.LogInformation("De-registering from Consul");
                consulClient.Agent.ServiceDeregister(registration.ID).Wait();
            });
        }
    }
}

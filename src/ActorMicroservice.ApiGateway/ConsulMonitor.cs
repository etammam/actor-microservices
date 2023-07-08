using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.LoadBalancing;

namespace ActorMicroservice.ApiGateway
{
    public class ConsulMonitor : BackgroundService, IProxyConfigProvider
    {
        private readonly IConsulClient _consulClient;
        private readonly IConfigValidator _proxyConfigValidator;
        private readonly ILogger<ConsulMonitor> _logger;
        private volatile ConsulProxyConfig _config;
        private const int DefaultConsulPollInterval = 2;

        public ConsulMonitor(IConsulClient consulClient, IConfigValidator proxyConfigValidator, ILogger<ConsulMonitor> logger)
        {
            _consulClient = consulClient;
            _config = new ConsulProxyConfig(null, null);
            _proxyConfigValidator = proxyConfigValidator;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var serviceResult = await _consulClient.Agent.Services(stoppingToken);

                if (serviceResult.StatusCode == HttpStatusCode.OK)
                {
                    var clusters = await BuildCluster(serviceResult);
                    var routes = await BuildRoutes(serviceResult);
                    Update(routes, clusters);
                }

                await Task.Delay(TimeSpan.FromMinutes(DefaultConsulPollInterval), stoppingToken);
            }
        }

        private async Task<List<ClusterConfig>> BuildCluster(QueryResult<Dictionary<string, AgentService>> serviceResult)
        {
            var clusters = new Dictionary<string, ClusterConfig>();
            var serviceMapping = serviceResult.Response;
            var services = serviceMapping.GroupBy(x => x.Value.Service);
            foreach (var service in services)
            {
                var healthCheck = !service.Key.Contains("actor")
                    ? new ActiveHealthCheckConfig
                    {
                        Enabled = false,
                        Interval = TimeSpan.FromSeconds(10),
                        Timeout = TimeSpan.FromSeconds(10),
                        Policy = HealthCheckConstants.ActivePolicy.ConsecutiveFailures,
                        Path = "/_health"
                    }
                    : null;
                var cluster = new ClusterConfig
                {
                    ClusterId = $"{service.Key}-cluster",
                    Destinations = service.ToDictionary(_ => Guid.NewGuid().ToString("n"), destinationConfig => new DestinationConfig
                    {
                        Address = $"http://{destinationConfig.Value.Address}:{destinationConfig.Value.Port}/",
                        Health = "/_health"
                    }),
                    LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                    HealthCheck = new HealthCheckConfig()
                    {
                        Active = healthCheck
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { ConsecutiveFailuresHealthPolicyOptions.ThresholdMetadataName, "5" }
                    },
                };
                clusters.Add(service.Key, cluster);

                var clusterValidating = await _proxyConfigValidator.ValidateClusterAsync(cluster);
                if (clusterValidating.Any())
                {
                    _logger.LogError("Errors found when creating clusters for {Service}", service.Key);
                    foreach (var err in clusterValidating)
                    {
                        _logger.LogError(err, $"{service.Key} cluster validation error");
                    }
                }
            }

            return clusters.Values.Where(x => !x.ClusterId.Contains("actor")).ToList();
        }

        private async Task<List<RouteConfig>> BuildRoutes(QueryResult<Dictionary<string, AgentService>> serviceResult)
        {
            var serviceMapping = serviceResult.Response;
            var routes = new List<RouteConfig>();
            foreach (var (key, svc) in serviceMapping)
            {
                if (routes.Any(r => r.ClusterId == svc.Service)) continue;
                if (svc.Service.Contains("actor")) continue;

                var urlSegment = !svc.Service.Contains("actor")
                    ? svc.Tags[0]
                    : null;

                var transformer = !string.IsNullOrEmpty(urlSegment)
                    ? new List<IReadOnlyDictionary<string, string>>()
                    {
                        new Dictionary<string, string>()
                        {
                            { "PathRemovePrefix", urlSegment }
                        }
                    }
                    : null;

                var route = new RouteConfig
                {
                    ClusterId = $"{svc.Service}-cluster",
                    RouteId = $"{svc.Service}-{Guid.NewGuid():N}-route",
                    Match = new RouteMatch()
                    {
                        Path = !string.IsNullOrEmpty(urlSegment) ? $"/{urlSegment}/{{**catch-all}}" : null
                    },
                    Transforms = transformer
                };


                var routeErrs = await _proxyConfigValidator.ValidateRouteAsync(route);
                if (routeErrs.Any())
                {
                    _logger.LogError("Errors found when trying to generate routes for {Service}", svc.Service);
                    foreach (var err in routeErrs)
                    {
                        _logger.LogError(err, $"{svc.Service} route validation error");
                    }
                    continue;
                }
                routes.Add(route);
            }
            return routes.DistinctBy(x => x.ClusterId).ToList();
        }

        public IProxyConfig GetConfig() => _config;

        public virtual void Update(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            var oldConfig = _config;
            _config = new ConsulProxyConfig(routes, clusters);
            oldConfig.SignalChange();
        }

        private class ConsulProxyConfig : IProxyConfig
        {
            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            public IReadOnlyList<RouteConfig> Routes { get; }
            public IReadOnlyList<ClusterConfig> Clusters { get; }
            public IChangeToken ChangeToken { get; }

            public ConsulProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
            {
                Routes = routes;
                Clusters = clusters;
                ChangeToken = new CancellationChangeToken(_cts.Token);
            }

            internal void SignalChange()
            {
                _cts.Cancel();
            }
        }
    }
}

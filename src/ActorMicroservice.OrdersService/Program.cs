using System;
using System.Linq;
using ActorMicroservice.Common;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Discovery;
using Akka.Configuration;
using Consul;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, _, loggerConfiguration) => loggerConfiguration.WriteTo.Console());
builder.Logging.AddConsole();

builder.Services.AddConsulClient(options =>
{
    options.Address = new Uri("http://localhost:8500");
});
builder.Services.AddConsulService(service =>
{
    service.ServiceName = "orders-service";
    service.ServiceNameId = $"orders-service-{Guid.NewGuid():N}";
    service.UrlSegment = "orders";
});

const string actorSystemName = "orders-actor-system";
const int port = 0;
const string host = "127.0.0.1";

var config = ConfigurationFactory.ParseString($@"
            akka {{
                actor.provider = cluster
                cluster.discovery {{
                    provider = akka.cluster.discovery.consul
                    consul {{
                        listener-url = ""http://127.0.0.1:8500""
                        class = ""Akka.Cluster.Discovery.Consul.ConsulDiscoveryService, Akka.Cluster.Discovery.Consul""
                    }}
                }}
                remote {{
                    dot-netty.tcp {{
                        port = {port},
                        hostname = {host}
                    }}
                }}
                actor {{
                    debug {{
                      receive = on
                      autoreceive = on
                      lifecycle = on
                      event-stream = on
                      unhandled = on
                    }}
                }}
                log-remote-lifecycle-events = INFO
                loglevel = INFO
                loggers=[""Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog""]
                logger-formatter=""Akka.Logger.Serilog.SerilogLogMessageFormatter, Akka.Logger.Serilog""
                coordinated-shutdown {{
                    terminate-actor-system = on
                    exit-clr = on
                    run-by-actor-system-terminate = on
                }}
            }}");

var actorSystem = ActorSystem.Create(actorSystemName, config);
await ClusterDiscovery.JoinAsync(actorSystem);
var cluster = Cluster.Get(actorSystem);
builder.Services.AddSingleton(actorSystem);
builder.Services.AddSingleton(cluster);


var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/_health", () => Results.Ok());
app.MapGet("/talk-to-actor", async (IServiceProvider serviceProvider) =>
{
    var consulClient = serviceProvider.GetRequiredService<IConsulClient>();
    var customerCatalog = await consulClient.Catalog.Service("customer-actor-system");
    var customerServiceServer = customerCatalog.Response.First(); // will be bind through the YARP api gateway.
    var runningActorSystem = serviceProvider.GetService<ActorSystem>();
    var customerServiceName = customerServiceServer.ServiceName;
    var customerServiceAddress = customerServiceServer.ServiceAddress;
    var customerServicePort = customerServiceServer.ServicePort;
    var customersActor = runningActorSystem.ActorSelection($"akka.tcp://{customerServiceName}" +
                                                    $"@{customerServiceAddress}:" +
                                                    $"{customerServicePort}/user/customers-actor");

    customersActor.Tell(100);

    return Results.Ok();
});

var lifetime = app.Services.GetService<IHostApplicationLifetime>();

lifetime.ApplicationStopping.Register(() =>
{
    app.Services.LeaveConsulAsync().Wait();
    var runningActorSystem = app.Services.GetService<ActorSystem>();
    var runningActorCluster = Cluster.Get(runningActorSystem);
    runningActorSystem.Terminate().Wait();
    runningActorCluster.LeaveAsync().Wait();
});
app.Run();
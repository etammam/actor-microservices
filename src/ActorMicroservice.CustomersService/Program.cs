using System;
using ActorMicroservice.Common;
using ActorMicroservice.CustomersService;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Discovery;
using Akka.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, _, loggerConfiguration) => loggerConfiguration.WriteTo.Console());
builder.Logging.ClearProviders();


builder.Services.AddConsulClient(options =>
{
    options.Address = new Uri("http://localhost:8500");
});

builder.Services.AddConsulService(service =>
{
    service.ServiceName = "customers-service";
    service.ServiceNameId = $"customers-service-{Guid.NewGuid():N}";
    service.UrlSegment = "customers";
});

const string actorSystemName = "customer-actor-system";
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

var actorProps = Props.Create(typeof(CustomerActor));
actorSystem.ActorOf(actorProps, "customers-actor");

await ClusterDiscovery.JoinAsync(actorSystem);
var cluster = Cluster.Get(actorSystem);
builder.Services.AddSingleton(actorSystem);
builder.Services.AddSingleton(cluster);




var app = builder.Build();

app.MapGet("/", () => Results.Ok("Customers Services Working..."));
app.MapGet("/_health", () => Results.Ok());

var lifetime = app.Services.GetService<IHostApplicationLifetime>();

lifetime.ApplicationStopping.Register(() =>
{
    var runningActorSystem = app.Services.GetService<ActorSystem>();
    var runningActorCluster = Cluster.Get(runningActorSystem);
    runningActorCluster.LeaveAsync().Wait();
    runningActorSystem.Terminate().Wait();
}, false);

app.Run();
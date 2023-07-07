using System;
using ActorMicroservice.ApiGateway;
using ActorMicroservice.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConsulMonitor>();
builder.Services.AddSingleton<IProxyConfigProvider>(p => p.GetService<ConsulMonitor>());
builder.Services.AddReverseProxy();
builder.Services.AddConsulClient(options =>
{
    options.Address = new Uri("http://localhost:8500");
});

builder.Services.AddHostedService(p => p.GetService<ConsulMonitor>());

var app = builder.Build();
app.MapReverseProxy();
app.UseRouting();

app.MapGet("/", () => "Api Gateway");
app.Run();

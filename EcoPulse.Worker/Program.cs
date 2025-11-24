using EcoPulse.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using DotNetEnv;
using System.IO;
var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
Env.Load(envPath);
MetricServer server = new MetricServer(port: 9105);
server.Start();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<ForecastWorker>();
builder.Services.AddHostedService<AccuracyEvaluator>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

await builder.Build().RunAsync();

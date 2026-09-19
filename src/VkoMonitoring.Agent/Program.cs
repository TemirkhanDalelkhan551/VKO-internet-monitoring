using VkoMonitoring.Agent;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Services;
using VkoMonitoring.Agent.Infrastructure.Api;
using VkoMonitoring.Agent.Infrastructure.Diagnostics;
using VkoMonitoring.Agent.Infrastructure.Measurement;
using VkoMonitoring.Agent.Infrastructure.Outbox;
using VkoMonitoring.Agent.Logging;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
var runAsConsole = builder.Configuration.GetValue<bool>("RunAsConsole");
if (runAsConsole)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole();
}

if (WindowsServiceHelpers.IsWindowsService() && !runAsConsole)
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "VKO Internet Monitoring Agent";
    });
}

var agentOptions = builder.Configuration
    .GetRequiredSection(AgentOptions.SectionName)
    .Get<AgentOptions>()
    ?? throw new InvalidOperationException("Agent configuration is missing.");

agentOptions.MeasurementWindows = builder.Configuration
    .GetRequiredSection($"{AgentOptions.SectionName}:MeasurementWindows")
    .Get<string[]>()
    ?? [];

AgentOptionsValidator.Validate(agentOptions);

var diagnosticLogDirectory = Path.Combine(
    Environment.ExpandEnvironmentVariables(agentOptions.DataDirectory),
    "logs");
builder.Logging.AddProvider(new RollingFileLoggerProvider(new RollingTextFileWriter(
    diagnosticLogDirectory,
    agentOptions.DiagnosticLogMaxFileSizeBytes,
    agentOptions.DiagnosticLogRetentionDays,
    TimeProvider.System)));

builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DailyMeasurementSchedule>();
builder.Services.AddSingleton<IMeasurementSchedule>(provider => provider.GetRequiredService<DailyMeasurementSchedule>());
builder.Services.AddSingleton<IMeasurementOutbox, JsonFileMeasurementOutbox>();
builder.Services.AddSingleton<MeasurementCollector>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddSingleton<DispatchRetryPolicy>();
builder.Services.AddSingleton<ISystemLoadSampler, WindowsSystemLoadSampler>();
builder.Services.AddSingleton<ISystemLoadGuard, SystemLoadGuard>();

if (string.IsNullOrWhiteSpace(agentOptions.DeviceTokenFile))
{
    builder.Services.AddSingleton<IDeviceTokenProvider, ConfigurationDeviceTokenProvider>();
}
else
{
    builder.Services.AddSingleton<IDeviceTokenProvider, DpapiDeviceTokenProvider>();
}

builder.Services.AddSingleton<ILatencyProbe, IcmpLatencyProbe>();
builder.Services.AddSingleton<INetworkContextProvider, NetworkContextProvider>();
builder.Services.AddSingleton<IInternetMeasurementService, HttpInternetMeasurementService>();
builder.Services.AddSingleton<IMeasurementTrigger, FileMeasurementTrigger>();

builder.Services.AddHttpClient<IThroughputProbe, HttpThroughputProbe>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddHttpClient<IMeasurementApiClient, HttpMeasurementApiClient>(client =>
{
    client.BaseAddress = new Uri(agentOptions.ApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<IHeartbeatApiClient, HttpHeartbeatApiClient>(client =>
{
    client.BaseAddress = new Uri(agentOptions.ApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHostedService<MeasurementWorker>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();

var host = builder.Build();
host.Run();

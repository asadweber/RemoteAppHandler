using HandelApp.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "HandelApp Agent";
});

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

builder.Services.AddSingleton<AppRegistryService>();
builder.Services.AddSingleton<MultiAppManagerService>();
builder.Services.AddHostedService<TcpCommandListener>();
builder.Services.AddHostedService<DefaultInstanceStartupService>();

if (OperatingSystem.IsWindows())
    builder.Logging.AddEventLog();

var host = builder.Build();
host.Run();

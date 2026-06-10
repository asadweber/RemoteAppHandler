using HandelConsoleApp.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "HandelApp Agent";
});

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<ConsoleAppOptions>(builder.Configuration.GetSection("ConsoleApp"));

builder.Services.AddSingleton<ProcessManagerRegistry>();
builder.Services.AddSingleton<InstanceManagerService>();
builder.Services.AddHostedService<TcpCommandListener>();
builder.Services.AddHostedService<DefaultInstanceStartupService>();

if (OperatingSystem.IsWindows())
    builder.Logging.AddEventLog();

var host = builder.Build();
host.Run();

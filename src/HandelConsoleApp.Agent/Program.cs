using HandelConsoleApp.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "HandelConsoleApp Agent";
});

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<ConsoleAppOptions>(builder.Configuration.GetSection("ConsoleApp"));

builder.Services.AddSingleton<ProcessManagerService>();
builder.Services.AddHostedService<TcpCommandListener>();

if (OperatingSystem.IsWindows())
    builder.Logging.AddEventLog();

var host = builder.Build();
host.Run();

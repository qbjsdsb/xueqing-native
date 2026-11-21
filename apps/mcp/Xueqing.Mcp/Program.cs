using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Xueqing.Mcp;

var options = XueqingMcpOptions.Parse(args);

var builder = Host.CreateEmptyApplicationBuilder(settings: null);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(consoleOptions =>
{
    consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IXueqingMcpBackend>(sp =>
{
    var opt = sp.GetRequiredService<XueqingMcpOptions>();
    return opt.Mode == XueqingMcpMode.Prototype
        ? new PrototypeXueqingMcpBackend(opt.DataRoot)
        : new ProductionXueqingMcpBackend(opt);
});

builder.Services
    .AddMcpServer(serverOptions =>
    {
        serverOptions.ServerInfo = new Implementation
        {
            Name = "xueqing-mcp",
            Version = "1.0.0-rc.1",
        };
        serverOptions.ServerInstructions =
            "Xueqing MCP exposes authorized teaching context and local encrypted observation drafts. " +
            "Always read xueqing_get_observation_draft before calling xueqing_save_observation_draft with its epoch, " +
            "and hand back a canonical xueqing:// deep link so the teacher reviews and explicitly submits in the native app.";
    })
    .WithStdioServerTransport()
    .WithTools<XueqingMcpTools>();

await builder.Build().RunAsync();

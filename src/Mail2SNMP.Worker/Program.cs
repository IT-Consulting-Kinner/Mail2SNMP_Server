using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Logging;
using Mail2SNMP.Worker;
using Mail2SNMP.Worker.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Mail2SNMP Worker");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog(config =>
        SerilogConfigurator.Configure(config, builder.Configuration));

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Mail2SNMP Worker";
    });

    // Infrastructure (EF Core, services, notification channels, etc.)
    builder.Services.AddMail2SnmpInfrastructure(builder.Configuration);

    // Worker services (Quartz scheduler, bounded channel, hosted services)
    builder.Services.AddMail2SnmpWorkerServices(builder.Configuration);

    // Prometheus scrape endpoint for THIS process. Registered here rather than in
    // AddMail2SnmpWorkerServices because the Web host calls that method too in All-in-One
    // mode, where it already maps /metrics on its own HTTP server -- a second listener
    // there would be a redundant port binding, not a fix.
    builder.Services.AddHostedService<MetricsExporterService>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

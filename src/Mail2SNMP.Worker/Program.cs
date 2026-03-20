using Mail2SNMP.Infrastructure;
using Mail2SNMP.Worker;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Mail2SNMP Worker");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog(config => config
        .ReadFrom.Configuration(builder.Configuration));

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Mail2SNMP Worker";
    });

    // Infrastructure (EF Core, services, notification channels, etc.)
    builder.Services.AddMail2SnmpInfrastructure(builder.Configuration);

    // Worker services (Quartz scheduler, bounded channel, hosted services)
    builder.Services.AddMail2SnmpWorkerServices(builder.Configuration);

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

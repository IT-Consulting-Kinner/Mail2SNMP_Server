using Mail2SNMP.Models.Configuration;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Mail2SNMP.Infrastructure.Logging;

/// <summary>
/// Builds a Serilog <see cref="LoggerConfiguration"/> from the structured
/// <see cref="LoggingSettings"/> section. Falls back to reading the legacy
/// raw "Serilog" JSON section if no <c>Logging</c> section is present.
/// </summary>
public static class SerilogConfigurator
{
    /// <summary>
    /// Configures the supplied logger from either the structured "Logging" section
    /// or, as a fallback, the raw "Serilog" section in the application configuration.
    /// </summary>
    public static LoggerConfiguration Configure(LoggerConfiguration logger, IConfiguration configuration)
    {
        var loggingSection = configuration.GetSection("Logging");
        var settings = loggingSection.Get<LoggingSettings>() ?? new LoggingSettings();

        var minLevel = ParseLevel(settings.MinimumLevel);
        logger.MinimumLevel.Is(minLevel)
              .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
              .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
              .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
              .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
              .MinimumLevel.Override("Quartz", LogEventLevel.Warning)
              .Enrich.FromLogContext();

        if (settings.ConsoleEnabled)
        {
            logger.WriteTo.Console(
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
        }

        if (settings.FileEnabled && !string.IsNullOrWhiteSpace(settings.FilePath))
        {
            var path = Environment.ExpandEnvironmentVariables(settings.FilePath);
            var rollingInterval = ParseRollingInterval(settings.RollingInterval);
            var sizeLimitBytes = (long)Math.Max(1, settings.FileSizeLimitMB) * 1024 * 1024;

            logger.WriteTo.File(
                path: path,
                rollingInterval: rollingInterval,
                retainedFileCountLimit: settings.RetainedFileCountLimit,
                fileSizeLimitBytes: sizeLimitBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
        }

        return logger;
    }

    private static LogEventLevel ParseLevel(string? value)
        => Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var lvl) ? lvl : LogEventLevel.Information;

    private static RollingInterval ParseRollingInterval(string? value)
        => Enum.TryParse<RollingInterval>(value, ignoreCase: true, out var ri) ? ri : RollingInterval.Day;
}

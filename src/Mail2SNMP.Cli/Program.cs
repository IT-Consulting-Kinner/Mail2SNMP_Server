using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Cli;

public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("MAIL2SNMP_")
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMail2SnmpInfrastructure(configuration);

        // Identity services for user management commands (create-admin, reset-password)
        services.AddIdentity<AppUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
        })
        .AddEntityFrameworkStores<Mail2SnmpDbContext>()
        .AddDefaultTokenProviders();

        var sp = services.BuildServiceProvider();

        var command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "db" => await HandleDb(args.Skip(1).ToArray(), sp),
                "config" => await HandleConfig(args.Skip(1).ToArray(), sp, configuration),
                "license" => HandleLicense(args.Skip(1).ToArray(), sp),
                "user" => await HandleUser(args.Skip(1).ToArray(), sp),
                "credentials" => await HandleCredentials(args.Skip(1).ToArray(), sp),
                "worker" => await HandleWorker(args.Skip(1).ToArray(), sp),
                "add-mailbox" => await HandleAddMailbox(args.Skip(1).ToArray(), sp),
                "add-rule" => await HandleAddRule(args.Skip(1).ToArray(), sp),
                "list-jobs" => await HandleListJobs(sp),
                "test-connection" => await HandleTestConnection(args.Skip(1).ToArray(), sp),
                "dry-run" => await HandleDryRun(args.Skip(1).ToArray(), sp),
                "test-snmp" => HandleTestSnmp(args.Skip(1).ToArray()),
                "test-mail" => await HandleTestMail(args.Skip(1).ToArray(), sp),
                "deadletter" => await HandleDeadLetter(args.Skip(1).ToArray(), sp),
                "diagnostics" => await HandleDiagnostics(args.Skip(1).ToArray(), sp, configuration),
                "backup" => await HandleBackup(args.Skip(1).ToArray(), sp, configuration),
                _ => PrintUsageAndReturn()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            Console.Error.WriteLine("[ACTION] Check configuration and try again.");
            return 1;
        }
    }

    static void PrintUsage()
    {
        // Read version from assembly so the banner tracks Directory.Build.props
        // automatically on every release â€” no manual string edit required.
        var version = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";
        Console.WriteLine($"Mail2SNMP CLI v{version}\n");
        Console.WriteLine("Database:");
        Console.WriteLine("  db migrate                       Apply EF Core migrations");
        Console.WriteLine("  db rollback                      Roll back last applied migration");
        Console.WriteLine("  db status                        Check database connection and entity counts");
        Console.WriteLine("\nConfiguration:");
        Console.WriteLine("  config validate                  Validate configuration and subsystems");
        Console.WriteLine("  license show|reload              License information and reload");
        Console.WriteLine("\nUser management:");
        Console.WriteLine("  user create-admin [--email ...]  Create admin user (CLI or /setup Wizard)");
        Console.WriteLine("  user reset-password [--email ..] Reset user password");
        Console.WriteLine("  credentials reset                Reset all stored encrypted credentials");
        Console.WriteLine("\nEntity management:");
        Console.WriteLine("  add-mailbox [--name ...] [--host ...] [--port ...] [--username ...] [--folder ...]");
        Console.WriteLine("                                   Add IMAP mailbox (interactive or via flags)");
        Console.WriteLine("  add-rule [--name ...] [--field ...] [--match-type ...] [--criteria ...] [--severity ...]");
        Console.WriteLine("                                   Add email parsing rule (interactive or via flags)");
        Console.WriteLine("  list-jobs                        List all configured jobs with details");
        Console.WriteLine("\nTesting & diagnostics:");
        Console.WriteLine("  test-connection [--mailbox <id>] Test IMAP connection to a mailbox");
        Console.WriteLine("  dry-run --job <id>               Dry-run a job (evaluate rules, no notifications)");
        Console.WriteLine("  test-snmp <host> <port>          Send test SNMP v2c trap");
        Console.WriteLine("  test-mail simulate --job <id>    Simulate test mail injection");
        Console.WriteLine("\nOperations:");
        Console.WriteLine("  worker release-lease             Release all worker leases");
        Console.WriteLine("  deadletter list                  List dead letter entries");
        Console.WriteLine("  deadletter retry <id>            Retry a specific dead letter");
        Console.WriteLine("  deadletter retry-all <target>    Retry all for a webhook target");
        Console.WriteLine("  diagnostics export [path]        Export diagnostics bundle");
        Console.WriteLine("  backup export [path.zip]         Export configuration backup (ZIP with README)");
    }

    static int PrintUsageAndReturn() { PrintUsage(); return 1; }
}

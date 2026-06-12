using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mail2SNMP.Cli;

public partial class Program
{
    static Task<int> HandleConfig(string[] args, IServiceProvider sp, IConfiguration config)
    {
        var sub = args.FirstOrDefault() ?? "validate";
        if (sub == "validate")
        {
            Console.WriteLine("Validating configuration...");
            var dbProvider = config["Database:Provider"] ?? "Sqlite";
            var connStr = config["Database:ConnectionString"];
            Console.WriteLine($"  Database Provider: {dbProvider} {(string.IsNullOrEmpty(connStr) ? "MISSING" : "OK")}");

            var keyPath = config["Security:MasterKeyPath"] ?? MasterKeyProvider.GetDefaultKeyPath();
            Console.WriteLine($"  Master Key: {(File.Exists(keyPath) ? "OK" : "NOT FOUND")} ({keyPath})");

            var license = sp.GetRequiredService<ILicenseProvider>();
            Console.WriteLine($"  License: {license.Current.Edition} (Mailboxes: {license.Current.MaxMailboxes}, Jobs: {license.Current.MaxJobs})");

            var encryptor = sp.GetRequiredService<ICredentialEncryptor>();
            var testEnc = encryptor.Encrypt("test");
            Console.WriteLine($"  Encryption: {(encryptor.TryDecrypt(testEnc, out _) ? "OK" : "FAILED")}");

            Console.WriteLine("\nValidation complete.");
            return Task.FromResult(0);
        }
        Console.WriteLine("Usage: mail2snmp config validate");
        return Task.FromResult(1);
    }

    static int HandleLicense(string[] args, IServiceProvider sp)
    {
        var sub = args.FirstOrDefault() ?? "show";
        var license = sp.GetRequiredService<ILicenseProvider>();
        var info = license.Current;

        switch (sub)
        {
            case "show":
                Console.WriteLine("License Information:");
                Console.WriteLine($"  Edition:        {info.Edition}");
                Console.WriteLine($"  License ID:     {(string.IsNullOrEmpty(info.LicenseId) ? "N/A" : info.LicenseId)}");
                Console.WriteLine($"  Customer:       {(string.IsNullOrEmpty(info.CustomerName) ? "N/A" : info.CustomerName)}");
                Console.WriteLine($"  Expires:        {(info.ExpiresUtc?.ToString("O") ?? "Never")}");
                Console.WriteLine($"  Max Mailboxes:  {(info.MaxMailboxes == int.MaxValue ? "Unlimited" : info.MaxMailboxes)}");
                Console.WriteLine($"  Max Jobs:       {(info.MaxJobs == int.MaxValue ? "Unlimited" : info.MaxJobs)}");
                Console.WriteLine($"  Max Workers:    {(info.MaxWorkerInstances == int.MaxValue ? "Unlimited" : info.MaxWorkerInstances)}");
                Console.WriteLine($"  Features:       {(info.Features.Length > 0 ? string.Join(", ", info.Features) : "None")}");
                return 0;
            case "reload":
                license.ReloadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"License reloaded. Edition: {license.Current.Edition}");
                return 0;
            default:
                Console.WriteLine("Usage: mail2snmp license [show|reload]");
                return 1;
        }
    }

    static async Task<int> HandleDiagnostics(string[] args, IServiceProvider sp, IConfiguration config)
    {
        if (args.FirstOrDefault() == "export")
        {
            var outputPath = args.Length > 1 ? args[1] : $"mail2snmp-diag-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";

            Console.WriteLine("Collecting diagnostics...");

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
            var license = sp.GetRequiredService<ILicenseProvider>();
            var encryptor = sp.GetRequiredService<ICredentialEncryptor>();

            var canConnect = await db.Database.CanConnectAsync();
            var testEnc = encryptor.Encrypt("test");
            var encOk = encryptor.TryDecrypt(testEnc, out _);

            var keyPath = config["Security:MasterKeyPath"] ?? MasterKeyProvider.GetDefaultKeyPath();

            var diag = new
            {
                GeneratedUtc = DateTime.UtcNow,
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0",
                Environment = new
                {
                    MachineName = Environment.MachineName,
                    OsVersion = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    DotNetVersion = Environment.Version.ToString(),
                    Is64Bit = Environment.Is64BitProcess,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                },
                Database = new
                {
                    Provider = db.Database.ProviderName,
                    CanConnect = canConnect,
                    Mailboxes = canConnect ? await db.Mailboxes.CountAsync() : 0,
                    Rules = canConnect ? await db.Rules.CountAsync() : 0,
                    Jobs = canConnect ? await db.Jobs.CountAsync() : 0,
                    Schedules = canConnect ? await db.Schedules.CountAsync() : 0,
                    Events = canConnect ? await db.Events.CountAsync() : 0,
                    SnmpTargets = canConnect ? await db.SnmpTargets.CountAsync() : 0,
                    WebhookTargets = canConnect ? await db.WebhookTargets.CountAsync() : 0,
                    AuditEntries = canConnect ? await db.AuditEvents.CountAsync() : 0,
                    DeadLetters = canConnect ? await db.DeadLetterEntries.CountAsync() : 0,
                    ProcessedMails = canConnect ? await db.ProcessedMails.CountAsync() : 0,
                    WorkerLeases = canConnect ? await db.WorkerLeases.CountAsync() : 0
                },
                Security = new
                {
                    MasterKeyPath = keyPath,
                    MasterKeyExists = File.Exists(keyPath),
                    EncryptionStatus = encOk ? "OK" : "FAILED"
                },
                License = new
                {
                    license.Current.Edition,
                    license.Current.MaxMailboxes,
                    license.Current.MaxJobs,
                    license.Current.MaxWorkerInstances,
                    license.Current.ExpiresUtc
                }
            };

            var json = JsonSerializer.Serialize(diag, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json);

            Console.WriteLine($"Diagnostics bundle exported to: {outputPath}");
            return 0;
        }
        Console.WriteLine("Usage: mail2snmp diagnostics export [output-path]");
        return 1;
    }

    static async Task<int> HandleBackup(string[] args, IServiceProvider sp, IConfiguration config)
    {
        if (args.FirstOrDefault() == "export")
        {
            var outputPath = args.Length > 1 ? args[1] : $"mail2snmp-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";

            // Ensure the output path ends with .zip
            if (!outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                outputPath += ".zip";

            Console.WriteLine("WARNING: This backup does NOT include the master encryption key.");
            Console.WriteLine("Without it, ALL stored credentials will be PERMANENTLY LOST.");
            Console.WriteLine("Back up master.key SEPARATELY and store it securely.\n");

            Console.WriteLine("Exporting data...");

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
            var license = sp.GetRequiredService<ILicenseProvider>();

            var mailboxes = await db.Mailboxes.AsNoTracking().ToListAsync();
            var rules = await db.Rules.AsNoTracking().ToListAsync();
            var jobs = await db.Jobs.AsNoTracking()
                .Include(j => j.JobSnmpTargets)
                .Include(j => j.JobWebhookTargets)
                .ToListAsync();
            var schedules = await db.Schedules.AsNoTracking().ToListAsync();
            var snmpTargets = await db.SnmpTargets.AsNoTracking().ToListAsync();
            var webhookTargets = await db.WebhookTargets.AsNoTracking().ToListAsync();
            var maintenanceWindows = await db.MaintenanceWindows.AsNoTracking().ToListAsync();

            var backup = new
            {
                ExportedUtc = DateTime.UtcNow,
                Version = "1.0",
                Mailboxes = mailboxes,
                Rules = rules,
                Jobs = jobs,
                Schedules = schedules,
                SnmpTargets = snmpTargets,
                WebhookTargets = webhookTargets,
                MaintenanceWindows = maintenanceWindows
            };

            var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            });

            // Build README content
            var keyPath = config["Security:MasterKeyPath"] ?? MasterKeyProvider.GetDefaultKeyPath();
            var readme = new StringBuilder();
            readme.AppendLine("# Mail2SNMP Backup");
            readme.AppendLine();
            readme.AppendLine($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            readme.AppendLine($"Machine:  {Environment.MachineName}");
            readme.AppendLine($"License:  {license.Current.Edition}");
            readme.AppendLine($"Version:  {Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0"}");
            readme.AppendLine();
            readme.AppendLine("## Contents");
            readme.AppendLine();
            readme.AppendLine("| File | Description |");
            readme.AppendLine("|------|-------------|");
            readme.AppendLine("| data.json | Configuration data (mailboxes, rules, jobs, schedules, targets, maintenance windows) |");
            readme.AppendLine("| README.md | This file |");
            readme.AppendLine();
            readme.AppendLine("## Entity Counts");
            readme.AppendLine();
            readme.AppendLine($"- Mailboxes: {mailboxes.Count}");
            readme.AppendLine($"- Rules: {rules.Count}");
            readme.AppendLine($"- Jobs: {jobs.Count}");
            readme.AppendLine($"- Schedules: {schedules.Count}");
            readme.AppendLine($"- SNMP Targets: {snmpTargets.Count}");
            readme.AppendLine($"- Webhook Targets: {webhookTargets.Count}");
            readme.AppendLine($"- Maintenance Windows: {maintenanceWindows.Count}");
            readme.AppendLine();
            readme.AppendLine("## IMPORTANT: Master Key");
            readme.AppendLine();
            readme.AppendLine("This backup contains ENCRYPTED credentials (mailbox passwords, SNMP auth/priv");
            readme.AppendLine("passwords, webhook secrets). They can ONLY be decrypted with the original master key.");
            readme.AppendLine();
            readme.AppendLine($"Master key path: {keyPath}");
            readme.AppendLine();
            readme.AppendLine("Back up the master.key file SEPARATELY and store it securely.");
            readme.AppendLine("Without it, all stored credentials are permanently lost.");
            readme.AppendLine();
            readme.AppendLine("## Restore Instructions");
            readme.AppendLine();
            readme.AppendLine("1. Install Mail2SNMP on the target machine");
            readme.AppendLine("2. Copy the master.key file to the configured Security:MasterKeyPath");
            readme.AppendLine("3. Run `mail2snmp db migrate` to initialize the database schema");
            readme.AppendLine("4. Import data.json via the Web UI or API");
            readme.AppendLine("5. Verify all mailbox connections with `mail2snmp test-connection`");

            // Create ZIP archive with data.json and README.md
            using (var zipStream = new FileStream(outputPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                // Add data.json
                var dataEntry = archive.CreateEntry("data.json", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(dataEntry.Open(), Encoding.UTF8))
                    await writer.WriteAsync(json);

                // Add README.md
                var readmeEntry = archive.CreateEntry("README.md", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
                    await writer.WriteAsync(readme.ToString());
            }

            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"Backup exported to: {outputPath} ({fileInfo.Length / 1024} KB)");
            Console.WriteLine($"  Mailboxes:            {mailboxes.Count}");
            Console.WriteLine($"  Rules:                {rules.Count}");
            Console.WriteLine($"  Jobs:                 {jobs.Count}");
            Console.WriteLine($"  Schedules:            {schedules.Count}");
            Console.WriteLine($"  SNMP Targets:         {snmpTargets.Count}");
            Console.WriteLine($"  Webhook Targets:      {webhookTargets.Count}");
            Console.WriteLine($"  Maintenance Windows:  {maintenanceWindows.Count}");
            Console.WriteLine($"\nArchive contains: data.json + README.md");
            Console.WriteLine("REMINDER: Back up master.key separately!");
            return 0;
        }
        Console.WriteLine("Usage: mail2snmp backup export [output-path.zip]");
        return 1;
    }
}

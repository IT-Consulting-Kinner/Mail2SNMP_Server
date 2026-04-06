using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Cli;

public class Program
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

    static async Task<int> HandleDb(string[] args, IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
        var sub = args.FirstOrDefault() ?? "status";

        switch (sub)
        {
            case "migrate":
                Console.WriteLine("Applying EF Core migrations...");
                var pending = await db.Database.GetPendingMigrationsAsync();
                var pendingList = pending.ToList();

                if (pendingList.Count == 0)
                {
                    Console.WriteLine("Database schema is already up to date. No pending migrations.");
                }
                else
                {
                    Console.WriteLine($"  Pending migrations: {pendingList.Count}");
                    foreach (var migration in pendingList)
                        Console.WriteLine($"    - {migration}");

                    await db.Database.MigrateAsync();
                    Console.WriteLine("All migrations applied successfully.");
                }

                // List applied migrations for verification
                var applied = await db.Database.GetAppliedMigrationsAsync();
                Console.WriteLine($"  Total applied migrations: {applied.Count()}");
                return 0;
            case "rollback":
                Console.WriteLine("Rolling back last applied migration...");
                var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
                if (appliedMigrations.Count == 0)
                {
                    Console.WriteLine("No applied migrations to roll back.");
                    return 1;
                }

                // Determine the target migration (second-to-last) for rollback
                var lastMigration = appliedMigrations[^1];
                var targetMigration = appliedMigrations.Count > 1 ? appliedMigrations[^2] : "0";

                Console.WriteLine($"  Rolling back: {lastMigration}");
                Console.WriteLine($"  Target:       {(targetMigration == "0" ? "(empty database)" : targetMigration)}");
                Console.Write("Type 'CONFIRM' to proceed: ");
                if (Console.ReadLine()?.Trim() != "CONFIRM") { Console.WriteLine("Aborted."); return 1; }

                var migrator = db.Database.GetInfrastructure().GetService(typeof(Microsoft.EntityFrameworkCore.Migrations.IMigrator))
                    as Microsoft.EntityFrameworkCore.Migrations.IMigrator;
                if (migrator is null)
                {
                    Console.Error.WriteLine("Could not obtain the EF Core migrator service.");
                    return 1;
                }
                await migrator.MigrateAsync(targetMigration);

                Console.WriteLine($"Rolled back '{lastMigration}' successfully.");
                var remainingMigrations = await db.Database.GetAppliedMigrationsAsync();
                Console.WriteLine($"  Remaining applied migrations: {remainingMigrations.Count()}");
                return 0;
            case "status":
                var canConnect = await db.Database.CanConnectAsync();
                Console.WriteLine($"Database connection: {(canConnect ? "OK" : "FAILED")}");
                Console.WriteLine($"Provider: {db.Database.ProviderName}");
                if (canConnect)
                {
                    Console.WriteLine($"  Mailboxes: {await db.Mailboxes.CountAsync()}");
                    Console.WriteLine($"  Rules: {await db.Rules.CountAsync()}");
                    Console.WriteLine($"  Jobs: {await db.Jobs.CountAsync()}");
                    Console.WriteLine($"  Events: {await db.Events.CountAsync()}");
                }
                return canConnect ? 0 : 1;
            default:
                Console.WriteLine("Usage: mail2snmp db [migrate|rollback|status]");
                return 1;
        }
    }

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

    /// <summary>
    /// Handles user management commands: create-admin (CLI or Web Wizard) and reset-password.
    /// v5.8: Both CLI and Web Wizard are supported paths for admin creation.
    /// </summary>
    static async Task<int> HandleUser(string[] args, IServiceProvider sp)
    {
        var sub = args.FirstOrDefault();
        if (sub == "create-admin")
        {
            // Parse optional command-line arguments: --email <email> --display-name <name> --password <pw>
            string? email = null;
            string? displayName = null;
            string? cliPassword = null;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--email") email = args[i + 1];
                if (args[i] == "--display-name") displayName = args[i + 1];
                if (args[i] == "--password") cliPassword = args[i + 1];
            }

            // Interactive prompts for missing values
            if (string.IsNullOrWhiteSpace(email))
            {
                Console.Write("Admin email: ");
                email = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    Console.Error.WriteLine("Email is required.");
                    return 1;
                }
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                Console.Write("Display name [Administrator]: ");
                displayName = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = "Administrator";
            }

            // Read password: from --password flag or masked interactive input
            string password;
            if (!string.IsNullOrWhiteSpace(cliPassword))
            {
                password = cliPassword;
            }
            else
            {
                Console.Write("Password (min 12 chars, uppercase, lowercase, digit, special): ");
                password = ReadPasswordMasked();
                Console.WriteLine();

                if (string.IsNullOrWhiteSpace(password))
                {
                    Console.Error.WriteLine("Password is required.");
                    return 1;
                }

                Console.Write("Confirm password: ");
                var confirmPassword = ReadPasswordMasked();
                Console.WriteLine();

                if (password != confirmPassword)
                {
                    Console.Error.WriteLine("Passwords do not match.");
                    return 1;
                }
            }

            // Create the admin user via Identity
            using var scope = sp.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Ensure the Admin role exists
            if (!await roleManager.RoleExistsAsync("Admin"))
                await roleManager.CreateAsync(new IdentityRole("Admin"));

            // Check if user already exists
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                Console.Error.WriteLine($"A user with email '{email}' already exists.");
                return 1;
            }

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                EmailConfirmed = true,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                Console.Error.WriteLine("Failed to create admin user:");
                foreach (var error in createResult.Errors)
                    Console.Error.WriteLine($"  - {error.Description}");
                return 1;
            }

            var roleResult = await userManager.AddToRoleAsync(user, "Admin");
            if (!roleResult.Succeeded)
            {
                Console.Error.WriteLine("User created but failed to assign Admin role:");
                foreach (var error in roleResult.Errors)
                    Console.Error.WriteLine($"  - {error.Description}");
                return 1;
            }

            Console.WriteLine($"\nAdmin user created successfully:");
            Console.WriteLine($"  Email:        {email}");
            Console.WriteLine($"  Display Name: {displayName}");
            Console.WriteLine($"  Role:         Admin");
            Console.WriteLine("\nAlternatively, use the Web First-Run Wizard at /setup for interactive setup.");
            return 0;
        }

        if (sub == "reset-password")
        {
            // Parse --email and --password arguments
            string? email = null;
            string? cliPw = null;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--email") email = args[i + 1];
                if (args[i] == "--password") cliPw = args[i + 1];
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                Console.Write("User email: ");
                email = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    Console.Error.WriteLine("Email is required.");
                    return 1;
                }
            }

            using var scope = sp.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                Console.Error.WriteLine($"No user found with email '{email}'.");
                return 1;
            }

            string newPassword;
            if (!string.IsNullOrWhiteSpace(cliPw))
            {
                newPassword = cliPw;
            }
            else
            {
                Console.Write("New password: ");
                newPassword = ReadPasswordMasked();
                Console.WriteLine();

                Console.Write("Confirm new password: ");
                var confirmPassword = ReadPasswordMasked();
                Console.WriteLine();

                if (newPassword != confirmPassword)
                {
                    Console.Error.WriteLine("Passwords do not match.");
                    return 1;
                }
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await userManager.ResetPasswordAsync(user, token, newPassword);

            if (!resetResult.Succeeded)
            {
                Console.Error.WriteLine("Failed to reset password:");
                foreach (var error in resetResult.Errors)
                    Console.Error.WriteLine($"  - {error.Description}");
                return 1;
            }

            Console.WriteLine($"Password reset successfully for '{email}'.");
            return 0;
        }

        Console.WriteLine("Usage: mail2snmp user create-admin [--email <email>] [--display-name <name>] [--password <pw>]");
        Console.WriteLine("       mail2snmp user reset-password [--email <email>]");
        return 1;
    }

    /// <summary>
    /// Reads a password from the console with masked input (asterisks displayed instead of characters).
    /// </summary>
    private static string ReadPasswordMasked()
    {
        var password = new StringBuilder();
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.Enter)
                break;
            if (keyInfo.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                password.Append(keyInfo.KeyChar);
                Console.Write('*');
            }
        }
        return password.ToString();
    }

    static async Task<int> HandleCredentials(string[] args, IServiceProvider sp)
    {
        // G1: rotate-key — atomically re-encrypts every stored credential with a new
        // master key. Procedure:
        //   1. Load OLD key (current MASTER_KEY env var or default key file).
        //   2. Decrypt every credential in-memory using the old encryptor.
        //   3. Generate or read NEW key (--new-key-file <path>).
        //   4. Re-encrypt with the new encryptor.
        //   5. Persist all rows in a single transaction. The OLD key file is then
        //      backed up next to the new one for one rotation cycle so the operator
        //      can recover from accidents.
        if (args.FirstOrDefault() == "rotate-key")
        {
            string? newKeyPath = null;
            for (int i = 1; i < args.Length - 1; i++)
                if (args[i] == "--new-key-file") newKeyPath = args[i + 1];

            if (string.IsNullOrWhiteSpace(newKeyPath))
            {
                Console.Error.WriteLine("Usage: mail2snmp credentials rotate-key --new-key-file <path>");
                Console.Error.WriteLine("  The new key file is created if missing (random 256-bit key).");
                return 1;
            }

            Console.WriteLine("WARNING: Master key rotation re-encrypts ALL stored credentials.");
            Console.WriteLine("A full database backup is STRONGLY recommended before proceeding.");
            Console.Write("Type 'CONFIRM' to proceed: ");
            if (Console.ReadLine()?.Trim() != "CONFIRM") { Console.WriteLine("Aborted."); return 1; }

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
            var oldEncryptor = scope.ServiceProvider.GetRequiredService<Mail2SNMP.Core.Interfaces.ICredentialEncryptor>();

            // 1) Load or generate the new key
            byte[] newKey;
            if (File.Exists(newKeyPath))
            {
                newKey = await File.ReadAllBytesAsync(newKeyPath);
                if (newKey.Length != 32)
                {
                    Console.Error.WriteLine($"New key file is {newKey.Length} bytes; expected 32.");
                    return 1;
                }
                Console.WriteLine($"Using existing key from {newKeyPath}");
            }
            else
            {
                newKey = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Fill(newKey);
                var dir = Path.GetDirectoryName(newKeyPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(newKeyPath, newKey);
                Console.WriteLine($"Generated new key at {newKeyPath}");
            }

            var newLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var newEncryptor = new Mail2SNMP.Infrastructure.Security.AesGcmCredentialEncryptor(
                newKey, newLoggerFactory.CreateLogger<Mail2SNMP.Infrastructure.Security.AesGcmCredentialEncryptor>());

            // 2/3/4) Decrypt with old, re-encrypt with new, in-memory only.
            // We collect plaintexts first so a decrypt failure aborts BEFORE any write.
            var mailboxes = await db.Mailboxes.ToListAsync();
            var snmpTargets = await db.SnmpTargets.ToListAsync();
            var webhookTargets = await db.WebhookTargets.ToListAsync();

            var rewritten = 0;
            try
            {
                foreach (var m in mailboxes.Where(x => !string.IsNullOrEmpty(x.EncryptedPassword)))
                {
                    var pt = oldEncryptor.Decrypt(m.EncryptedPassword);
                    m.EncryptedPassword = newEncryptor.Encrypt(pt);
                    rewritten++;
                }
                foreach (var t in snmpTargets)
                {
                    if (!string.IsNullOrEmpty(t.EncryptedAuthPassword))
                    { t.EncryptedAuthPassword = newEncryptor.Encrypt(oldEncryptor.Decrypt(t.EncryptedAuthPassword)); rewritten++; }
                    if (!string.IsNullOrEmpty(t.EncryptedPrivPassword))
                    { t.EncryptedPrivPassword = newEncryptor.Encrypt(oldEncryptor.Decrypt(t.EncryptedPrivPassword)); rewritten++; }
                }
                foreach (var w in webhookTargets.Where(x => !string.IsNullOrEmpty(x.EncryptedSecret)))
                {
                    var pt = oldEncryptor.Decrypt(w.EncryptedSecret!);
                    w.EncryptedSecret = newEncryptor.Encrypt(pt);
                    rewritten++;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAILED while decrypting/re-encrypting: {ex.Message}");
                Console.Error.WriteLine("No database changes were saved. Aborting.");
                return 2;
            }

            // 5) Persist all changes in one transaction
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.Error.WriteLine($"DATABASE WRITE FAILED — rolled back: {ex.Message}");
                return 3;
            }

            Console.WriteLine();
            Console.WriteLine($"Rotation complete. Re-encrypted {rewritten} credentials.");
            Console.WriteLine();
            Console.WriteLine("NEXT STEPS:");
            Console.WriteLine($"  1. Update the service configuration to point at: {newKeyPath}");
            Console.WriteLine("     (set MASTER_KEY_PATH env var or update appsettings.json)");
            Console.WriteLine("  2. Restart the Mail2SNMP service");
            Console.WriteLine("  3. Verify /health/ready reports 'master-key' as Healthy");
            Console.WriteLine("  4. Once verified, securely wipe the OLD key file");
            return 0;
        }

        if (args.FirstOrDefault() == "reset")
        {
            Console.WriteLine("WARNING: This will clear ALL stored encrypted credentials.");
            Console.WriteLine("You will need to re-enter all passwords via the Web UI.");
            Console.Write("Type 'CONFIRM' to proceed: ");
            if (Console.ReadLine()?.Trim() != "CONFIRM") { Console.WriteLine("Aborted."); return 1; }

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

            // Clear mailbox passwords
            var mailboxes = await db.Mailboxes.ToListAsync();
            foreach (var m in mailboxes)
                m.EncryptedPassword = string.Empty;

            // Clear SNMP target passwords
            var snmpTargets = await db.SnmpTargets.ToListAsync();
            foreach (var t in snmpTargets)
            {
                t.EncryptedAuthPassword = null;
                t.EncryptedPrivPassword = null;
            }

            // Clear webhook secrets
            var webhookTargets = await db.WebhookTargets.ToListAsync();
            foreach (var t in webhookTargets)
                t.EncryptedSecret = null;

            await db.SaveChangesAsync();

            Console.WriteLine($"Credentials cleared:");
            Console.WriteLine($"  Mailboxes: {mailboxes.Count}");
            Console.WriteLine($"  SNMP Targets: {snmpTargets.Count}");
            Console.WriteLine($"  Webhook Targets: {webhookTargets.Count}");
            Console.WriteLine("\nRe-enter all passwords via the Web UI.");
            return 0;
        }
        Console.WriteLine("Usage:");
        Console.WriteLine("  mail2snmp credentials reset");
        Console.WriteLine("  mail2snmp credentials rotate-key --new-key-file <path>");
        return 1;
    }

    static async Task<int> HandleWorker(string[] args, IServiceProvider sp)
    {
        if (args.FirstOrDefault() == "release-lease")
        {
            using var scope = sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IWorkerLeaseService>();
            await svc.ReleaseAllLeasesAsync();
            Console.WriteLine("All worker leases released.");
            return 0;
        }
        Console.WriteLine("Usage: mail2snmp worker [release-lease|drain]");
        return 1;
    }

    /// <summary>
    /// Adds a new IMAP mailbox configuration via CLI with interactive prompts and optional flags.
    /// </summary>
    static async Task<int> HandleAddMailbox(string[] args, IServiceProvider sp)
    {
        // Parse optional command-line arguments
        string? name = null, host = null, username = null, password = null, folder = null;
        int? port = null;
        bool? useSsl = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--name": name = args[i + 1]; break;
                case "--host": host = args[i + 1]; break;
                case "--port" when int.TryParse(args[i + 1], out var p): port = p; break;
                case "--username": username = args[i + 1]; break;
                case "--password": password = args[i + 1]; break;
                case "--folder": folder = args[i + 1]; break;
                case "--no-ssl": useSsl = false; break;
            }
        }
        // Handle --no-ssl as a standalone flag (no value)
        if (args.Contains("--no-ssl")) useSsl = false;

        // Interactive prompts for missing values
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Write("Mailbox name: ");
            name = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(name)) { Console.Error.WriteLine("Name is required."); return 1; }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            Console.Write("IMAP host: ");
            host = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(host)) { Console.Error.WriteLine("Host is required."); return 1; }
        }

        if (!port.HasValue)
        {
            Console.Write("IMAP port [993]: ");
            var portInput = Console.ReadLine()?.Trim();
            port = string.IsNullOrWhiteSpace(portInput) ? 993 : int.Parse(portInput);
        }

        if (!useSsl.HasValue)
        {
            Console.Write("Use SSL? [Y/n]: ");
            var sslInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            useSsl = sslInput != "n" && sslInput != "no";
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Write("Username: ");
            username = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(username)) { Console.Error.WriteLine("Username is required."); return 1; }
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Write("Password: ");
            password = ReadPasswordMasked();
            Console.WriteLine();
            if (string.IsNullOrWhiteSpace(password)) { Console.Error.WriteLine("Password is required."); return 1; }
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            Console.Write("IMAP folder [INBOX]: ");
            folder = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(folder)) folder = "INBOX";
        }

        // Encrypt password and create mailbox
        using var scope = sp.CreateScope();
        var encryptor = scope.ServiceProvider.GetRequiredService<ICredentialEncryptor>();
        var mailboxService = scope.ServiceProvider.GetRequiredService<IMailboxService>();

        var mailbox = new Mailbox
        {
            Name = name,
            Host = host,
            Port = port.Value,
            UseSsl = useSsl.Value,
            Username = username,
            EncryptedPassword = encryptor.Encrypt(password),
            Folder = folder,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };

        var created = await mailboxService.CreateAsync(mailbox);

        Console.WriteLine($"\nMailbox created successfully:");
        Console.WriteLine($"  Id:       {created.Id}");
        Console.WriteLine($"  Name:     {created.Name}");
        Console.WriteLine($"  Host:     {created.Host}:{created.Port} (SSL: {created.UseSsl})");
        Console.WriteLine($"  Username: {created.Username}");
        Console.WriteLine($"  Folder:   {created.Folder}");
        return 0;
    }

    /// <summary>
    /// Adds a new email parsing rule via CLI with flags and interactive prompts.
    /// </summary>
    static async Task<int> HandleAddRule(string[] args, IServiceProvider sp)
    {
        string? name = null, criteria = null;
        RuleFieldType? field = null;
        RuleMatchType? matchType = null;
        Severity? severity = null;
        int? priority = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--name": name = args[i + 1]; break;
                case "--field" when Enum.TryParse<RuleFieldType>(args[i + 1], true, out var f): field = f; break;
                case "--match-type" when Enum.TryParse<RuleMatchType>(args[i + 1], true, out var m): matchType = m; break;
                case "--criteria": criteria = args[i + 1]; break;
                case "--severity" when Enum.TryParse<Severity>(args[i + 1], true, out var s): severity = s; break;
                case "--priority" when int.TryParse(args[i + 1], out var p): priority = p; break;
            }
        }

        // Interactive prompts for missing values
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Write("Rule name: ");
            name = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(name)) { Console.Error.WriteLine("Name is required."); return 1; }
        }

        if (!field.HasValue)
        {
            Console.WriteLine($"Field types: {string.Join(", ", Enum.GetNames<RuleFieldType>())}");
            Console.Write("Field [Subject]: ");
            var fieldInput = Console.ReadLine()?.Trim();
            field = string.IsNullOrWhiteSpace(fieldInput) ? RuleFieldType.Subject :
                    Enum.TryParse<RuleFieldType>(fieldInput, true, out var f) ? f : RuleFieldType.Subject;
        }

        if (!matchType.HasValue)
        {
            Console.WriteLine($"Match types: {string.Join(", ", Enum.GetNames<RuleMatchType>())}");
            Console.Write("Match type [Contains]: ");
            var matchInput = Console.ReadLine()?.Trim();
            matchType = string.IsNullOrWhiteSpace(matchInput) ? RuleMatchType.Contains :
                        Enum.TryParse<RuleMatchType>(matchInput, true, out var m) ? m : RuleMatchType.Contains;
        }

        if (string.IsNullOrWhiteSpace(criteria))
        {
            Console.Write("Criteria (pattern to match): ");
            criteria = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(criteria)) { Console.Error.WriteLine("Criteria is required."); return 1; }
        }

        if (!severity.HasValue)
        {
            Console.WriteLine($"Severity levels: {string.Join(", ", Enum.GetNames<Severity>())}");
            Console.Write("Severity [Warning]: ");
            var sevInput = Console.ReadLine()?.Trim();
            severity = string.IsNullOrWhiteSpace(sevInput) ? Severity.Warning :
                       Enum.TryParse<Severity>(sevInput, true, out var s) ? s : Severity.Warning;
        }

        if (!priority.HasValue)
        {
            Console.Write("Priority [0]: ");
            var prioInput = Console.ReadLine()?.Trim();
            priority = string.IsNullOrWhiteSpace(prioInput) ? 0 : int.Parse(prioInput);
        }

        using var scope = sp.CreateScope();
        var ruleService = scope.ServiceProvider.GetRequiredService<IRuleService>();

        var rule = new Rule
        {
            Name = name,
            Field = field.Value,
            MatchType = matchType.Value,
            Criteria = criteria,
            Severity = severity.Value,
            Priority = priority.Value,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };

        var created = await ruleService.CreateAsync(rule);

        Console.WriteLine($"\nRule created successfully:");
        Console.WriteLine($"  Id:         {created.Id}");
        Console.WriteLine($"  Name:       {created.Name}");
        Console.WriteLine($"  Field:      {created.Field}");
        Console.WriteLine($"  Match Type: {created.MatchType}");
        Console.WriteLine($"  Criteria:   {created.Criteria}");
        Console.WriteLine($"  Severity:   {created.Severity}");
        Console.WriteLine($"  Priority:   {created.Priority}");
        return 0;
    }

    /// <summary>
    /// Lists all configured polling jobs with their linked mailbox, rule, and schedules.
    /// </summary>
    static async Task<int> HandleListJobs(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var jobs = await jobService.GetAllAsync();

        if (jobs.Count == 0)
        {
            Console.WriteLine("No jobs configured.");
            return 0;
        }

        Console.WriteLine($"Jobs ({jobs.Count}):\n");
        Console.WriteLine($"  {"Id",-5} {"Name",-25} {"Mailbox",-20} {"Rule",-20} {"Channels",-15} {"Active",-7} {"Schedules"}");
        Console.WriteLine($"  {new string('-', 5)} {new string('-', 25)} {new string('-', 20)} {new string('-', 20)} {new string('-', 15)} {new string('-', 7)} {new string('-', 10)}");

        foreach (var job in jobs)
        {
            var mailboxName = job.Mailbox?.Name ?? $"(#{job.MailboxId})";
            var ruleName = job.Rule?.Name ?? $"(#{job.RuleId})";
            var scheduleCount = job.Schedules?.Count ?? 0;
            Console.WriteLine($"  {job.Id,-5} {Truncate(job.Name, 25),-25} {Truncate(mailboxName, 20),-20} {Truncate(ruleName, 20),-20} {job.Channels,-15} {(job.IsActive ? "Yes" : "No"),-7} {scheduleCount}");
        }

        Console.WriteLine($"\n  Rate limits per job: MaxEventsPerHour / MaxActiveEvents / DedupWindowMinutes");
        foreach (var job in jobs)
            Console.WriteLine($"  [{job.Id}] {job.Name}: {job.MaxEventsPerHour} / {job.MaxActiveEvents} / {job.DedupWindowMinutes}min");

        return 0;
    }

    /// <summary>
    /// Tests the IMAP connection for a specified mailbox by its ID.
    /// </summary>
    static async Task<int> HandleTestConnection(string[] args, IServiceProvider sp)
    {
        int? mailboxId = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--mailbox" && int.TryParse(args[i + 1], out var mid)) mailboxId = mid;
        }

        // Also accept plain numeric argument
        if (!mailboxId.HasValue && args.Length > 0 && int.TryParse(args[0], out var id))
            mailboxId = id;

        if (!mailboxId.HasValue)
        {
            // List available mailboxes for selection
            using var listScope = sp.CreateScope();
            var listService = listScope.ServiceProvider.GetRequiredService<IMailboxService>();
            var allMailboxes = await listService.GetAllAsync();
            if (allMailboxes.Count == 0)
            {
                Console.Error.WriteLine("No mailboxes configured. Use 'add-mailbox' first.");
                return 1;
            }
            Console.WriteLine("Available mailboxes:");
            foreach (var m in allMailboxes)
                Console.WriteLine($"  [{m.Id}] {m.Name} — {m.Host}:{m.Port}");
            Console.Write("\nMailbox ID to test: ");
            if (!int.TryParse(Console.ReadLine()?.Trim(), out var selected)) { Console.Error.WriteLine("Invalid ID."); return 1; }
            mailboxId = selected;
        }

        using var scope = sp.CreateScope();
        var mailboxService = scope.ServiceProvider.GetRequiredService<IMailboxService>();

        var mailbox = await mailboxService.GetByIdAsync(mailboxId.Value);
        if (mailbox is null)
        {
            Console.Error.WriteLine($"Mailbox {mailboxId.Value} not found.");
            return 1;
        }

        Console.WriteLine($"Testing IMAP connection to '{mailbox.Name}' ({mailbox.Host}:{mailbox.Port}, SSL: {mailbox.UseSsl})...");

        var success = await mailboxService.TestConnectionAsync(mailboxId.Value);

        if (success)
        {
            Console.WriteLine("Connection successful. IMAP server is reachable and credentials are valid.");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Connection FAILED. Check host, port, SSL, and credentials.");
            return 1;
        }
    }

    /// <summary>
    /// Executes a dry run for a job: connects to the mailbox, evaluates rules, but does NOT send notifications.
    /// </summary>
    static async Task<int> HandleDryRun(string[] args, IServiceProvider sp)
    {
        int? jobId = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--job" && int.TryParse(args[i + 1], out var jid)) jobId = jid;
        }

        // Also accept plain numeric argument
        if (!jobId.HasValue && args.Length > 0 && int.TryParse(args[0], out var id))
            jobId = id;

        if (!jobId.HasValue)
        {
            Console.Error.WriteLine("Usage: mail2snmp dry-run --job <id>");
            Console.Error.WriteLine("       mail2snmp dry-run <job-id>");
            return 1;
        }

        using var scope = sp.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

        var job = await jobService.GetByIdAsync(jobId.Value);
        if (job is null)
        {
            Console.Error.WriteLine($"Job {jobId.Value} not found.");
            return 1;
        }

        Console.WriteLine($"Executing dry run for Job [{job.Id}] '{job.Name}'...");
        Console.WriteLine($"  Mailbox: {job.Mailbox?.Name ?? $"(#{job.MailboxId})"}");
        Console.WriteLine($"  Rule:    {job.Rule?.Name ?? $"(#{job.RuleId})"}\n");

        var result = await jobService.DryRunAsync(jobId.Value);

        Console.WriteLine("Dry-run result:");
        Console.WriteLine(result);
        return 0;
    }

    /// <summary>
    /// Truncates a string to a maximum length, appending "…" if truncated.
    /// </summary>
    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    static int HandleTestSnmp(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: mail2snmp test-snmp <host> <port> [--community <string>] [--oid <enterprise-oid>]");
            return 1;
        }

        var host = args[0];
        if (!int.TryParse(args[1], out var port))
        {
            Console.Error.WriteLine($"Invalid port: {args[1]}");
            return 1;
        }

        var community = "public";
        var oid = "1.3.6.1.4.1.99999.1.1";
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--community") community = args[i + 1];
            if (args[i] == "--oid") oid = args[i + 1];
        }

        Console.WriteLine($"Sending test SNMP v2c trap to {host}:{port}...");
        Console.WriteLine($"  Community: {community}");
        Console.WriteLine($"  OID: {oid}");

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            var trapOid = new ObjectIdentifier(oid);

            var varbinds = new List<Variable>
            {
                new(new ObjectIdentifier(oid + ".1"), new OctetString("Mail2SNMP-Test")),
                new(new ObjectIdentifier(oid + ".2"), new OctetString("Test trap from Mail2SNMP CLI")),
                new(new ObjectIdentifier(oid + ".3"), new OctetString("test@mail2snmp.local")),
                new(new ObjectIdentifier(oid + ".4"), new OctetString("Info")),
                new(new ObjectIdentifier(oid + ".5"), new Integer32(1))
            };

            Messenger.SendTrapV2(0, VersionCode.V2, endpoint,
                new OctetString(community), trapOid, 0, varbinds);

            Console.WriteLine("Test trap sent successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to send trap: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> HandleTestMail(string[] args, IServiceProvider sp)
    {
        if (args.FirstOrDefault() != "simulate")
        {
            Console.WriteLine("Usage: mail2snmp test-mail simulate --job <id> --subject <text> --from <addr>");
            return 1;
        }

        int? jobId = null;
        string subject = "Test mail from Mail2SNMP CLI";
        string from = "test@mail2snmp.local";

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--job" && int.TryParse(args[i + 1], out var jid)) jobId = jid;
            if (args[i] == "--subject") subject = args[i + 1];
            if (args[i] == "--from") from = args[i + 1];
        }

        if (!jobId.HasValue)
        {
            Console.Error.WriteLine("--job <id> is required.");
            return 1;
        }

        Console.WriteLine($"Simulating test mail injection for Job {jobId.Value}...");

        using var scope = sp.CreateScope();
        var eventService = scope.ServiceProvider.GetRequiredService<IEventService>();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

        var job = await jobService.GetByIdAsync(jobId.Value);
        if (job is null)
        {
            Console.Error.WriteLine($"Job {jobId.Value} not found.");
            return 1;
        }

        var evt = new Event
        {
            JobId = job.Id,
            State = EventState.New,
            Severity = Severity.Information,
            RuleName = job.Rule?.Name ?? "CLI-Test",
            Subject = subject,
            MailFrom = from,
            MessageId = $"cli-test-{Guid.NewGuid():N}@mail2snmp.local",
            CreatedUtc = DateTime.UtcNow
        };

        evt = await eventService.CreateAsync(evt);

        Console.WriteLine($"Test event created: Id={evt.Id}, Job={job.Name}, Subject={subject}");
        Console.WriteLine("Use 'Event Replay' in the Web UI to send notifications for this event.");
        return 0;
    }

    static async Task<int> HandleDeadLetter(string[] args, IServiceProvider sp)
    {
        var sub = args.FirstOrDefault();

        if (sub == "list")
        {
            using var scope = sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDeadLetterService>();
            var entries = await svc.GetAllAsync();
            Console.WriteLine($"Dead letters: {entries.Count}");
            foreach (var e in entries)
                Console.WriteLine($"  [{e.Id}] Target={e.WebhookTargetId} Event={e.EventId} Status={e.Status} Attempts={e.AttemptCount} Error={e.LastError}");
            return 0;
        }

        if (sub == "retry" && args.Length > 1 && long.TryParse(args[1], out var entryId))
        {
            using var scope = sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDeadLetterService>();
            await svc.RetryAsync(entryId);
            Console.WriteLine($"Dead letter {entryId} queued for immediate retry.");
            return 0;
        }

        if (sub == "retry-all" && args.Length > 1 && int.TryParse(args[1], out var targetId))
        {
            using var scope = sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IDeadLetterService>();
            await svc.RetryAllAsync(targetId);
            Console.WriteLine($"All dead letters for webhook target {targetId} queued for retry.");
            return 0;
        }

        Console.WriteLine("Usage: mail2snmp deadletter [list|retry <id>|retry-all <target-id>]");
        return 1;
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

    static void PrintUsage()
    {
        Console.WriteLine("Mail2SNMP CLI v5.8\n");
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

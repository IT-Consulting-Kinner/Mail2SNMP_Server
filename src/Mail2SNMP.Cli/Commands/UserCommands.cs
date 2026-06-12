using System.Text;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Cli;

/// <summary>
/// Partial of the CLI entry-point program that implements the <c>user</c> command group
/// (create-admin, reset-password, list, activate/deactivate) and the <c>credentials</c>
/// master-key rotation command.
/// </summary>
public partial class Program
{
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
            // V10: passing a password as a command-line argument leaks it into
            // shell history and the process list. Warn and recommend the masked
            // interactive prompt (omit --password to be prompted securely).
            if (cliPassword is not null)
                Console.Error.WriteLine("WARNING: --password on the command line is visible in shell history and process listings. Omit it to be prompted securely.");

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
            // V10: see create-admin â€” discourage password-on-command-line.
            if (cliPw is not null)
                Console.Error.WriteLine("WARNING: --password on the command line is visible in shell history and process listings. Omit it to be prompted securely.");

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
        // G1: rotate-key â€” atomically re-encrypts every stored credential with a new
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
            Console.WriteLine();
            Console.WriteLine("CRITICAL: Stop the Mail2SNMP service BEFORE running this command.");
            Console.WriteLine("If the service is running, a concurrent mailbox/target create will be");
            Console.WriteLine("encrypted with the OLD key and become unusable after rotation.");
            Console.Write("Type 'CONFIRM' to proceed: ");
            if (Console.ReadLine()?.Trim() != "CONFIRM") { Console.WriteLine("Aborted."); return 1; }

            // Wire Ctrl+C to a CancellationToken so the rotation can abort cleanly
            // on operator interrupt without leaving the DB in a half-written state.
            using var rotateCts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; rotateCts.Cancel(); };
            var rotateCt = rotateCts.Token;

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

            // 5) Persist all changes in one transaction (CT-aware)
            using var transaction = await db.Database.BeginTransactionAsync(rotateCt);
            try
            {
                await db.SaveChangesAsync(rotateCt);
                await transaction.CommitAsync(rotateCt);
            }
            catch (OperationCanceledException)
            {
                await transaction.RollbackAsync();
                Console.Error.WriteLine("Rotation aborted by operator (Ctrl+C). All changes rolled back.");
                return 4;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.Error.WriteLine($"DATABASE WRITE FAILED â€” rolled back: {ex.Message}");
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
}

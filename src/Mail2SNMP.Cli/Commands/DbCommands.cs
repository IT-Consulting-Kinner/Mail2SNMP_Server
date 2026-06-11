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

public partial class Program
{
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
}

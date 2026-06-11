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
                Console.WriteLine($"  [{m.Id}] {m.Name} â€” {m.Host}:{m.Port}");
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
    /// Truncates a string to a maximum length, appending "â€¦" if truncated.
    /// </summary>
    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "â€¦";

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
}

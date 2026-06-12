using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mail2SNMP.Cli;

/// <summary>
/// Partial of the CLI entry-point program that implements the configuration-entity
/// commands: <c>add-mailbox</c>, <c>add-rule</c> and <c>list-jobs</c>.
/// </summary>
public partial class Program
{
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
        // V10: discourage mailbox password on the command line.
        if (password is not null)
            Console.Error.WriteLine("WARNING: --password on the command line is visible in shell history and process listings. Omit it to be prompted securely.");

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
}

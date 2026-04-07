using System.Text.Json;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// G5: Bulk export/import for the main configuration entities (mailboxes, rules,
/// SNMP/webhook targets, jobs, schedules, maintenance windows). Encrypted credential
/// fields are intentionally NULLED on export so a backup file does not leak secrets.
/// On import the operator must re-enter passwords via the regular UI flows.
/// </summary>
public static class BulkExportEndpoints
{
    public static void MapBulkExportEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/bulk").RequireAuthorization("Operator");

        grp.MapGet("/export", async (
            IMailboxService mb, IRuleService rl, IJobService jb,
            IScheduleService sc, ISnmpTargetService st, IWebhookTargetService wt,
            IMaintenanceWindowService mw, CancellationToken ct) =>
        {
            var bundle = new
            {
                exportedUtc = DateTime.UtcNow,
                version = 1,
                mailboxes = (await mb.GetAllAsync(ct)).Select(m => new
                {
                    m.Name, m.Host, m.Port, m.UseSsl, m.Username, m.Folder, m.IsActive
                    // EncryptedPassword intentionally omitted
                }),
                rules = (await rl.GetAllAsync(ct)).Select(r => new
                {
                    r.Name, r.Field, r.MatchType, r.Criteria, r.Severity, r.IsActive,
                    r.Priority, r.DedupWindowMinutes
                }),
                snmpTargets = (await st.GetAllAsync(ct)).Select(t => new
                {
                    t.Name, t.Host, t.Port, t.Version, t.IsActive
                    // R2 + J1: EncryptedCommunityString, EncryptedAuthPassword and
                    // EncryptedPrivPassword are intentionally omitted from the export
                    // bundle. Operators must re-enter the credentials in the new
                    // environment via the regular UI flows.
                }),
                webhookTargets = (await wt.GetAllAsync(ct)).Select(t => new
                {
                    t.Name, t.Url, t.Headers, t.PayloadTemplate, t.MaxRequestsPerMinute, t.IsActive
                    // EncryptedSecret intentionally omitted
                }),
                jobs = (await jb.GetAllAsync(ct)).Select(j => new
                {
                    j.Name, j.MailboxId, j.RuleId, j.IsActive,
                    j.MaxActiveEvents, j.MaxEventsPerHour
                }),
                schedules = (await sc.GetAllAsync(ct)).Select(s => new
                {
                    s.Name, s.JobId, s.IntervalMinutes, s.IsActive
                }),
                maintenanceWindows = (await mw.GetAllAsync(ct)).Select(w => new
                {
                    w.Name, w.StartUtc, w.EndUtc, w.Scope, w.RecurringCron, w.IsActive
                })
            };

            var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var filename = $"mail2snmp-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            return Results.File(bytes, "application/json", filename);
        });
    }
}

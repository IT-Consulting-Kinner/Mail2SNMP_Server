namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// AR-3: single registration point for the complete REST API surface.
///
/// The standalone API host and the Web host (All-in-One mode) previously each
/// maintained their own hand-written list of <c>MapXxxEndpoints()</c> calls, and
/// the two had already drifted: <c>MapBulkExportEndpoints</c> was mapped only in
/// All-in-One mode, so the bulk-export API silently did not exist on a split
/// deployment. With one shared extension the API surface cannot diverge between
/// deployment modes again.
/// </summary>
public static class ApiEndpointRegistration
{
    /// <summary>
    /// Maps every <c>/api/v1/*</c> endpoint group. Call from both hosts instead of
    /// listing the groups individually.
    /// </summary>
    /// <param name="app">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapMail2SnmpApi(this IEndpointRouteBuilder app)
    {
        app.MapMailboxEndpoints();
        app.MapRuleEndpoints();
        app.MapJobEndpoints();
        app.MapScheduleEndpoints();
        app.MapSnmpTargetEndpoints();
        app.MapWebhookTargetEndpoints();
        app.MapEventEndpoints();
        app.MapAuditEndpoints();
        app.MapMaintenanceWindowEndpoints();
        app.MapDashboardEndpoints();
        app.MapLicenseEndpoints();
        app.MapDeadLetterEndpoints();
        app.MapWorkerEndpoints();
        app.MapBulkExportEndpoints();
        return app;
    }
}

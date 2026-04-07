namespace Mail2SNMP.Web.Components.Shared;

/// <summary>
/// T3: themed confirmation dialog. The previous <c>JS.TryConfirmAsync</c> path
/// used the browser's native <c>confirm()</c> dialog which ignores the app's
/// dark theme and looks jarring. This service is a singleton-per-circuit
/// (registered Scoped) that the global <see cref="ConfirmDialog"/> host in
/// MainLayout subscribes to. Razor pages call <see cref="AskAsync"/> exactly
/// like before; the dialog renders inline with the rest of the app, respects
/// the theme and supports Esc to cancel.
///
/// The same call sites can keep using <c>JS.TryConfirmAsync</c> if they want
/// the native dialog (e.g. as a fallback when ConfirmDialog hasn't rendered
/// yet) but the recommended path is this service.
/// </summary>
public class ConfirmService
{
    /// <summary>
    /// Raised by <see cref="AskAsync"/> when a page wants to show a dialog.
    /// The <see cref="ConfirmDialog"/> host listens, displays the modal,
    /// and completes the supplied <see cref="TaskCompletionSource{Boolean}"/>
    /// with the user's choice.
    /// </summary>
    public event Func<ConfirmRequest, Task>? OnRequest;

    /// <summary>
    /// Shows a themed confirmation dialog and resolves to <c>true</c> if the
    /// user clicks the confirm button, <c>false</c> on cancel / Esc / dialog
    /// dismiss / no host listening.
    /// </summary>
    public Task<bool> AskAsync(string message, string title = "Confirm", string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool danger = true)
    {
        var handler = OnRequest;
        if (handler is null)
        {
            // No host listening — fail safe (treat as cancel) so destructive
            // actions never run on a circuit that hasn't rendered the dialog.
            return Task.FromResult(false);
        }
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var req = new ConfirmRequest(title, message, confirmLabel, cancelLabel, danger, tcs);
        _ = handler.Invoke(req);
        return tcs.Task;
    }
}

/// <summary>State envelope passed from <see cref="ConfirmService"/> to the host.</summary>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    bool Danger,
    TaskCompletionSource<bool> Result);

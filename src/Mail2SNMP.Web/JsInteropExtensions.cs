using Microsoft.JSInterop;

namespace Mail2SNMP.Web;

/// <summary>
/// P1: Helpers around <see cref="IJSRuntime"/> that swallow the
/// <see cref="JSDisconnectedException"/> Blazor Server raises when the SignalR
/// circuit is shutting down. Without this wrapper every <c>confirm()</c> dialog
/// in every Razor page would have to be wrapped in its own try/catch — and the
/// previous code did not, so a disconnect mid-dialog escalated silently and
/// left the form in a half-busy state with no user feedback.
/// </summary>
public static class JsInteropExtensions
{
    /// <summary>
    /// Shows a browser <c>confirm()</c> dialog. Returns the user's choice on
    /// success. Returns <c>false</c> when the call fails (circuit disconnected,
    /// JS interop unavailable, …) — i.e. a failure is treated as "cancel" so
    /// destructive actions never run on a torn-down page.
    /// </summary>
    public static async Task<bool> TryConfirmAsync(this IJSRuntime js, string message)
    {
        try
        {
            return await js.InvokeAsync<bool>("confirm", message);
        }
        catch (JSDisconnectedException)
        {
            // Circuit is going away — pretend the user clicked Cancel.
            return false;
        }
        catch (TaskCanceledException)
        {
            // Same intent: a teardown raced with our call.
            return false;
        }
    }
}

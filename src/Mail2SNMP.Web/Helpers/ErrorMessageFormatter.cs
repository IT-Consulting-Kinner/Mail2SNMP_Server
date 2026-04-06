using Mail2SNMP.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Web.Helpers;

/// <summary>
/// Translates common exception types into user-friendly, actionable messages.
/// Used by all CRUD pages so the UI never exposes raw stack traces or
/// database error codes to end users.
/// </summary>
public static class ErrorMessageFormatter
{
    /// <summary>
    /// Returns a friendly message for the given exception. Falls back to the
    /// inner exception's message when no specific handler matches, and finally
    /// to a generic "An unexpected error occurred" string.
    /// </summary>
    public static string ToFriendly(Exception ex, string actionDescription = "the operation")
    {
        switch (ex)
        {
            case DependencyException dep:
                // Service-level guard: the entity is still referenced.
                return dep.Message;

            case KeyNotFoundException:
                return "The requested item could not be found. It may have been deleted by another user. Please refresh the list.";

            case DbUpdateConcurrencyException:
                return "Another user modified this record while you were editing. Please refresh and try again.";

            case DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx):
                return "An item with the same name (or unique field) already exists. Please choose a different value.";

            case DbUpdateException dbEx when IsForeignKeyViolation(dbEx):
                return "The operation cannot be completed because related records prevent it. Make sure no other entities reference this item.";

            case DbUpdateException:
                return "The database refused the change. Verify the input values and try again.";

            case OperationCanceledException:
                return $"{Capitalize(actionDescription)} timed out. The server may be slow or unreachable.";

            case UnauthorizedAccessException:
                return "You do not have permission to perform this action.";

            case InvalidOperationException invOp when invOp.Message.Contains("master key", StringComparison.OrdinalIgnoreCase):
                return "Stored credentials could not be decrypted (master key mismatch). Re-enter the password and try again.";

            case InvalidOperationException invOp:
                // InvalidOperationException is often used by services for business-rule errors.
                // Their messages are usually already user-friendly.
                return invOp.Message;

            case System.Net.Sockets.SocketException sock:
                return $"Network error while {actionDescription.ToLowerInvariant()}: {sock.Message}";

            case System.Net.Http.HttpRequestException httpEx:
                return $"HTTP error while {actionDescription.ToLowerInvariant()}: {httpEx.Message}";

            default:
                // Generic fallback. We never expose the stack trace.
                return $"An unexpected error occurred while {actionDescription.ToLowerInvariant()}. Please check the server log for details.";
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Cannot insert duplicate", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

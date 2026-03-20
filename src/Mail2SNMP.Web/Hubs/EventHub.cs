using Microsoft.AspNetCore.SignalR;

namespace Mail2SNMP.Web.Hubs;

/// <summary>
/// SignalR hub for real-time event and dashboard updates.
/// Clients join groups by entity type (e.g. "events", "dashboard") to receive targeted notifications.
/// </summary>
public class EventHub : Hub
{
    /// <summary>
    /// Subscribes the calling client to updates for a specific entity group.
    /// </summary>
    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Unsubscribes the calling client from a specific entity group.
    /// </summary>
    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}

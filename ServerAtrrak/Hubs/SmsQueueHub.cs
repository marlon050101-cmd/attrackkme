using Microsoft.AspNetCore.SignalR;

namespace ServerAtrrak.Hubs
{
    public class SmsQueueHub : Hub
    {
        public const string GroupName = "sms-hub";

        public async Task JoinSmsHub()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
            await Clients.Caller.SendAsync("SmsQueueChanged", new { Reason = "Connected" });
        }

        public async Task LeaveSmsHub()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName);
        }
    }
}


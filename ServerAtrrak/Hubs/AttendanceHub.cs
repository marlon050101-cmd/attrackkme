using Microsoft.AspNetCore.SignalR;

namespace ServerAtrrak.Hubs
{
    public class AttendanceHub : Hub
    {
        public async Task JoinClassGroup(string teacherId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, teacherId);
        }

        public async Task LeaveClassGroup(string teacherId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, teacherId);
        }
    }
}

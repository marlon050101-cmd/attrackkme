using Microsoft.AspNetCore.SignalR;

namespace ServerAtrrak.Hubs
{
    public class AttendanceHub : Hub
    {
        public async Task JoinClassGroup(string teacherId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, teacherId);
            await Clients.Caller.SendAsync("ReceiveAttendanceUpdate", new { StudentName = "SYSTEM", Status = "Connected to group " + teacherId });
        }

        public async Task LeaveClassGroup(string teacherId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, teacherId);
        }

        public async Task TestConnection(string teacherId)
        {
            Console.WriteLine($"DEBUG: SignalR Hub - TestConnection requested for {teacherId}");
            
            // Send a test message only to the caller to verify the bridge
            await Clients.Caller.SendAsync("ReceiveAttendanceUpdate", new { StudentName = "TEST", Status = "SignalR Bridge active for connection " + Context.ConnectionId });
            
            // Send a test message to the entire group to verify group membership
            await Clients.Group(teacherId).SendAsync("ReceiveAttendanceUpdate", new { StudentName = "GROUP TEST", Status = "Broadcast working for group " + teacherId });
            
            Console.WriteLine($"DEBUG: SignalR Hub - Test messages sent for {teacherId}");
        }
    }
}

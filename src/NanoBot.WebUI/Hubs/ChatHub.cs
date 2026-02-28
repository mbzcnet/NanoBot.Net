using Microsoft.AspNetCore.SignalR;

namespace NanoBot.WebUI.Hubs;

public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ILogger<ChatHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string sessionId, string role, string content)
    {
        _logger.LogInformation("Received message for session {SessionId} from {Role}", sessionId, role);
        
        // 广播消息给所有连接到该会话的客户端
        await Clients.Group(sessionId).SendAsync("ReceiveMessage", role, content, DateTime.UtcNow);
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} joined session {SessionId}", Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} left session {SessionId}", Context.ConnectionId, sessionId);
    }

    public async Task StreamMessage(string sessionId, string content)
    {
        // 流式传输消息片段
        await Clients.Group(sessionId).SendAsync("ReceiveMessageChunk", content);
    }

    public async Task CompleteStream(string sessionId)
    {
        // 通知流式传输完成
        await Clients.Group(sessionId).SendAsync("StreamCompleted");
    }
}

using System;

namespace AvoPerformanceSetupAI.Models;

public enum MessageRole { User, Assistant }

public class ChatMessage
{
    public MessageRole Role { get; init; }
    public string Text { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string TimestampText => Timestamp.ToString("HH:mm:ss");
}

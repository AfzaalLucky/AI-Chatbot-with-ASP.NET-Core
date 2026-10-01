using Chatbot.Domain.Enums;

namespace Chatbot.Domain.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Metadata (populated for assistant messages)
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? StopReason { get; set; }
    public int? LatencyMs { get; set; }

    public Conversation? Conversation { get; set; }
}

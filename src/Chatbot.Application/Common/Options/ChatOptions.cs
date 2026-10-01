using System.ComponentModel.DataAnnotations;

namespace Chatbot.Application.Common.Options;

public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>Maximum characters allowed in a single user message.</summary>
    [Range(1, 100_000)]
    public int MaxMessageLength { get; set; } = 8_000;

    /// <summary>How many previous messages are sent to the AI provider as context.</summary>
    [Range(1, 500)]
    public int MaxHistoryMessages { get; set; } = 40;
}

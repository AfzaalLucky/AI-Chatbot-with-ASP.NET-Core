using Chatbot.Domain.Entities;
using Chatbot.Domain.Enums;

namespace Chatbot.Application.DTOs;

public static class Mapping
{
    public static string ToApiRole(this MessageRole role) => role switch
    {
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    public static MessageDto ToDto(this Message message) =>
        new(message.Id, message.Role.ToApiRole(), message.Content, message.CreatedAtUtc, message.Model);

    public static ConversationSummaryDto ToSummaryDto(this Conversation conversation) =>
        new(conversation.Id, conversation.Title, conversation.CreatedAtUtc, conversation.UpdatedAtUtc);

    public static ConversationDetailDto ToDetailDto(this Conversation conversation) =>
        new(conversation.Id,
            conversation.Title,
            conversation.CreatedAtUtc,
            conversation.UpdatedAtUtc,
            conversation.Messages
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Role)
                .Select(m => m.ToDto())
                .ToList());

    public static UserDto ToDto(this User user) => new(user.Id, user.Email, user.DisplayName);
}

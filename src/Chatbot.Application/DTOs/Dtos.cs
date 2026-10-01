namespace Chatbot.Application.DTOs;

// Auth
public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

// Conversations
public sealed record CreateConversationRequest(string? Title);

public sealed record ConversationSummaryDto(Guid Id, string Title, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record ConversationDetailDto(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<MessageDto> Messages);

public sealed record MessageDto(Guid Id, string Role, string Content, DateTime CreatedAtUtc, string? Model);

// Chat

/// <summary>Sends a message. When <see cref="ConversationId"/> is null a new conversation is created.</summary>
public sealed record ChatRequest(Guid? ConversationId, string Message);

public sealed record ChatResponse(
    Guid ConversationId,
    string ConversationTitle,
    MessageDto UserMessage,
    MessageDto AssistantMessage);

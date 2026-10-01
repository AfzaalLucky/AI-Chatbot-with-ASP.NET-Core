using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.DTOs;
using Chatbot.Application.Validation;
using Chatbot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Chatbot.Application.Services;

public interface IConversationService
{
    Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<ConversationDetailDto> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
    Task<ConversationSummaryDto> CreateAsync(Guid userId, CreateConversationRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}

public sealed class ConversationService : IConversationService
{
    private readonly IConversationRepository _conversations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository conversations,
        IUnitOfWork unitOfWork,
        ILogger<ConversationService> logger)
    {
        _conversations = conversations;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var conversations = await _conversations.ListForUserAsync(userId, cancellationToken);
        return conversations.Select(c => c.ToSummaryDto()).ToList();
    }

    public async Task<ConversationDetailDto> GetAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _conversations.GetForUserAsync(conversationId, userId, includeMessages: true, cancellationToken)
                           ?? throw new NotFoundException("Conversation", conversationId);
        return conversation.ToDetailDto();
    }

    public async Task<ConversationSummaryDto> CreateAsync(Guid userId, CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var title = request?.Title?.Trim();

        new Validator()
            .Require(title is null || title.Length <= Conversation.MaxTitleLength, "Title",
                $"Title must be at most {Conversation.MaxTitleLength} characters.")
            .ThrowIfInvalid();

        var conversation = new Conversation
        {
            UserId = userId,
            Title = string.IsNullOrEmpty(title) ? Conversation.DefaultTitle : title
        };

        _conversations.Add(conversation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Conversation {ConversationId} created for user {UserId}", conversation.Id, userId);
        return conversation.ToSummaryDto();
    }

    public async Task DeleteAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _conversations.GetForUserAsync(conversationId, userId, includeMessages: false, cancellationToken)
                           ?? throw new NotFoundException("Conversation", conversationId);

        _conversations.Remove(conversation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Conversation {ConversationId} deleted by user {UserId}", conversationId, userId);
    }
}

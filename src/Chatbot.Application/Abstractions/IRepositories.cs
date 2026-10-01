using Chatbot.Domain.Entities;

namespace Chatbot.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> ExistsByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    void Add(User user);
}

public interface IConversationRepository
{
    /// <summary>Returns the conversation only when it belongs to <paramref name="userId"/>.</summary>
    Task<Conversation?> GetForUserAsync(Guid id, Guid userId, bool includeMessages, CancellationToken cancellationToken);
    Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>Most recent <paramref name="take"/> messages, returned oldest-first.</summary>
    Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid conversationId, int take, CancellationToken cancellationToken);
    void Add(Conversation conversation);
    void AddMessage(Message message);
    void Remove(Conversation conversation);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

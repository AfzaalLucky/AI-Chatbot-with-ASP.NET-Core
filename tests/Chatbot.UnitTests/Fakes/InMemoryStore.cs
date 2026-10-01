using Chatbot.Application.Abstractions;
using Chatbot.Domain.Entities;

namespace Chatbot.UnitTests.Fakes;

/// <summary>Simple in-memory repositories so service tests exercise real behaviour without a database.</summary>
public sealed class InMemoryStore : IConversationRepository, IUserRepository, IUnitOfWork
{
    public List<User> Users { get; } = [];
    public List<Conversation> Conversations { get; } = [];
    public List<Message> Messages { get; } = [];
    public int SaveCount { get; private set; }

    // IUnitOfWork
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.FromResult(1);
    }

    // IConversationRepository
    public Task<Conversation?> GetForUserAsync(Guid id, Guid userId, bool includeMessages, CancellationToken cancellationToken)
    {
        var conversation = Conversations.FirstOrDefault(c => c.Id == id && c.UserId == userId);
        if (conversation is not null && includeMessages)
        {
            conversation.Messages = Messages.Where(m => m.ConversationId == id).ToList();
        }

        return Task.FromResult(conversation);
    }

    public Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Conversation>>(Conversations.Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAtUtc).ToList());

    public Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid conversationId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Message>>(Messages.Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAtUtc).Take(take).Reverse().ToList());

    public void Add(Conversation conversation) => Conversations.Add(conversation);

    public void AddMessage(Message message) => Messages.Add(message);

    public void Remove(Conversation conversation)
    {
        Conversations.Remove(conversation);
        Messages.RemoveAll(m => m.ConversationId == conversation.Id);
    }

    // IUserRepository
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail));

    public Task<bool> ExistsByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(u => u.NormalizedEmail == normalizedEmail));

    public void Add(User user) => Users.Add(user);
}

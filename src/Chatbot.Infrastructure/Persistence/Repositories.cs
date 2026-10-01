using Chatbot.Application.Abstractions;
using Chatbot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.Infrastructure.Persistence;

internal sealed class UserRepository : IUserRepository
{
    private readonly ChatbotDbContext _db;

    public UserRepository(ChatbotDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<bool> ExistsByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public void Add(User user) => _db.Users.Add(user);
}

internal sealed class ConversationRepository : IConversationRepository
{
    private readonly ChatbotDbContext _db;

    public ConversationRepository(ChatbotDbContext db) => _db = db;

    public Task<Conversation?> GetForUserAsync(Guid id, Guid userId, bool includeMessages, CancellationToken cancellationToken)
    {
        IQueryable<Conversation> query = _db.Conversations;
        if (includeMessages)
        {
            query = query.Include(c => c.Messages);
        }

        return query.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Conversations
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid conversationId, int take, CancellationToken cancellationToken)
    {
        var recent = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        recent.Reverse();
        return recent;
    }

    public void Add(Conversation conversation) => _db.Conversations.Add(conversation);

    public void AddMessage(Message message) => _db.Messages.Add(message);

    public void Remove(Conversation conversation) => _db.Conversations.Remove(conversation);
}

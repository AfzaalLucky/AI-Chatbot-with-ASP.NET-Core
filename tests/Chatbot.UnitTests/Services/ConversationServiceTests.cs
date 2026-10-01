using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.DTOs;
using Chatbot.Application.Services;
using Chatbot.Domain.Entities;
using Chatbot.Domain.Enums;
using Chatbot.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chatbot.UnitTests.Services;

public sealed class ConversationServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly InMemoryStore _store = new();

    private ConversationService CreateService() => new(_store, _store, NullLogger<ConversationService>.Instance);

    [Fact]
    public async Task Create_WithoutTitle_UsesDefault()
    {
        var created = await CreateService().CreateAsync(UserId, new CreateConversationRequest(null), CancellationToken.None);

        Assert.Equal(Conversation.DefaultTitle, created.Title);
        Assert.Equal(UserId, Assert.Single(_store.Conversations).UserId);
    }

    [Fact]
    public async Task Create_TitleTooLong_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().CreateAsync(UserId, new CreateConversationRequest(new string('x', Conversation.MaxTitleLength + 1)), CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsOnlyCurrentUsersConversations_MostRecentFirst()
    {
        _store.Conversations.Add(new Conversation { UserId = UserId, Title = "old", UpdatedAtUtc = DateTime.UtcNow.AddHours(-2) });
        _store.Conversations.Add(new Conversation { UserId = UserId, Title = "new", UpdatedAtUtc = DateTime.UtcNow });
        _store.Conversations.Add(new Conversation { UserId = Guid.NewGuid(), Title = "someone else" });

        var list = await CreateService().ListAsync(UserId, CancellationToken.None);

        Assert.Equal(["new", "old"], list.Select(c => c.Title));
    }

    [Fact]
    public async Task Get_ReturnsMessagesInChronologicalOrder()
    {
        var conversation = new Conversation { UserId = UserId };
        _store.Conversations.Add(conversation);
        var t = DateTime.UtcNow;
        _store.Messages.Add(new Message { ConversationId = conversation.Id, Role = MessageRole.Assistant, Content = "b", CreatedAtUtc = t.AddSeconds(1) });
        _store.Messages.Add(new Message { ConversationId = conversation.Id, Role = MessageRole.User, Content = "a", CreatedAtUtc = t });

        var detail = await CreateService().GetAsync(UserId, conversation.Id, CancellationToken.None);

        Assert.Equal(["a", "b"], detail.Messages.Select(m => m.Content));
        Assert.Equal(["user", "assistant"], detail.Messages.Select(m => m.Role));
    }

    [Fact]
    public async Task Get_OtherUsersConversation_ThrowsNotFound()
    {
        var conversation = new Conversation { UserId = Guid.NewGuid() };
        _store.Conversations.Add(conversation);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().GetAsync(UserId, conversation.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesConversation()
    {
        var conversation = new Conversation { UserId = UserId };
        _store.Conversations.Add(conversation);

        await CreateService().DeleteAsync(UserId, conversation.Id, CancellationToken.None);

        Assert.Empty(_store.Conversations);
    }

    [Fact]
    public async Task Delete_Missing_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().DeleteAsync(UserId, Guid.NewGuid(), CancellationToken.None));
    }
}

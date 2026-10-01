using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.Common.Options;
using Chatbot.Application.DTOs;
using Chatbot.Application.Services;
using Chatbot.Domain.Entities;
using Chatbot.Domain.Enums;
using Chatbot.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Chatbot.UnitTests.Services;

public sealed class ChatServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly InMemoryStore _store = new();
    private readonly Mock<IAiChatService> _ai = new();
    private readonly ChatOptions _options = new() { MaxMessageLength = 100, MaxHistoryMessages = 10 };

    private ChatService CreateService() =>
        new(_store, _store, _ai.Object, MsOptions.Create(_options), NullLogger<ChatService>.Instance);

    private void SetupAiReply(string reply, IReadOnlyList<AiChatMessage>? captured = null) =>
        _ai.Setup(a => a.GenerateResponseAsync(It.IsAny<IReadOnlyList<AiChatMessage>>(),
                It.IsAny<Func<string, CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<AiChatMessage> history, Func<string, CancellationToken, Task>? onDelta, CancellationToken ct) =>
            {
                if (onDelta is not null)
                {
                    foreach (var word in reply.Split(' '))
                    {
                        await onDelta(word + " ", ct);
                    }
                }

                return new AiChatResult(reply, "test-model", 10, 5, "end_turn");
            });

    [Fact]
    public async Task SendMessage_WithoutConversationId_CreatesConversationAndPersistsBothMessages()
    {
        SetupAiReply("Hi there");

        var response = await CreateService().SendMessageAsync(UserId, new ChatRequest(null, "  Hello bot  "), null, CancellationToken.None);

        var conversation = Assert.Single(_store.Conversations);
        Assert.Equal(UserId, conversation.UserId);
        Assert.Equal("Hello bot", conversation.Title);
        Assert.Equal(conversation.Id, response.ConversationId);
        Assert.Equal("Hello bot", response.UserMessage.Content);
        Assert.Equal("user", response.UserMessage.Role);
        Assert.Equal("Hi there", response.AssistantMessage.Content);
        Assert.Equal("assistant", response.AssistantMessage.Role);

        var assistant = _store.Messages.Single(m => m.Role == MessageRole.Assistant);
        Assert.Equal("test-model", assistant.Model);
        Assert.Equal(10, assistant.InputTokens);
        Assert.Equal(5, assistant.OutputTokens);
        Assert.Equal("end_turn", assistant.StopReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task SendMessage_EmptyMessage_ThrowsValidation(string message)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().SendMessageAsync(UserId, new ChatRequest(null, message), null, CancellationToken.None));

        Assert.Contains("Message", ex.Errors.Keys);
        Assert.Empty(_store.Messages);
        _ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendMessage_TooLong_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().SendMessageAsync(UserId, new ChatRequest(null, new string('a', 101)), null, CancellationToken.None));

        Assert.Empty(_store.Conversations);
    }

    [Fact]
    public async Task SendMessage_ToAnotherUsersConversation_ThrowsNotFound()
    {
        var foreign = new Conversation { UserId = Guid.NewGuid() };
        _store.Conversations.Add(foreign);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService().SendMessageAsync(UserId, new ChatRequest(foreign.Id, "hello"), null, CancellationToken.None));

        Assert.Empty(_store.Messages);
    }

    [Fact]
    public async Task SendMessage_SendsConversationHistoryToAi()
    {
        var conversation = new Conversation { UserId = UserId, Title = "Existing" };
        _store.Conversations.Add(conversation);
        var t = DateTime.UtcNow.AddMinutes(-10);
        _store.Messages.Add(new Message { ConversationId = conversation.Id, Role = MessageRole.User, Content = "first", CreatedAtUtc = t });
        _store.Messages.Add(new Message { ConversationId = conversation.Id, Role = MessageRole.Assistant, Content = "answer", CreatedAtUtc = t.AddSeconds(1) });

        IReadOnlyList<AiChatMessage>? sent = null;
        _ai.Setup(a => a.GenerateResponseAsync(It.IsAny<IReadOnlyList<AiChatMessage>>(),
                It.IsAny<Func<string, CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<AiChatMessage> h, Func<string, CancellationToken, Task>? _, CancellationToken _) => sent = h)
            .ReturnsAsync(new AiChatResult("ok", "m", null, null, "end_turn"));

        await CreateService().SendMessageAsync(UserId, new ChatRequest(conversation.Id, "second"), null, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Collection(sent,
            m => Assert.Equal(new AiChatMessage(MessageRole.User, "first"), m),
            m => Assert.Equal(new AiChatMessage(MessageRole.Assistant, "answer"), m),
            m => Assert.Equal(new AiChatMessage(MessageRole.User, "second"), m));
        Assert.Equal("Existing", conversation.Title);
    }

    [Fact]
    public async Task SendMessage_MergesConsecutiveUserTurnsAndDropsLeadingAssistant()
    {
        var conversation = new Conversation { UserId = UserId };
        _store.Conversations.Add(conversation);
        var t = DateTime.UtcNow.AddMinutes(-10);
        _store.Messages.Add(new Message { ConversationId = conversation.Id, Role = MessageRole.Assistant, Content = "orphan", CreatedAtUtc = t });
        _store.Messages.Add(new Message { ConversationId = conversation.Id, Role = MessageRole.User, Content = "unanswered", CreatedAtUtc = t.AddSeconds(1) });

        IReadOnlyList<AiChatMessage>? sent = null;
        _ai.Setup(a => a.GenerateResponseAsync(It.IsAny<IReadOnlyList<AiChatMessage>>(),
                It.IsAny<Func<string, CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<AiChatMessage> h, Func<string, CancellationToken, Task>? _, CancellationToken _) => sent = h)
            .ReturnsAsync(new AiChatResult("ok", "m", null, null, "end_turn"));

        await CreateService().SendMessageAsync(UserId, new ChatRequest(conversation.Id, "retry"), null, CancellationToken.None);

        var only = Assert.Single(sent!);
        Assert.Equal(MessageRole.User, only.Role);
        Assert.Equal("unanswered\n\nretry", only.Content);
    }

    [Fact]
    public async Task SendMessage_WhenAiFails_KeepsUserMessageAndRethrows()
    {
        _ai.Setup(a => a.GenerateResponseAsync(It.IsAny<IReadOnlyList<AiChatMessage>>(),
                It.IsAny<Func<string, CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiServiceException("down", isTransient: true));

        var ex = await Assert.ThrowsAsync<AiServiceException>(() =>
            CreateService().SendMessageAsync(UserId, new ChatRequest(null, "hello"), null, CancellationToken.None));

        Assert.True(ex.IsTransient);
        var saved = Assert.Single(_store.Messages);
        Assert.Equal(MessageRole.User, saved.Role);
    }

    [Fact]
    public async Task SendMessage_StreamsToObserver()
    {
        SetupAiReply("one two three");
        var observer = new RecordingObserver();

        var response = await CreateService().SendMessageAsync(UserId, new ChatRequest(null, "count"), observer, CancellationToken.None);

        Assert.Equal(response.ConversationId, observer.SavedConversationId);
        Assert.Equal("count", observer.SavedMessage?.Content);
        Assert.Equal(["one ", "two ", "three "], observer.Deltas);
    }

    [Fact]
    public async Task SendMessage_CancelledMidStream_SavesPartialReply()
    {
        using var cts = new CancellationTokenSource();
        _ai.Setup(a => a.GenerateResponseAsync(It.IsAny<IReadOnlyList<AiChatMessage>>(),
                It.IsAny<Func<string, CancellationToken, Task>?>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<AiChatMessage> _, Func<string, CancellationToken, Task>? onDelta, CancellationToken ct) =>
            {
                await onDelta!("partial answer", ct);
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return new AiChatResult("never", "m", null, null, null);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService().SendMessageAsync(UserId, new ChatRequest(null, "hi"), null, cts.Token));

        var assistant = _store.Messages.Single(m => m.Role == MessageRole.Assistant);
        Assert.Equal("partial answer", assistant.Content);
        Assert.Equal("cancelled", assistant.StopReason);
    }

    private sealed class RecordingObserver : IChatStreamObserver
    {
        public Guid? SavedConversationId { get; private set; }
        public MessageDto? SavedMessage { get; private set; }
        public List<string> Deltas { get; } = [];

        public Task OnUserMessageSavedAsync(Guid conversationId, string conversationTitle, MessageDto userMessage, CancellationToken cancellationToken)
        {
            SavedConversationId = conversationId;
            SavedMessage = userMessage;
            return Task.CompletedTask;
        }

        public Task OnDeltaAsync(Guid conversationId, string text, CancellationToken cancellationToken)
        {
            Deltas.Add(text);
            return Task.CompletedTask;
        }
    }
}

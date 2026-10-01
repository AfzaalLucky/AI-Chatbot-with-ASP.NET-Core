using System.Net;
using System.Net.Http.Json;
using Chatbot.Application.DTOs;
using Chatbot.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.IntegrationTests;

public sealed class ConversationTests : IClassFixture<ChatbotApiFactory>
{
    private readonly ChatbotApiFactory _factory;

    public ConversationTests(ChatbotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Get_List_Delete_RoundTrip()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest("Trip planning"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ConversationSummaryDto>();
        Assert.Equal("Trip planning", created!.Title);
        Assert.EndsWith($"/api/conversations/{created.Id}", createResponse.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var detail = await client.GetFromJsonAsync<ConversationDetailDto>($"/api/conversations/{created.Id}");
        Assert.Equal(created.Id, detail!.Id);
        Assert.Empty(detail.Messages);

        var list = await client.GetFromJsonAsync<List<ConversationSummaryDto>>("/api/conversations");
        Assert.Contains(list!, c => c.Id == created.Id);

        var deleteResponse = await client.DeleteAsync($"/api/conversations/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await client.GetAsync($"/api/conversations/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Create_WithEmptyBody_UsesDefaultTitle()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/conversations", new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ConversationSummaryDto>();
        Assert.Equal("New conversation", created!.Title);
    }

    [Fact]
    public async Task Create_TitleTooLong_ReturnsValidationProblem()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/conversations", new CreateConversationRequest(new string('t', 201)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Title", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Users_CannotSeeOrDeleteEachOthersConversations()
    {
        var (owner, _) = await _factory.CreateAuthenticatedClientAsync();
        var (intruder, _) = await _factory.CreateAuthenticatedClientAsync();

        var created = await (await owner.PostAsJsonAsync("/api/conversations", new CreateConversationRequest("Private")))
            .Content.ReadFromJsonAsync<ConversationSummaryDto>();

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/conversations/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/api/conversations/{created.Id}")).StatusCode);

        var intruderList = await intruder.GetFromJsonAsync<List<ConversationSummaryDto>>("/api/conversations");
        Assert.DoesNotContain(intruderList!, c => c.Id == created.Id);

        // Still intact for the owner.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/conversations/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsProblemDetails404()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/conversations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(404, problem!.Status);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }
}

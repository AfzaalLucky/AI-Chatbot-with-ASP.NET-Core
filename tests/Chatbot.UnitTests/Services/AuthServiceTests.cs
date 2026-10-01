using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.DTOs;
using Chatbot.Application.Services;
using Chatbot.Domain.Entities;
using Chatbot.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Chatbot.UnitTests.Services;

public sealed class AuthServiceTests
{
    private readonly InMemoryStore _store = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly FakeHasher _hasher = new();

    public AuthServiceTests()
    {
        _tokens.Setup(t => t.CreateToken(It.IsAny<User>()))
            .Returns((User u) => new AccessToken("token-" + u.Id, DateTime.UtcNow.AddHours(1)));
    }

    private AuthService CreateService() => new(_store, _store, _hasher, _tokens.Object, NullLogger<AuthService>.Instance);

    [Fact]
    public async Task Register_ValidRequest_CreatesUserAndReturnsToken()
    {
        var response = await CreateService().RegisterAsync(new RegisterRequest(" Alice@Example.com ", "passw0rd!", null), CancellationToken.None);

        var user = Assert.Single(_store.Users);
        Assert.Equal("Alice@Example.com", user.Email);
        Assert.Equal("ALICE@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal("Alice", user.DisplayName);
        Assert.Equal("hashed:passw0rd!", user.PasswordHash);
        Assert.Equal("token-" + user.Id, response.AccessToken);
    }

    [Theory]
    [InlineData("not-an-email", "passw0rd!", "Email")]
    [InlineData("a@b.com", "short1", "Password")]
    [InlineData("a@b.com", "lettersonly", "Password")]
    [InlineData("a@b.com", "12345678", "Password")]
    public async Task Register_InvalidInput_ThrowsValidation(string email, string password, string field)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().RegisterAsync(new RegisterRequest(email, password, null), CancellationToken.None));

        Assert.Contains(field, ex.Errors.Keys);
        Assert.Empty(_store.Users);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ThrowsConflict()
    {
        var service = CreateService();
        await service.RegisterAsync(new RegisterRequest("bob@example.com", "passw0rd!", "Bob"), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegisterAsync(new RegisterRequest("BOB@example.com", "passw0rd!", "Bob 2"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsAuthenticationFailed()
    {
        var service = CreateService();
        await service.RegisterAsync(new RegisterRequest("carol@example.com", "passw0rd!", null), CancellationToken.None);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.LoginAsync(new LoginRequest("carol@example.com", "wrong-pass1"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_UnknownUser_ThrowsAuthenticationFailed()
    {
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            CreateService().LoginAsync(new LoginRequest("nobody@example.com", "passw0rd!"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_CorrectCredentials_IsCaseInsensitiveOnEmail()
    {
        var service = CreateService();
        await service.RegisterAsync(new RegisterRequest("dave@example.com", "passw0rd!", null), CancellationToken.None);

        var response = await service.LoginAsync(new LoginRequest("DAVE@EXAMPLE.COM", "passw0rd!"), CancellationToken.None);

        Assert.Equal("dave@example.com", response.User.Email);
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(User user, string password) => "hashed:" + password;
        public bool Verify(User user, string hashedPassword, string providedPassword) => hashedPassword == "hashed:" + providedPassword;
    }
}

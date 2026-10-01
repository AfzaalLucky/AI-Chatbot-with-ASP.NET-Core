using Chatbot.Domain.Entities;

namespace Chatbot.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string hashedPassword, string providedPassword);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateToken(User user);
}

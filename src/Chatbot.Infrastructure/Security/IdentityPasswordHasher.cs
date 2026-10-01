using Chatbot.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using IPasswordHasher = Chatbot.Application.Abstractions.IPasswordHasher;

namespace Chatbot.Infrastructure.Security;

/// <summary>Delegates to ASP.NET Core Identity's PBKDF2 password hasher (versioned, salted, iterated).</summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public bool Verify(User user, string hashedPassword, string providedPassword) =>
        _inner.VerifyHashedPassword(user, hashedPassword, providedPassword) is
            PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}

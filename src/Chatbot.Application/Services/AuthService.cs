using Chatbot.Application.Abstractions;
using Chatbot.Application.Common.Exceptions;
using Chatbot.Application.DTOs;
using Chatbot.Application.Validation;
using Chatbot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Chatbot.Application.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class AuthService : IAuthService
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxDisplayNameLength = 100;

    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILogger<AuthService> logger)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? email.Split('@')[0]
            : request.DisplayName.Trim();

        new Validator()
            .Require(Validator.IsValidEmail(email), nameof(request.Email), "A valid email address is required.")
            .Require(password.Length is >= MinPasswordLength and <= MaxPasswordLength, nameof(request.Password),
                $"Password must be between {MinPasswordLength} and {MaxPasswordLength} characters.")
            .Require(password.Any(char.IsLetter) && password.Any(char.IsDigit), nameof(request.Password),
                "Password must contain at least one letter and one digit.")
            .Require(displayName.Length <= MaxDisplayNameLength, nameof(request.DisplayName),
                $"Display name must be at most {MaxDisplayNameLength} characters.")
            .ThrowIfInvalid();

        var normalizedEmail = Normalize(email);
        if (await _users.ExistsByNormalizedEmailAsync(normalizedEmail, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName
        };
        user.PasswordHash = _passwordHasher.Hash(user, password);

        _users.Add(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} registered", user.Id);
        return CreateResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        new Validator()
            .Require(!string.IsNullOrWhiteSpace(request.Email), nameof(request.Email), "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), nameof(request.Password), "Password is required.")
            .ThrowIfInvalid();

        var user = await _users.GetByNormalizedEmailAsync(Normalize(request.Email.Trim()), cancellationToken);
        if (user is null || !_passwordHasher.Verify(user, user.PasswordHash, request.Password))
        {
            // Same error for unknown user and wrong password to avoid account enumeration.
            _logger.LogWarning("Failed login attempt");
            throw new AuthenticationFailedException();
        }

        _logger.LogInformation("User {UserId} logged in", user.Id);
        return CreateResponse(user);
    }

    public async Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);
        return user.ToDto();
    }

    private AuthResponse CreateResponse(User user)
    {
        var token = _tokenService.CreateToken(user);
        return new AuthResponse(token.Token, token.ExpiresAtUtc, user.ToDto());
    }

    private static string Normalize(string email) => email.ToUpperInvariant();
}

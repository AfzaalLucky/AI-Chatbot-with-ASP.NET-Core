namespace Chatbot.Application.Common.Exceptions;

/// <summary>Base type for expected, client-facing application errors. Mapped to Problem Details by the API.</summary>
public abstract class AppException : Exception
{
    protected AppException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string resource, object key) : base($"{resource} '{key}' was not found.")
    {
    }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string message) : base(message)
    {
    }
}

public sealed class AuthenticationFailedException : AppException
{
    public AuthenticationFailedException(string message = "Invalid email or password.") : base(message)
    {
    }
}

public sealed class ValidationException : AppException
{
    public ValidationException(IDictionary<string, string[]> errors) : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public ValidationException(string field, string error) : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Raised when the AI provider fails or is unavailable. The message is safe to show to clients.</summary>
public sealed class AiServiceException : AppException
{
    public AiServiceException(string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}

using Chatbot.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Infrastructure;

/// <summary>
/// Converts every unhandled exception into an RFC 9457 Problem Details response.
/// Expected application errors keep their (client-safe) message; anything else becomes a generic 500
/// so internal details never leak.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nothing useful to send back.
            _logger.LogInformation("Request {Path} was cancelled by the client", httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problem = Map(exception);

        if (problem.Status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request {Method} {Path} failed with {StatusCode}: {ExceptionType}",
                httpContext.Request.Method, httpContext.Request.Path, problem.Status, exception.GetType().Name);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        if (exception is AiServiceException { IsTransient: true })
        {
            httpContext.Response.Headers.RetryAfter = "10";
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    public static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException ex => new ValidationProblemDetails(ex.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = ex.Message
        },
        NotFoundException ex => Create(StatusCodes.Status404NotFound, "Resource not found", ex.Message),
        ConflictException ex => Create(StatusCodes.Status409Conflict, "Conflict", ex.Message),
        AuthenticationFailedException ex => Create(StatusCodes.Status401Unauthorized, "Authentication failed", ex.Message),
        UnauthorizedAccessException => Create(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required."),
        AiServiceException { IsTransient: true } ex => Create(StatusCodes.Status503ServiceUnavailable, "AI service unavailable", ex.Message),
        AiServiceException ex => Create(StatusCodes.Status502BadGateway, "AI service error", ex.Message),
        BadHttpRequestException ex => Create(ex.StatusCode, "Bad request", "The request could not be processed."),
        _ => Create(StatusCodes.Status500InternalServerError, "An unexpected error occurred",
            "The server encountered an unexpected error. Please try again later.")
    };

    private static ProblemDetails Create(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}

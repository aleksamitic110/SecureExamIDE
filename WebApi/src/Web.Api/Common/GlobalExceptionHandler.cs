using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Common;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A body the framework could not bind is the caller's mistake, not a fault in the server.
        // This handler used to map every exception to 500, so a JSON typo - a number where the
        // contract wants a string, a trailing comma - came back as "Server failure" with nothing
        // in it to act on. BadHttpRequestException already carries the status it deserves, which
        // is 400 for a malformed body and 413 for one that is too large.
        var badRequest = exception as BadHttpRequestException;
        bool isClientError = badRequest is not null;
        int status = badRequest?.StatusCode ?? StatusCodes.Status500InternalServerError;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Type = isClientError
                ? "https://tools.ietf.org/html/rfc7231#section-6.5.1"
                : "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Title = isClientError ? "Bad request" : "Server failure",

            // A client error may safely echo the message: it describes the caller's own request,
            // not anything about the server. A server fault says nothing.
            Detail = isClientError ? exception.Message : "An unexpected error occurred"
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception occurred");
        }
        else
        {
            // Still worth seeing, but a client sending bad JSON is not an incident.
            logger.LogWarning(exception, "Malformed request rejected");
        }

        httpContext.Response.StatusCode = status;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}

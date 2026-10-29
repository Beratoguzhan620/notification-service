using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using NotificationService.Core;

namespace NotificationService.Api.Common;

/// <summary>
/// Turns business rule violations into RFC 9457 problem responses with a stable <c>code</c> field.
/// Anything else falls through to the default handler (500 without details).
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = ErrorStatuses.StatusFor(domain.Code);
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = domain.Message,
            Extensions = { ["code"] = domain.Code },
        };

        foreach (var (key, value) in domain.Details)
        {
            problem.Extensions[key] = value;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}

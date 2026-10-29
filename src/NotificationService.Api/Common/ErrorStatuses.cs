using NotificationService.Core;

namespace NotificationService.Api.Common;

/// <summary>Maps domain error codes to HTTP status codes. Unknown codes are business rule failures (422).</summary>
internal static class ErrorStatuses
{
    private static readonly Dictionary<string, int> Statuses = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationFailed] = StatusCodes.Status400BadRequest,
        [ErrorCodes.NotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.InvalidState] = StatusCodes.Status409Conflict,
        [ErrorCodes.NameTaken] = StatusCodes.Status409Conflict,
    };

    public static int StatusFor(string code) =>
        Statuses.TryGetValue(code, out var status) ? status : StatusCodes.Status422UnprocessableEntity;
}

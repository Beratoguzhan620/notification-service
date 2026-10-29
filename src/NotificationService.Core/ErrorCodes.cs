namespace NotificationService.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string InvalidState = "invalid_state";
    public const string NameTaken = "name_taken";
}

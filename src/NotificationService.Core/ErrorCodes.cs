namespace NotificationService.Core;

/// <summary>The stable error codes returned in problem responses.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string InvalidState = "invalid_state";
    public const string NameTaken = "name_taken";
    public const string TemplateExists = "template_exists";
    public const string InvalidTemplate = "invalid_template";
    public const string MissingVariables = "missing_variables";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
    public const string ConcurrentUpdate = "concurrent_update";
}

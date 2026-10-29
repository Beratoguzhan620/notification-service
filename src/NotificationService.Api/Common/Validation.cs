using Microsoft.AspNetCore.Http.HttpResults;

namespace NotificationService.Api.Common;

/// <summary>Collects field errors and turns them into a 400 validation problem.</summary>
internal sealed class Validation
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public Validation Require(bool condition, string field, string message)
    {
        if (!condition)
        {
            if (!_errors.TryGetValue(field, out var messages))
            {
                messages = [];
                _errors[field] = messages;
            }

            messages.Add(message);
        }

        return this;
    }

    public ValidationProblem ToProblem() => TypedResults.ValidationProblem(
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal),
        extensions: new Dictionary<string, object?> { ["code"] = NotificationService.Core.ErrorCodes.ValidationFailed });
}

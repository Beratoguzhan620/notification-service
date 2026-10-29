namespace NotificationService.Core.Templates;

/// <summary>A template version with the variables filled in: exactly what will be delivered.</summary>
public sealed record RenderedMessage(string Subject, string Text, string? Html);

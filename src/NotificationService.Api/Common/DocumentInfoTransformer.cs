using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NotificationService.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Notification Service",
            Version = "1.0",
            Description = """
                Reach people by email or through an in-app inbox. Requests are idempotent, leave the database
                through an outbox and are delivered by RabbitMQ workers with retries and a dead-letter queue.

                **Getting started**

                1. Paste the admin API key from configuration into *Authentication* (`X-Api-Key`).
                2. Create a key for your system at `POST /api/clients`.
                3. Look at the templates at `GET /api/templates` (Compose starts with `order-shipped` and
                   `password-changed` in Turkish and English).
                4. Send one with `POST /api/notifications` and an `Idempotency-Key` header, then follow it at
                   `GET /api/notifications/{id}`.

                **Roles:** senders send notifications and read their own; admins also manage clients and templates.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `not_found`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/notification-service/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}

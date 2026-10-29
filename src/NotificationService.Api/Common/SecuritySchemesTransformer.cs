using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NotificationService.Api.Features.Auth;

namespace NotificationService.Api.Common;

/// <summary>Declares the API key scheme and attaches it to every operation that requires a key.</summary>
internal sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthentication.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthentication.HeaderName,
            Description = "The API key of a client, for example the admin key from configuration.",
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiKeyAuthentication.Scheme, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}

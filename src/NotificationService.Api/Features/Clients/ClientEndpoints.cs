using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Common;
using NotificationService.Api.Features.Auth;
using NotificationService.Core;
using NotificationService.Core.Clients;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Clients;

public sealed record CreateClientRequest(string Name, ClientRole Role = ClientRole.Sender);

public sealed record ClientResponse(Guid Id, string Name, ClientRole Role, string KeyPrefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public static ClientResponse Create(ApiClient client) =>
        new(client.Id, client.Name, client.Role, client.KeyPrefix, client.CreatedAt, client.RevokedAt);
}

/// <summary>Returned once, when the key is created. The service keeps only a hash.</summary>
public sealed record CreatedClientResponse(Guid Id, string Name, ClientRole Role, string KeyPrefix, DateTimeOffset CreatedAt, string ApiKey);

internal static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clients").WithTags("Clients");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithSummary("The client the key belongs to");

        group.MapGet("/", List)
            .RequireAuthorization(ApiKeyAuthentication.AdminPolicy)
            .WithSummary("All clients (admin)");

        group.MapPost("/", Create)
            .RequireAuthorization(ApiKeyAuthentication.AdminPolicy)
            .WithSummary("Create a client and its API key (admin)")
            .WithDescription("The key is in the response only; store it right away. `role` is `Sender` unless given.");

        group.MapPost("/{id:guid}/revoke", Revoke)
            .RequireAuthorization(ApiKeyAuthentication.AdminPolicy)
            .WithSummary("Revoke a client's key (admin)");

        return app;
    }

    private static async Task<Results<Ok<ClientResponse>, NotFound>> Me(ClaimsPrincipal user, NotificationsDbContext db, CancellationToken ct)
    {
        var client = await db.ApiClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == user.ClientId(), ct);
        return client is null ? TypedResults.NotFound() : TypedResults.Ok(ClientResponse.Create(client));
    }

    private static async Task<Ok<List<ClientResponse>>> List(NotificationsDbContext db, CancellationToken ct)
    {
        var clients = await db.ApiClients.AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(clients.Select(ClientResponse.Create).ToList());
    }

    private static async Task<Results<Created<CreatedClientResponse>, ValidationProblem>> Create(
        CreateClientRequest request, NotificationsDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Name), "name", "Give the client a name.")
            .Require(Enum.IsDefined(request.Role), "role", "The role is Sender or Admin.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (client, key) = ApiClient.Create(request.Name, request.Role, clock.GetUtcNow());
        db.ApiClients.Add(client);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_api_clients_name"))
        {
            throw new DomainException(ErrorCodes.NameTaken, "A client with this name already exists.");
        }

        ClientLog.Created(loggers.CreateLogger(typeof(ClientEndpoints)), client.Name, client.Role, client.KeyPrefix);
        return TypedResults.Created($"/api/clients/{client.Id}",
            new CreatedClientResponse(client.Id, client.Name, client.Role, client.KeyPrefix, client.CreatedAt, key));
    }

    private static async Task<Ok<ClientResponse>> Revoke(
        Guid id, ClaimsPrincipal user, NotificationsDbContext db, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        if (id == user.ClientId())
        {
            throw new DomainException(ErrorCodes.InvalidState, "A key cannot revoke itself; use another admin key.");
        }

        var client = await db.ApiClients.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no client with this id.");
        client.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        ClientLog.Revoked(loggers.CreateLogger(typeof(ClientEndpoints)), client.Name, client.KeyPrefix);
        return TypedResults.Ok(ClientResponse.Create(client));
    }
}

internal static partial class ClientLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ClientName} created with role {Role} and key prefix {KeyPrefix}")]
    public static partial void Created(ILogger logger, string clientName, ClientRole role, string keyPrefix);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ClientName} revoked (key prefix {KeyPrefix})")]
    public static partial void Revoked(ILogger logger, string clientName, string keyPrefix);
}

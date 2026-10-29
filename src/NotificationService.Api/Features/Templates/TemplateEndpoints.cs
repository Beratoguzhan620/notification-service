using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Common;
using NotificationService.Api.Features.Auth;
using NotificationService.Core;
using NotificationService.Core.Templates;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Api.Features.Templates;

public sealed record CreateTemplateRequest(string Key, string Description, string DefaultLanguage);

public sealed record AddVersionRequest(string Language, string Subject, string Text, string? Html = null);

public sealed record PreviewRequest(string? Language = null, Dictionary<string, string>? Variables = null);

public sealed record LanguageVersion(string Language, int LatestVersion);

public sealed record TemplateSummary(Guid Id, string Key, string Description, string DefaultLanguage, List<LanguageVersion> Languages, DateTimeOffset CreatedAt);

public sealed record VersionSummary(string Language, int Version, string Subject, IReadOnlyList<string> Variables, bool HasHtml, DateTimeOffset CreatedAt);

public sealed record TemplateDetail(Guid Id, string Key, string Description, string DefaultLanguage, List<VersionSummary> Versions, DateTimeOffset CreatedAt);

public sealed record VersionDetail(string Key, string Language, int Version, string Subject, string Text, string? Html, IReadOnlyList<string> Variables, DateTimeOffset CreatedAt);

public sealed record PreviewResponse(string Key, string Language, int Version, string Subject, string Text, string? Html);

internal static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/templates").WithTags("Templates").RequireAuthorization();

        group.MapGet("/", List).WithSummary("All templates with their languages");
        group.MapGet("/{key}", Get).WithSummary("A template with every version");
        group.MapGet("/{key}/versions/{language}/{version:int}", GetVersion).WithSummary("The full text of one version");
        group.MapPost("/{key}/preview", Preview)
            .WithSummary("Render the newest version with sample values")
            .WithDescription("Uses the same rules as a real notification: missing variables are refused and HTML values are escaped.");

        group.MapPost("/", Create)
            .RequireAuthorization(ApiKeyAuthentication.AdminPolicy)
            .WithSummary("Create a template (admin)");
        group.MapPost("/{key}/versions", AddVersion)
            .RequireAuthorization(ApiKeyAuthentication.AdminPolicy)
            .WithSummary("Add a version in one language (admin)")
            .WithDescription("Placeholders look like `{{name}}`. Earlier versions stay unchanged; new notifications use the newest one.");

        return app;
    }

    private static async Task<Ok<List<TemplateSummary>>> List(NotificationsDbContext db, CancellationToken ct)
    {
        var templates = await db.Templates.AsNoTracking().Include(t => t.Versions).OrderBy(t => t.Key).ToListAsync(ct);
        return TypedResults.Ok(templates.Select(t => new TemplateSummary(
            t.Id, t.Key, t.Description, t.DefaultLanguage,
            t.Versions.GroupBy(v => v.Language).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new LanguageVersion(g.Key, g.Max(v => v.Version))).ToList(),
            t.CreatedAt)).ToList());
    }

    private static async Task<Ok<TemplateDetail>> Get(string key, NotificationsDbContext db, CancellationToken ct)
    {
        var template = await FindAsync(db, key, ct, tracking: false);
        return TypedResults.Ok(Detail(template));
    }

    private static async Task<Ok<VersionDetail>> GetVersion(string key, string language, int version, NotificationsDbContext db, CancellationToken ct)
    {
        var template = await FindAsync(db, key, ct, tracking: false);
        var found = template.Versions.SingleOrDefault(v => v.Language == Languages.Normalize(language) && v.Version == version)
            ?? throw new DomainException(ErrorCodes.NotFound, $"The template '{template.Key}' has no version {version} in '{language}'.");
        return TypedResults.Ok(new VersionDetail(template.Key, found.Language, found.Version, found.Subject, found.Text, found.Html, found.Variables, found.CreatedAt));
    }

    private static async Task<Results<Ok<PreviewResponse>, ValidationProblem>> Preview(string key, PreviewRequest request, NotificationsDbContext db, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(request.Language is null || Languages.Normalize(request.Language) is not null, "language", "The language is tr or en.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var template = await FindAsync(db, key, ct, tracking: false);
        var version = template.Resolve(request.Language);
        var rendered = version.Render(request.Variables ?? []);
        return TypedResults.Ok(new PreviewResponse(template.Key, version.Language, version.Version, rendered.Subject, rendered.Text, rendered.Html));
    }

    private static async Task<Results<Created<TemplateDetail>, ValidationProblem>> Create(
        CreateTemplateRequest request, NotificationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Key), "key", "Give the template a key, like 'order-shipped'.")
            .Require(!string.IsNullOrWhiteSpace(request.DefaultLanguage), "defaultLanguage", "The default language is tr or en.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var template = Template.Create(request.Key, request.Description, request.DefaultLanguage, clock.GetUtcNow());
        db.Templates.Add(template);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.TemplateExists, $"A template with the key '{template.Key}' already exists.");
        }

        return TypedResults.Created($"/api/templates/{template.Key}", Detail(template));
    }

    private static async Task<Results<Created<VersionDetail>, ValidationProblem>> AddVersion(
        string key, AddVersionRequest request, NotificationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Language), "language", "The language is tr or en.")
            .Require(!string.IsNullOrWhiteSpace(request.Subject), "subject", "Give the version a subject.")
            .Require(!string.IsNullOrWhiteSpace(request.Text), "text", "Give the version a plain text body; HTML is optional.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var template = await FindAsync(db, key, ct, tracking: true);
        var version = template.AddVersion(request.Language, request.Subject, request.Text, request.Html, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.ConcurrentUpdate, "Another version was added at the same moment. Read the template again and retry.");
        }

        return TypedResults.Created($"/api/templates/{template.Key}/versions/{version.Language}/{version.Version}",
            new VersionDetail(template.Key, version.Language, version.Version, version.Subject, version.Text, version.Html, version.Variables, version.CreatedAt));
    }

    internal static async Task<Template> FindAsync(NotificationsDbContext db, string key, CancellationToken ct, bool tracking)
    {
        var normalized = key.Trim().ToLowerInvariant();
        var query = db.Templates.Include(t => t.Versions).Where(t => t.Key == normalized);
        return await (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw new DomainException(ErrorCodes.NotFound, $"There is no template with the key '{normalized}'.");
    }

    private static TemplateDetail Detail(Template template) => new(
        template.Id, template.Key, template.Description, template.DefaultLanguage,
        template.Versions
            .OrderBy(v => v.Language, StringComparer.Ordinal).ThenByDescending(v => v.Version)
            .Select(v => new VersionSummary(v.Language, v.Version, v.Subject, v.Variables, v.Html is not null, v.CreatedAt))
            .ToList(),
        template.CreatedAt);
}

namespace NotificationService.Api.Features.Notifications;

/// <summary>A page of results, newest first. Pass <c>nextCursor</c> as <c>cursor</c> to read on.</summary>
public sealed record Page<T>(List<T> Items, Guid? NextCursor);

internal static class Paging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static int Clamp(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    /// <summary>Ids are UUIDv7, so ordering by id is ordering by creation time.</summary>
    public static Page<TOut> Create<TIn, TOut>(List<TIn> rows, int limit, Func<TIn, Guid> id, Func<TIn, TOut> map) =>
        new(rows.Take(limit).Select(map).ToList(), rows.Count > limit ? id(rows[limit - 1]) : null);
}

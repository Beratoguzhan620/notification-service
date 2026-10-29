namespace NotificationService.IntegrationTests.Support;

public sealed record PageDto<T>(List<T> Items, Guid? NextCursor);

public sealed record InboxItemDto(Guid Id, string Template, string Subject, string Text, DateTimeOffset DeliveredAt, DateTimeOffset? ReadAt);

public sealed record InboxPageDto(List<InboxItemDto> Items, Guid? NextCursor, int Unread);

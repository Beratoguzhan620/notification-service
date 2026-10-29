namespace NotificationService.Core.Clients;

public enum ClientRole
{
    /// <summary>A calling system: sends notifications and reads its own.</summary>
    Sender,

    /// <summary>An operator: manages clients and templates and handles dead letters.</summary>
    Admin,
}

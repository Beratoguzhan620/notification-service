using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationService.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=notifications_design");
        return new NotificationsDbContext(options.Options);
    }
}

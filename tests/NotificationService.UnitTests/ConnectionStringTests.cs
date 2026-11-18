using NotificationService.Infrastructure.Persistence;

namespace NotificationService.UnitTests;

public sealed class ConnectionStringTests
{
    [Fact]
    public void A_postgres_url_from_a_hosted_database_becomes_a_connection_string()
    {
        var converted = ConnectionStrings.FromUrl("postgresql://app_user:p%40ss@ep-cool-name-123.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require");
        Assert.Equal("Host=ep-cool-name-123.eu-central-1.aws.neon.tech;Database=neondb;Username=app_user;Password=p@ss;SSL Mode=Require;Channel Binding=Require", converted);
    }

    [Fact]
    public void A_connection_string_passes_through()
    {
        const string value = "Host=localhost;Database=yoldas;Username=yoldas;Password=x";
        Assert.Equal(value, ConnectionStrings.FromUrl(value));
        Assert.Null(ConnectionStrings.FromUrl(null));
    }
}

using System.Net;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HealthTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_need_no_key(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }
}

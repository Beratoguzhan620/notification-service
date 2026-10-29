using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NotificationService.IntegrationTests.Support;

namespace NotificationService.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TemplateTests(ApiFactory factory)
{
    [Fact]
    public async Task The_sample_templates_are_there_in_both_languages()
    {
        using var admin = factory.Admin();

        var list = await (await admin.GetAsync("/api/templates")).Content.ReadAsStringAsync();
        using var detail = JsonDocument.Parse(await (await admin.GetAsync("/api/templates/order-shipped")).Content.ReadAsStringAsync());

        Assert.Contains("order-shipped", list, StringComparison.Ordinal);
        Assert.Contains("password-changed", list, StringComparison.Ordinal);
        var versions = detail.RootElement.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(new[] { "en", "tr" }, versions.Select(v => v.GetProperty("language").GetString()));
        Assert.Equal(new[] { "carrier", "name", "orderNo", "trackingNo" },
            versions[1].GetProperty("variables").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Admins_create_templates_and_add_versions_per_language()
    {
        using var admin = factory.Admin();
        var key = NewKey();

        var created = await admin.PostAsJsonAsync("/api/templates", new { key, description = "Welcome", defaultLanguage = "en" });
        var en1 = await admin.PostAsJsonAsync($"/api/templates/{key}/versions", new { language = "en", subject = "Welcome", text = "Hello {{name}}" });
        var en2 = await admin.PostAsJsonAsync($"/api/templates/{key}/versions", new { language = "en", subject = "Welcome!", text = "Hello {{name}}" });
        var tr1 = await admin.PostAsJsonAsync($"/api/templates/{key}/versions", new { language = "tr", subject = "Hoş geldin", text = "Merhaba {{name}}" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(new[] { 1, 2, 1 }, await Task.WhenAll(new[] { en1, en2, tr1 }.Select(VersionOf)));
        Assert.Equal($"/api/templates/{key}/versions/en/2", en2.Headers.Location!.ToString());

        var stored = await (await admin.GetAsync($"/api/templates/{key}/versions/en/1")).ReadAsync<JsonElement>();
        Assert.Equal("Welcome", stored.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task Senders_read_templates_but_cannot_change_them()
    {
        var (sender, _) = await factory.NewClientAsync();
        using var _ = sender;

        Assert.Equal(HttpStatusCode.OK, (await sender.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await sender.PostAsJsonAsync("/api/templates", new { key = NewKey(), description = "x", defaultLanguage = "en" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await sender.PostAsJsonAsync("/api/templates/order-shipped/versions", new { language = "en", subject = "x", text = "y" })).StatusCode);
    }

    [Fact]
    public async Task Keys_are_unique()
    {
        using var admin = factory.Admin();
        var key = NewKey();

        await admin.PostAsJsonAsync("/api/templates", new { key, description = "x", defaultLanguage = "en" });
        var again = await admin.PostAsJsonAsync("/api/templates", new { key = key.ToUpperInvariant(), description = "x", defaultLanguage = "tr" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("template_exists", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Broken_templates_are_refused_with_the_place_of_the_mistake()
    {
        using var admin = factory.Admin();
        var key = NewKey();
        await admin.PostAsJsonAsync("/api/templates", new { key, description = "x", defaultLanguage = "en" });

        var response = await admin.PostAsJsonAsync($"/api/templates/{key}/versions",
            new { language = "en", subject = "Hi", text = "Hello", html = "<p>{{ first name }}</p>" });
        var problem = await response.ReadAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_template", problem.GetProperty("code").GetString());
        Assert.Equal("html", problem.GetProperty("field").GetString());
        Assert.Equal(3, problem.GetProperty("position").GetInt32());
    }

    [Theory]
    [InlineData("x", "en")]
    [InlineData("valid-key", "de")]
    public async Task Bad_keys_and_languages_are_refused(string key, string language)
    {
        using var admin = factory.Admin();

        var response = await admin.PostAsJsonAsync("/api/templates", new { key, description = "x", defaultLanguage = language });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Preview_renders_and_escapes_html()
    {
        using var admin = factory.Admin();

        var preview = await (await admin.PostAsJsonAsync("/api/templates/order-shipped/preview", new
        {
            language = "en",
            variables = new { name = "<b>Ali</b>", orderNo = "1001", carrier = "Aras", trackingNo = "A-1" },
        })).ReadAsync<JsonElement>();

        Assert.Equal("Your order 1001 is on its way", preview.GetProperty("subject").GetString());
        Assert.Contains("Hello <b>Ali</b>,", preview.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("Hello &lt;b&gt;Ali&lt;/b&gt;,", preview.GetProperty("html").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_names_missing_variables()
    {
        using var admin = factory.Admin();

        var response = await admin.PostAsJsonAsync("/api/templates/order-shipped/preview", new { language = "tr", variables = new { name = "Ayşe" } });
        var problem = await response.ReadAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("missing_variables", problem.GetProperty("code").GetString());
        Assert.Equal(new[] { "carrier", "orderNo", "trackingNo" }, problem.GetProperty("missing").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Unknown_templates_are_not_found()
    {
        using var admin = factory.Admin();

        var response = await admin.GetAsync("/api/templates/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string NewKey() => $"t-{Guid.NewGuid():N}"[..20];

    private static async Task<int> VersionOf(HttpResponseMessage response) =>
        (await response.ReadAsync<JsonElement>()).GetProperty("version").GetInt32();
}

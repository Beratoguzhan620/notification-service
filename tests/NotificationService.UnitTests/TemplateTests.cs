using NotificationService.Core;
using NotificationService.Core.Templates;

namespace NotificationService.UnitTests;

public sealed class TemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("order-shipped", "order-shipped")]
    [InlineData("  Order-Shipped ", "order-shipped")]
    [InlineData("a1", "a1")]
    public void Keys_are_normalized(string key, string expected) =>
        Assert.Equal(expected, Template.Create(key, null, "tr", Now).Key);

    [Theory]
    [InlineData("x")]
    [InlineData("-order")]
    [InlineData("order-")]
    [InlineData("order shipped")]
    [InlineData("sipariş")]
    public void Odd_keys_are_refused(string key) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Template.Create(key, null, "tr", Now)).Code);

    [Fact]
    public void Only_turkish_and_english_are_supported() =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Template.Create("welcome", null, "de", Now)).Code);

    [Fact]
    public void Versions_are_numbered_per_language()
    {
        var template = Template.Create("welcome", null, "tr", Now);

        var tr1 = template.AddVersion("tr", "Hoş geldin", "Merhaba", null, Now);
        var en1 = template.AddVersion("EN", "Welcome", "Hello", null, Now);
        var tr2 = template.AddVersion("tr", "Hoş geldin {{name}}", "Merhaba {{name}}", null, Now.AddMinutes(1));

        Assert.Equal((1, 1, 2), (tr1.Version, en1.Version, tr2.Version));
        Assert.Equal("en", en1.Language);
        Assert.Same(tr2, template.Latest("tr"));
    }

    [Fact]
    public void Resolve_falls_back_to_the_default_language()
    {
        var template = Template.Create("welcome", null, "en", Now);
        var english = template.AddVersion("en", "Welcome", "Hello", null, Now);

        Assert.Same(english, template.Resolve("tr"));
        Assert.Same(english, template.Resolve(null));
        Assert.Same(english, template.Resolve("en"));
    }

    [Fact]
    public void A_template_without_versions_cannot_be_used()
    {
        var template = Template.Create("welcome", null, "en", Now);

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => template.Resolve("en")).Code);
    }

    [Fact]
    public void A_broken_version_is_refused_when_it_is_added()
    {
        var template = Template.Create("welcome", null, "en", Now);

        var error = Assert.Throws<DomainException>(() => template.AddVersion("en", "Hi", "Hello {{name", null, Now));

        Assert.Equal(ErrorCodes.InvalidTemplate, error.Code);
        Assert.Empty(template.Versions);
    }

    [Fact]
    public void Subjects_are_single_lines()
    {
        var template = Template.Create("welcome", null, "en", Now);

        Assert.Throws<DomainException>(() => template.AddVersion("en", "Hi\nthere", "Hello", null, Now));
    }

    [Fact]
    public void Rendering_names_every_missing_variable()
    {
        var template = Template.Create("welcome", null, "en", Now);
        var version = template.AddVersion("en", "Hi {{name}}", "Order {{orderNo}}", "<p>{{carrier}}</p>", Now);

        var error = Assert.Throws<DomainException>(() => version.Render(new Dictionary<string, string> { ["name"] = "Ayşe" }));

        Assert.Equal(ErrorCodes.MissingVariables, error.Code);
        Assert.Equal(new[] { "carrier", "orderNo" }, (IEnumerable<string>)error.Details["missing"]!);
    }

    [Fact]
    public void Rendering_fills_every_part_and_ignores_extra_values()
    {
        var template = Template.Create("welcome", null, "en", Now);
        var version = template.AddVersion("en", "Hi {{name}}", "Hello {{name}}", "<b>{{name}}</b>", Now);

        var message = version.Render(new Dictionary<string, string> { ["name"] = "Ayşe & Ali", ["unused"] = "x" });

        Assert.Equal(new RenderedMessage("Hi Ayşe & Ali", "Hello Ayşe & Ali", "<b>Ayşe &amp; Ali</b>"), message);
        Assert.Equal(new[] { "name" }, version.Variables);
    }
}

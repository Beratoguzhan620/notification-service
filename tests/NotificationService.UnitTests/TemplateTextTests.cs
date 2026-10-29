using NotificationService.Core;
using NotificationService.Core.Templates;

namespace NotificationService.UnitTests;

public sealed class TemplateTextTests
{
    [Fact]
    public void Finds_every_variable_once()
    {
        var text = TemplateText.Parse("Hi {{name}}, order {{ orderNo }} for {{name}}.", "text");

        Assert.Equal(new[] { "name", "orderNo" }, text.Variables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Text_without_placeholders_is_kept_as_it_is()
    {
        var text = TemplateText.Parse("Single } and { braces stay.", "text");

        Assert.Empty(text.Variables);
        Assert.Equal("Single } and { braces stay.", text.Render(new Dictionary<string, string>(), TextKind.Plain));
    }

    [Theory]
    [InlineData("Hello {{name", 6)]
    [InlineData("Hello {{}}", 6)]
    [InlineData("A {{1st}} try", 2)]
    [InlineData("A {{first name}}", 2)]
    [InlineData("A {{na-me}}", 2)]
    public void Broken_placeholders_are_refused_with_their_position(string source, int position)
    {
        var error = Assert.Throws<DomainException>(() => TemplateText.Parse(source, "subject"));

        Assert.Equal(ErrorCodes.InvalidTemplate, error.Code);
        Assert.Equal("subject", error.Details["field"]);
        Assert.Equal(position, error.Details["position"]);
    }

    [Fact]
    public void Html_values_are_encoded()
    {
        var text = TemplateText.Parse("<p>{{name}}</p>", "html");

        var html = text.Render(new Dictionary<string, string> { ["name"] = "<script>alert('x')</script> & \"you\"" }, TextKind.Html);

        Assert.Equal("<p>&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;you&quot;</p>", html);
    }

    [Fact]
    public void Single_line_values_cannot_add_lines()
    {
        var text = TemplateText.Parse("Order {{orderNo}}", "subject");

        var subject = text.Render(new Dictionary<string, string> { ["orderNo"] = "1001\r\nBcc: someone@example.com" }, TextKind.SingleLine);

        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
    }

    [Fact]
    public void Plain_text_keeps_values_unchanged()
    {
        var text = TemplateText.Parse("{{a}} & {{b}}", "text");

        Assert.Equal("<b> & Ş\nx", text.Render(new Dictionary<string, string> { ["a"] = "<b>", ["b"] = "Ş\nx" }, TextKind.Plain));
    }
}

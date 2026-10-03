using AngleSharp.Html.Parser;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class ReadingTests
{
    private static string Render(string body) { var id = Guid.NewGuid(); return new MarkdownReader().Render(new(id, "Read.md", "Read", NoteDocument.AddId(body, id), "revision")); }
    [Fact]
    public void OutlineUsesRenderedTextAllLevelsAndUniqueAnchorsExcludingCode()
    {
        var doc = new HtmlParser().ParseDocument(Render("# One\n\n### **Deep** heading\n\n###### Sixth\n\n# One\n\n```md\n# Not a heading\n```"));
        var headings = doc.QuerySelectorAll("main [data-slate-heading]");
        Assert.Equal(new[] { "one", "deep-heading", "sixth", "one-1" }, headings.Select(x => x.Id));
        Assert.Equal("Deep heading", headings[1].TextContent); Assert.Equal("H6", headings[2].TagName);
        Assert.NotNull(doc.QuerySelector("script[src='slate-reading.js']"));
    }
    [Fact]
    public void ExplicitDisclosureCalloutsPreserveBodyAndDefaultOpenState()
    {
        var doc = new HtmlParser().ParseDocument(Render("> [!NOTE]- Details\n> The first body line.\n>\n> More **body**.\n\n> [!TIP]+ Open title\n> Visible body."));
        var details = doc.QuerySelectorAll("main details"); Assert.Equal(2, details.Length);
        Assert.Equal("Details", details[0].QuerySelector("summary")!.TextContent); Assert.False(details[0].HasAttribute("open"));
        Assert.Contains("The first body line.", details[0].TextContent); Assert.Contains("More body.", details[0].TextContent);
        Assert.True(details[1].HasAttribute("open")); Assert.Contains("Visible body.", details[1].TextContent);
    }
    [Fact]
    public void RawHtmlCannotCreateDisclosureHandlersOrScripts()
    {
        var doc = new HtmlParser().ParseDocument(Render("<details open ontoggle=\"alert(1)\"><summary>Bad</summary><script>alert(2)</script></details>\n\n> [!NOTE]- <img src=x onerror=alert(3)>\n> Preserved body."));
        Assert.Single(doc.QuerySelectorAll("main details")); Assert.Empty(doc.QuerySelectorAll("main script, main [ontoggle], main [onerror], main img"));
        Assert.Contains("Preserved body.", doc.QuerySelector("main")!.TextContent);
    }
    [Fact]
    public async Task ReaderMaterializesNavigationLocallyWithoutRemoteResources()
    {
        using var fixture = new TestLibrary(); var directory = await MarkdownReader.MaterializeAssetsAsync(fixture.DerivedRoot);
        Assert.True(File.Exists(Path.Combine(directory, "slate-reading.js")));
        var html = Render("## Section\n\nText"); Assert.Contains("script-src 'self'", html); Assert.Contains("connect-src 'none'", html);
    }
}

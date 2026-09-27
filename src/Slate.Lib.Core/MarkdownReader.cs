using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Markdig;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Slate.Lib.Core;

public sealed partial class MarkdownReader
{
    public const string RendererVersion = "3c-mermaid12-katex0189-hljs1112";
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public string Render(LibraryNote note, NoteLinks? links = null, IReadOnlyDictionary<Guid, string>? imageData = null)
    {
        var body = NoteDocument.Parse(note.Markdown, note.Path).Body;
        var rendered = Markdown.ToHtml(PrepareExtensions(body, links), pipeline)
            .Replace("<pre><code class=\"language-mermaid\">", "<pre class=\"mermaid-frame\"><code class=\"language-mermaid\">", StringComparison.Ordinal);
        var document = new HtmlParser().ParseDocument(rendered);
        var slugger = new HeadingSlugger();
        foreach (var heading in document.QuerySelectorAll("h1,h2,h3,h4,h5,h6")) heading.Id = slugger.Add(heading.TextContent);

        foreach (var code in document.QuerySelectorAll("pre > code.language-mermaid").ToArray())
        {
            code.ParentElement!.ClassName = "mermaid-frame";
        }
        foreach (var callout in document.QuerySelectorAll("blockquote").ToArray())
        {
            var paragraph = callout.QuerySelector("p");
            if (paragraph is null) continue;
            var match = CalloutPattern().Match(paragraph.TextContent);
            if (!match.Success) continue;
            var kind = match.Groups[1].Value.ToLowerInvariant();
            callout.ClassName = "callout callout-" + kind;
            paragraph.InnerHtml = Regex.Replace(paragraph.InnerHtml, @"^\s*\[![A-Za-z]+\]\s*", $"<strong>{WebUtility.HtmlEncode(match.Groups[1].Value.ToUpperInvariant())}</strong> ");
        }
        foreach (var image in document.QuerySelectorAll("img[src]").ToArray())
        {
            var source = image.GetAttribute("src")!;
            if (AssetReferences.TryParse(note.Path, source, out var id) && imageData?.TryGetValue(id, out var data) == true)
            { image.SetAttribute("src", data); image.SetAttribute("loading", "lazy"); }
            else image.Replace(document.CreateTextNode($"[Image unavailable: {image.GetAttribute("alt") ?? "image"}]"));
        }
        foreach (var link in document.QuerySelectorAll("a[href]").ToArray())
        {
            var href = link.GetAttribute("href")!;
            if (AssetReferences.TryParse(note.Path, href, out var assetId)) { link.SetAttribute("href", $"slate-asset://{assetId:D}"); continue; }
            if (href.StartsWith("slate-note:", StringComparison.OrdinalIgnoreCase) || href.StartsWith('#')) continue;
            if (!Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                var baseUri = new Uri("https://slate.invalid/" + string.Join('/', note.Path.Split('/').Select(Uri.EscapeDataString)));
                if (Uri.TryCreate(baseUri, href, out var target) && target.Host == "slate.invalid") link.SetAttribute("href", target.AbsoluteUri);
                else link.RemoveAttribute("href");
            }
        }
        var safe = CreateSanitizer().Sanitize(document.Body!.InnerHtml);
        var backlinks = links is null || links.Backlinks.Count == 0 ? "" : "<aside class=\"backlinks\"><h2>Linked mentions</h2><ul>" +
            string.Concat(links.Backlinks.Select(link => $"<li><a href=\"slate-note://{link.SourceId:D}\">{WebUtility.HtmlEncode(link.SourceTitle)}</a><small>{WebUtility.HtmlEncode(link.SourcePath)}</small></li>")) + "</ul></aside>";
        return $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src data:; script-src 'self'; base-uri 'none'; form-action 'none'; connect-src 'none'; object-src 'none'">
            <title>{{WebUtility.HtmlEncode(note.Title)}}</title>
            <link rel="stylesheet" href="highlight.css"><link rel="stylesheet" href="katex/katex.min.css">
            <style>{{ReaderCss}}</style></head><body><main>{{safe}}{{backlinks}}</main>
            <script src="highlight.min.js"></script><script src="katex/katex.min.js"></script><script src="mermaid.min.js"></script>
            <script src="slate-render.js"></script></body></html>
            """;
    }

    public async Task<string> WriteDocumentAsync(string root, LibraryNote note, NoteLinks? links = null,
        IReadOnlyDictionary<Guid, string>? imageData = null, CancellationToken token = default)
    {
        var directory = await MaterializeAssetsAsync(root, token);
        var path = Path.Combine(directory, "note-" + note.Id.ToString("D") + ".html");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, Render(note, links, imageData), new UTF8Encoding(false), token);
        File.Move(temporary, path, true);
        return path;
    }

    public static async Task<string> MaterializeAssetsAsync(string root, CancellationToken token = default)
    {
        var directory = Path.Combine(root, "renderer", RendererVersion);
        var assembly = typeof(MarkdownReader).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.Contains(".RenderingAssets.", StringComparison.Ordinal)))
        {
            var suffix = resource[(resource.IndexOf(".RenderingAssets.", StringComparison.Ordinal) + ".RenderingAssets.".Length)..];
            var relative = suffix.StartsWith("katex.", StringComparison.Ordinal)
                ? "katex/" + suffix["katex.".Length..].Replace("fonts.", "fonts/", StringComparison.Ordinal)
                : suffix;
            var destination = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Missing renderer resource: " + resource);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await input.CopyToAsync(output, token);
        }
        var bootstrap = Path.Combine(directory, "slate-render.js");
        if (!File.Exists(bootstrap)) await File.WriteAllTextAsync(bootstrap, BootstrapJavaScript, new UTF8Encoding(false), token);
        return directory;
    }

    private static string PrepareExtensions(string body, NoteLinks? links)
    {
        var fences = new List<string>();
        var value = FencedPattern().Replace(body, match => { fences.Add(match.Value); return $"@@SLATE_FENCE_{fences.Count - 1}@@"; });
        value = InlineCodePattern().Replace(value, match => { fences.Add(match.Value); return $"@@SLATE_FENCE_{fences.Count - 1}@@"; });
        value = DisplayMathPattern().Replace(value, match => MathMarker(match.Groups[1].Value, true));
        value = InlineMathPattern().Replace(value, match => MathMarker(match.Groups[1].Value, false));
        var outgoing = links?.Outgoing.GetEnumerator();
        value = WikiPattern().Replace(value, match =>
        {
            NoteLink? link = null; if (outgoing is not null && outgoing.MoveNext()) link = outgoing.Current;
            var inner = match.Groups[1].Value; var pipe = inner.IndexOf('|'); var target = (pipe < 0 ? inner : inner[..pipe]).Trim();
            var label = EscapeMarkdown(pipe < 0 ? target.Split('#')[0] : inner[(pipe + 1)..].Trim());
            if (link?.State == "resolved" && link.TargetId is { } id)
            { var anchor = link.Heading is null ? "" : "#" + HeadingSlugger.Slug(link.Heading); return $"[{label}](slate-note://{id:D}{anchor})"; }
            var state = link?.State ?? "broken";
            return $"**{label}** [{(state == "ambiguous" ? "ambiguous" : state == "missing-heading" ? "heading missing" : "missing")}]";
        });
        for (var index = 0; index < fences.Count; index++) value = value.Replace($"@@SLATE_FENCE_{index}@@", fences[index], StringComparison.Ordinal);
        return value;
    }

    private static string MathMarker(string expression, bool display) => $"@@SLATE_MATH_{(display ? "D" : "I")}_{Convert.ToBase64String(Encoding.UTF8.GetBytes(expression))}@@";
    private static string EscapeMarkdown(string value) => value.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer(); sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "pre", "code", "blockquote", "strong", "em", "del", "a", "hr", "br", "table", "thead", "tbody", "tr", "th", "td", "input", "span", "div", "section", "sup" }) sanitizer.AllowedTags.Add(tag);
        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "title", "type", "checked", "disabled", "start", "class", "id", "data-mermaid", "loading" }) sanitizer.AllowedAttributes.Add(attr);
        sanitizer.AllowedSchemes.Clear(); foreach (var scheme in new[] { "https", "http", "slate-note", "slate-asset" }) sanitizer.AllowedSchemes.Add(scheme);
        return sanitizer;
    }

    [GeneratedRegex(@"(?ms)^(?<fence>`{3,}|~{3,})[^\n]*\n.*?^\k<fence>\s*$", RegexOptions.CultureInvariant)] private static partial Regex FencedPattern();
    [GeneratedRegex(@"(?m)(?<ticks>`+)[^\r\n]*?\k<ticks>", RegexOptions.CultureInvariant)] private static partial Regex InlineCodePattern();
    [GeneratedRegex(@"(?s)\$\$(.+?)\$\$", RegexOptions.CultureInvariant)] private static partial Regex DisplayMathPattern();
    [GeneratedRegex(@"(?<!\$)\$(?!\$)([^\r\n$]+?)(?<!\$)\$(?!\$)", RegexOptions.CultureInvariant)] private static partial Regex InlineMathPattern();
    [GeneratedRegex(@"\[\[([^\]\r\n]+)\]\]", RegexOptions.CultureInvariant)] private static partial Regex WikiPattern();
    [GeneratedRegex(@"^\s*\[!([A-Za-z]+)\]", RegexOptions.CultureInvariant)] private static partial Regex CalloutPattern();

    private const string BootstrapJavaScript = """
        (() => {
          document.querySelectorAll('pre code:not(.language-mermaid)').forEach(el => { try { hljs.highlightElement(el); } catch (_) {} });
          const decode = value => { try { return decodeURIComponent(escape(atob(value))); } catch (_) { return ''; } };
          const walker = document.createTreeWalker(document.querySelector('main'), NodeFilter.SHOW_TEXT); const nodes=[]; while(walker.nextNode()) nodes.push(walker.currentNode);
          const pattern=/@@SLATE_MATH_([DI])_([A-Za-z0-9+/=]+)@@/g;
          nodes.forEach(node=>{if(!pattern.test(node.nodeValue))return;pattern.lastIndex=0;const fragment=document.createDocumentFragment();let last=0,match;while((match=pattern.exec(node.nodeValue))!==null){fragment.append(document.createTextNode(node.nodeValue.slice(last,match.index)));const span=document.createElement('span');span.className=match[1]==='D'?'math-display':'math-inline';span.textContent=decode(match[2]);try{katex.render(span.textContent,span,{displayMode:match[1]==='D',throwOnError:false,strict:'warn',trust:false})}catch(_){}fragment.append(span);last=pattern.lastIndex}fragment.append(document.createTextNode(node.nodeValue.slice(last)));node.replaceWith(fragment)});
          mermaid.initialize({startOnLoad:false,securityLevel:'strict',theme:'dark',themeVariables:{primaryColor:'#29213d',primaryTextColor:'#f1f3f9',primaryBorderColor:'#8b5cf6',lineColor:'#9ba3b8',secondaryColor:'#181b28',tertiaryColor:'#202436'}});
          document.querySelectorAll('.mermaid,.mermaid-frame').forEach(async(frame,index)=>{const code=frame.querySelector('code'),source=(code||frame).textContent;try{const result=await mermaid.render('slateDiagram'+index,source);frame.innerHTML=result.svg;frame.classList.add('mermaid-ready')}catch(_){frame.classList.add('render-error');const label=document.createElement('div');label.className='render-error-label';label.textContent='Diagram could not be rendered';frame.prepend(label)}});
        })();
        """;

    private const string ReaderCss = """
        :root{color-scheme:dark}body{margin:0;padding:38px 48px 80px;font:16px/1.72 'Segoe UI',system-ui,sans-serif;color:#d8dce7;background:#0f1117;overflow-wrap:anywhere}main{max-width:860px;margin:auto}h1,h2,h3{line-height:1.25;letter-spacing:-.025em;color:#f1f3f9;scroll-margin-top:16px}h1{font-size:32px;margin:0 0 24px}h2{font-size:23px;margin-top:34px}h3{font-size:18px;margin-top:28px}a{color:#a78bfa;text-decoration-color:#6d4fc5;text-underline-offset:3px}img{display:block;max-width:100%;height:auto;margin:20px auto;border:1px solid #262b3d;border-radius:6px}pre{padding:18px 20px;border:1px solid #2d3145;border-radius:6px;overflow:auto;background:#141721}code{font:14px/1.65 Consolas,monospace}:not(pre)>code{background:#202436;padding:2px 5px;border-radius:3px;color:#c4b5fd}blockquote{margin:24px 0;padding:2px 20px;border-left:3px solid #8b5cf6;color:#aeb5c8}.callout,.markdown-alert{padding:10px 16px;border:1px solid #44336b;border-left:4px solid #8b5cf6;border-radius:5px;background:#181522}.callout-warning,.callout-important,.markdown-alert-warning,.markdown-alert-important{border-left-color:#f59e0b;background:#241d14}.callout-tip,.markdown-alert-tip{border-left-color:#34d399;background:#12211d}.markdown-alert-title{font-weight:700;margin:0}.mermaid,.mermaid-frame{max-width:100%;overflow:auto;touch-action:pan-x pan-y}.mermaid svg,.mermaid-frame svg{min-width:480px;max-width:none}.render-error-label{font:600 13px 'Segoe UI';color:#f87171;margin-bottom:8px}.math-display{display:block;overflow-x:auto;padding:10px 0}table{display:block;overflow-x:auto;border-collapse:collapse;width:100%;margin:24px 0}th,td{border:1px solid #2d3145;padding:10px 14px;text-align:left}th{background:#181b28;color:#f1f3f9}hr{border:0;border-top:1px solid #262b3d;margin:28px 0}li{margin:4px 0}input[type=checkbox]{accent-color:#8b5cf6}.footnotes{margin-top:36px;font-size:.9em;border-top:1px solid #262b3d}.backlinks{margin-top:44px;padding-top:18px;border-top:1px solid #262b3d}.backlinks h2{font-size:18px}.backlinks small{display:block;color:#64748b}@media(max-width:640px){body{padding:22px 18px 56px;font-size:15px}h1{font-size:27px}table{font-size:14px}}
        """;
}

using Android.Webkit;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace Slate.Lib.App;

internal static class AndroidNoteReader
{
    internal const string Origin = "https://slate-render.invalid/";

    internal static void Load(MauiWebView reader, string file, string? heading, Action<string> navigate)
    {
        if (reader.Handler?.PlatformView is not Android.Webkit.WebView native)
            throw new InvalidOperationException("The note reader is not ready yet.");
        native.Settings.AllowFileAccess = false;
        native.Settings.AllowContentAccess = false;
        native.Settings.JavaScriptEnabled = true;
        native.SetBackgroundColor(Android.Graphics.Color.Rgb(25, 27, 34));
        native.SetWebViewClient(new LocalReaderClient(Path.GetDirectoryName(file)!, navigate));
        native.LoadUrl(Origin + Uri.EscapeDataString(Path.GetFileName(file)) + (heading ?? ""));
    }

    private sealed class LocalReaderClient(string root, Action<string> navigate) : WebViewClient
    {
        public override WebResourceResponse? ShouldInterceptRequest(Android.Webkit.WebView? view, IWebResourceRequest? request)
        {
            var value = request?.Url?.ToString();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "slate-render.invalid")
                return new WebResourceResponse("text/plain", "UTF-8", new MemoryStream());
            try
            {
                var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
                var file = Path.GetFullPath(Path.Combine(root, relative));
                if (!file.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return new WebResourceResponse("text/plain", "UTF-8", new MemoryStream());
                var mime = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".html" => "text/html", ".css" => "text/css", ".js" => "application/javascript",
                    ".woff2" => "font/woff2", ".woff" => "font/woff", ".ttf" => "font/ttf", _ => null
                };
                if (mime is null) return new WebResourceResponse("text/plain", "UTF-8", new MemoryStream());
                return new WebResourceResponse(mime, "UTF-8", File.OpenRead(file));
            }
            catch (IOException) { return new WebResourceResponse("text/plain", "UTF-8", new MemoryStream()); }
            catch (ArgumentException) { return new WebResourceResponse("text/plain", "UTF-8", new MemoryStream()); }
        }

        public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString() ?? "";
            if (url.StartsWith(Origin, StringComparison.Ordinal)) return false;
            MainThread.BeginInvokeOnMainThread(() => navigate(url));
            return true;
        }
    }
}

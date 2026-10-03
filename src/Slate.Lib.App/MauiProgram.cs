using Slate.Lib.Core;

namespace Slate.Lib.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        ClientControls.Configure();
#if WINDOWS
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "slate.lib", "WebView2"));
#endif
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSingleton<LibraryApiClient>();
        builder.Services.AddSingleton<MarkdownReader>();
#if ANDROID
        builder.Services.AddSingleton<MobileSession>();
        builder.Services.AddSingleton<MobileRootPage>();
#else
        builder.Services.AddSingleton<MainPage>();
#endif
        return builder.Build();
    }
}

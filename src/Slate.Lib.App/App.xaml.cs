namespace Slate.Lib.App;

public partial class App : Application
{
#if ANDROID
    private readonly MobileRootPage mobile;
    public App(MobileRootPage mobile) { InitializeComponent(); UserAppTheme = AppTheme.Dark; this.mobile = mobile; }
    protected override Window CreateWindow(IActivationState? activationState) => new(mobile) { Title = "Slate" };
#else
    private readonly MainPage page;
    public App(MainPage page) { InitializeComponent(); UserAppTheme = AppTheme.Dark; this.page = page; }
    protected override Window CreateWindow(IActivationState? activationState) => new(page)
    {
        Title = "Slate", Width = 1440, Height = 900, MinimumWidth = 1100, MinimumHeight = 680
    };
#endif
}

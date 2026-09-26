namespace Slate.Lib.App;

public partial class App : Application
{
#if ANDROID
    private readonly MobileRootPage mobile;
    public App(MobileRootPage mobile) { InitializeComponent(); this.mobile = mobile; }
    protected override Window CreateWindow(IActivationState? activationState) => new(mobile) { Title = "slate.lib" };
#else
    private readonly MainPage page;
    public App(MainPage page) { InitializeComponent(); this.page = page; }
    protected override Window CreateWindow(IActivationState? activationState) => new(page)
    {
        Title = "slate.lib", Width = 1200, Height = 800, MinimumWidth = 850, MinimumHeight = 560
    };
#endif
}

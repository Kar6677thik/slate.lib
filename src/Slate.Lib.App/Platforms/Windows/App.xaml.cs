using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Slate.Lib.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		UnhandledException += (_, args) =>
		{
			try
			{
				File.WriteAllText(Path.Combine(Path.GetTempPath(), "slate-lib-unhandled.txt"), args.Exception.ToString());
			}
			catch
			{
				// Never let crash reporting obscure the original startup failure.
			}
		};
		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}


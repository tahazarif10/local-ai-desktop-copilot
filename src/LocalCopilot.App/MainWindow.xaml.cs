using LocalCopilot_App.Services;
using Microsoft.UI.Xaml;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace LocalCopilot_App;

/// <summary>
/// Hosts the diagnostic view while the application-owned coordinator retains
/// sensing composition and lifetime responsibility.
/// </summary>
public sealed partial class MainWindow : Window
{
    internal MainWindow(
        DesktopCopilotCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(
            coordinator);

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Content =
            new MainPage(
                coordinator);
    }
}

using LocalCopilot_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LocalCopilot_App;

public sealed partial class MainPage : Page
{
    private readonly ForegroundWindowService _foregroundWindowService = new();
    private readonly DispatcherTimer _timer = new();

    public MainPage()
    {
        InitializeComponent();

        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += Timer_Tick;

        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshForegroundWindow();
        _timer.Start();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
    }

    private void Timer_Tick(object? sender, object e)
    {
        RefreshForegroundWindow();
    }

    private void RefreshForegroundWindow()
    {
        ForegroundWindowSnapshot? snapshot =
            _foregroundWindowService.GetCurrent();

        if (snapshot is null)
            return;

        if (snapshot.ProcessId == Environment.ProcessId)
            return;

        ProcessNameText.Text = snapshot.ProcessName;

        WindowTitleText.Text =
            string.IsNullOrWhiteSpace(snapshot.WindowTitle)
                ? "(no title)"
                : snapshot.WindowTitle;

        ProcessIdText.Text =
            snapshot.ProcessId.ToString();

        WindowHandleText.Text =
            $"0x{snapshot.Handle.ToInt64():X}";

        LastObservedText.Text =
            DateTime.Now.ToString("HH:mm:ss.fff");
    }
}

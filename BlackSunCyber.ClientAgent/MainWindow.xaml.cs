using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Application = System.Windows.Application;

namespace BlackSunCyber.ClientAgent;

public partial class MainWindow : Window
{
    private AgentConfig _config = new();
    private AgentConnection? _connection;
    private DispatcherTimer? _heartbeatTimer;
    private DispatcherTimer? _localCountdownTimer;
    private string _currentNickname = string.Empty;
    private int _remainingSeconds;
    private bool _isStationLockedBySystem = true;
    private bool _cabinetVisible = true;
    private WidgetWindow? _widgetWindow;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadConfig();
        SystemLockHelper.SetTaskManagerDisabled(false);

        _connection = new AgentConnection(_config);
        _connection.OnShowNicknamePrompt += minutes =>
            Dispatcher.Invoke(() => ShowPendingState(minutes));
        _connection.OnUnlock += (nick, seconds) =>
            Dispatcher.Invoke(() => ShowActiveState(nick, seconds));
        _connection.OnTimeUpdated += seconds =>
            Dispatcher.Invoke(() => SyncTime(seconds));
        _connection.OnForceLock += () =>
            Dispatcher.Invoke(ShowLockedState);
        _connection.OnConnectionStateChanged += connected =>
            Dispatcher.Invoke(() => UpdateConnectionStatus(connected));

        _ = ConnectWithRetryAsync();

        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _heartbeatTimer.Tick += async (_, _) =>
        {
            if (_connection != null) await _connection.SendHeartbeatAsync();
        };
        _heartbeatTimer.Start();
    }

    private void LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "agentsettings.json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _config = JsonSerializer.Deserialize<AgentConfig>(json) ?? new AgentConfig();
        }
    }

    private async Task ConnectWithRetryAsync()
    {
        SystemLockHelper.SetTaskManagerDisabled(false);
        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await _connection!.ConnectAsync();
                Dispatcher.Invoke(() =>
                {
                    ConnectionStatusText.Text = "● Conectat la server";
                    ConnectionStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
                });
                break;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ConnectionStatusText.Text = $"Tentativa {attempt}: {ex.Message}";
                    ConnectionStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
                });
                await Task.Delay(TimeSpan.FromSeconds(4));
            }
        }
    }

    // ---------------------------------------------------------
    // STAREA 1: LOCKED
    // ---------------------------------------------------------
    private void ShowLockedState()
    {
        _localCountdownTimer?.Stop();
        _isStationLockedBySystem = true;
        _cabinetVisible = true;
        _widgetWindow?.StopTimer();
        _widgetWindow?.DeactivateWidgetMode();
        SystemLockHelper.SetTaskManagerDisabled(true);

        // Reset toggle cabinet button for next session
        ToggleCabinetBtn.Content = "▼  Ascunde cabinet";

        // Ensure the main window is fully visible and in foreground
        this.Show();
        this.Visibility = Visibility.Visible;
        this.WindowState = WindowState.Normal;   // Reset first
        this.WindowState = WindowState.Maximized; // Then maximize
        this.Topmost = true;
        this.Activate();
        this.Focus();

        LockedPanel.Visibility = Visibility.Visible;
        PendingPanel.Visibility = Visibility.Collapsed;
        ActivePanel.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------
    // STAREA 2: PENDING — asteapta nickname
    // ---------------------------------------------------------
    private void ShowPendingState(int minutes)
    {
        _isStationLockedBySystem = true;
        SystemLockHelper.SetTaskManagerDisabled(true);

        // Ensure the main window is fully visible and in foreground
        this.Show();
        this.Visibility = Visibility.Visible;
        this.WindowState = WindowState.Normal;   // Reset first
        this.WindowState = WindowState.Maximized; // Then maximize
        this.Topmost = true;
        this.Activate();
        this.Focus();

        LockedPanel.Visibility = Visibility.Collapsed;
        PendingPanel.Visibility = Visibility.Visible;
        ActivePanel.Visibility = Visibility.Collapsed;

        PendingMinutesText.Text = $"Ai alocate {minutes} minute";
        NicknameInput.Text = string.Empty;
        PendingErrorText.Visibility = Visibility.Collapsed;
        NicknameInput.Focus();
    }

    private async void ConfirmNicknameButton_Click(object sender, RoutedEventArgs e) =>
        await SubmitNickname();

    private async void NicknameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) await SubmitNickname();
    }

    private async Task SubmitNickname()
    {
        var nickname = NicknameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            PendingErrorText.Text = "Introdu un nickname valid.";
            PendingErrorText.Visibility = Visibility.Visible;
            return;
        }
        ConfirmNicknameButton.IsEnabled = false;
        try
        {
            await _connection!.SendNicknameAsync(nickname);
        }
        catch
        {
            PendingErrorText.Text = "Eroare de conexiune. Incearca din nou.";
            PendingErrorText.Visibility = Visibility.Visible;
        }
        finally { ConfirmNicknameButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------
    // STAREA 3: ACTIVE — cabinet complet cu WebView2
    // ---------------------------------------------------------
    private async void ShowActiveState(string nickname, int remainingSeconds)
    {
        _currentNickname = nickname;
        _remainingSeconds = remainingSeconds;
        _isStationLockedBySystem = false;
        _cabinetVisible = true;
        _widgetWindow?.DeactivateWidgetMode();

        // Task Manager reactivat — clientul poate folosi PC-ul normal
        SystemLockHelper.SetTaskManagerDisabled(false);

        this.Show();
        this.WindowState = WindowState.Maximized;
        this.Topmost = true;

        LockedPanel.Visibility = Visibility.Collapsed;
        PendingPanel.Visibility = Visibility.Collapsed;
        ActivePanel.Visibility = Visibility.Visible;

        // Bara de sus
        ActiveNickname.Text = $"Salut, {nickname}!";
        UpdateTimerDisplay(remainingSeconds);

        // Porneste countdown-ul local (1s)
        _localCountdownTimer?.Stop();
        _localCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _localCountdownTimer.Tick += (_, _) =>
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                UpdateTimerDisplay(_remainingSeconds);
            }
        };
        _localCountdownTimer.Start();

        // Initializeaza WebView2 si incarca portalul client
        // URL-ul portalului cu nickname-ul pre-completat (login automat prin QR)
        var clientUrl = $"{_config.ServerUrl}/client/?nick={Uri.EscapeDataString(nickname)}";
        try
        {
            await ClientWebView.EnsureCoreWebView2Async();
            ClientWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            ClientWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            ClientWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            ClientWebView.Source = new Uri(clientUrl);
        }
        catch (Exception ex)
        {
            // Daca WebView2 nu e disponibil, afisam un mesaj
            System.Windows.MessageBox.Show(
                $"Nu pot deschide cabinetul web: {ex.Message}\nInstalati Microsoft Edge WebView2 Runtime.",
                "BlackSun Cyber", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SyncTime(int seconds)
    {
        _remainingSeconds = seconds;
        UpdateTimerDisplay(seconds);
        _widgetWindow?.UpdateTime(seconds);
    }

    private void UpdateTimerDisplay(int seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        ActiveTimer.Text = ts.ToString(@"hh\:mm\:ss");
        ActiveTimer.Foreground = seconds < 300
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
    }

    // Ascunde cabinetul fullscreen si afiseaza widget-ul flotant (nu bara pe tot ecranul)
    private void ToggleCabinet_Click(object sender, RoutedEventArgs e)
    {
        if (_cabinetVisible)
            HideCabinetShowWidget();
        else
            ShowCabinetHideWidget();
    }

    private void HideCabinetShowWidget()
    {
        _cabinetVisible = false;
        ToggleCabinetBtn.Content = "▲ Arată cabinet";

        _widgetWindow ??= CreateWidgetWindow();
        _widgetWindow.Initialize(_currentNickname, _config.StationId, _remainingSeconds, _connection!);
        _widgetWindow.UpdateTime(_remainingSeconds);
        _widgetWindow.ActivateWidgetMode();

        Hide();
    }

    private void ShowCabinetHideWidget()
    {
        _cabinetVisible = true;
        ToggleCabinetBtn.Content = "▼ Ascunde cabinet";

        _widgetWindow?.DeactivateWidgetMode();

        Show();
        WindowState = WindowState.Maximized;
        Topmost = true;
        ClientWebView.Visibility = Visibility.Visible;
    }

    private WidgetWindow CreateWidgetWindow()
    {
        var widget = new WidgetWindow();
        widget.OnRequestFullScreenCabinet += () =>
            Dispatcher.Invoke(ShowCabinetHideWidget);
        return widget;
    }

    private void UpdateConnectionStatus(bool connected)
    {
        ConnectionStatusText.Text = connected ? "● Conectat la server" : "● Reconectare...";
        ConnectionStatusText.Foreground = connected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
    }

    // Blocare Alt+F4 cat timp e Locked/Pending
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isStationLockedBySystem)
        {
            e.Cancel = true;
            System.Windows.MessageBox.Show(
                "Sesiune inactiva! Achitati timpul la receptie pentru utilizare.",
                "BlackSun Cyber", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        base.OnClosing(e);
    }
}
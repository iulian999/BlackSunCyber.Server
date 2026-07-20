using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BlackSunCyber.ClientAgent;

public partial class WidgetWindow : Window
{
    private DispatcherTimer? _timer;
    private DispatcherTimer? _foregroundTimer;
    private int _remainingSeconds;
    private int _totalSeconds;
    private string _nickname = string.Empty;
    private int _stationId;
    private AgentConnection? _connection;
    private bool _isDragging;
    private System.Windows.Point _dragStart;
    private bool _isExpanded = false;
    private bool _webViewInitialized = false;

    public event Action? OnRequestFullScreenCabinet;

    // Dimensiuni
    private const double MiniW = 220, MiniH = 54;
    private const double ExpandW = 420, ExpandH = 604;

    public WidgetWindow()
    {
        InitializeComponent();
        this.Width = MiniW;
        this.Height = MiniH;
        this.Opacity = 0.85;
        PositionToCenter();
    }

    public void Initialize(string nickname, int stationId, int remainingSeconds, AgentConnection connection)
    {
        _nickname = nickname;
        _stationId = stationId;
        _remainingSeconds = remainingSeconds;
        _totalSeconds = remainingSeconds > 0 ? remainingSeconds : 1;
        _connection = connection;

        WelcomeText.Text = $"Salut, {nickname}!";
        UpdateDisplay();

        // Stop any existing timer before starting a new one
        _timer?.Stop();
        StartTimer();
    }

    public void UpdateTime(int seconds)
    {
        _remainingSeconds = seconds;
        Dispatcher.Invoke(UpdateDisplay);
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _foregroundTimer?.Stop();
    }

    public void ActivateWidgetMode()
    {
        if (_isExpanded) Collapse();
        PositionToCenter();
        StartForegroundMonitor();
        ApplyDesktopVisibility();
    }

    public void DeactivateWidgetMode()
    {
        _foregroundTimer?.Stop();
        if (_isExpanded) Collapse();
        HideWidgetCompletely();
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            if (_remainingSeconds > 0) { _remainingSeconds--; UpdateDisplay(); }
        };
        _timer.Start();
    }

    private void UpdateDisplay()
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, _remainingSeconds));
        var str = ts.ToString(@"hh\:mm\:ss");
        MiniTimer.Text = str;
        ExpandedTimer.Text = str;

        System.Windows.Media.Color c;
        if (_remainingSeconds < 300)
            c = System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C);
        else if (_remainingSeconds < 900)
            c = System.Windows.Media.Color.FromRgb(0xF3, 0x9C, 0x12);
        else
            c = System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71);

        var brush = new System.Windows.Media.SolidColorBrush(c);
        MiniTimer.Foreground = brush;
        ExpandedTimer.Foreground = brush;
        StatusDot.Fill = brush;
    }

    // ---- HOVER pe mini widget ----
    private void Mini_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        this.Opacity = 1.0;
        ExpandIcon.Visibility = Visibility.Visible;
    }

    private void Mini_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        this.Opacity = 0.85;
        ExpandIcon.Visibility = Visibility.Collapsed;
    }

    // ---- CLICK pe mini widget ----
    private void Mini_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                // Dublu-click = expandeaza cabinetul
                ToggleExpanded();
                return;
            }
            // Single click = drag
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            ShowContextMenu();
        }
    }

    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        Collapse();
    }

    private void ReturnToMainCabinet_Click(object sender, RoutedEventArgs e)
    {
        // Revine la cabinetul personal principal (MainWindow fullscreen)
        OnRequestFullScreenCabinet?.Invoke();
    }

    private async Task InitWebView()
    {
        _webViewInitialized = true;
        try
        {
            await CabinetWebView.EnsureCoreWebView2Async();
            CabinetWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CabinetWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            CabinetWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            var url = $"{_connection!.ServerUrl}/client/?nick={Uri.EscapeDataString(_nickname)}";
            CabinetWebView.Source = new Uri(url);
        }
        catch { Collapse(); }
    }

    private void ToggleExpanded()
    {
        if (_isExpanded) Collapse();
        else Expand();
    }

    private async void Expand()
    {
        ExpandedView.CornerRadius = new CornerRadius(18);
        _isExpanded = true;

        // Schimba dimensiunile
        MiniView.Visibility = Visibility.Collapsed;
        ExpandedView.Visibility = Visibility.Visible;
        this.Width = ExpandW;
        this.Height = ExpandH;
        PositionToCenter();

        // Initializeaza WebView2 prima oara
        if (!_webViewInitialized && _connection != null)
        {
            _webViewInitialized = true;
            try
            {
                await CabinetWebView.EnsureCoreWebView2Async();
                CabinetWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                CabinetWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                CabinetWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                var url = $"{_connection.ServerUrl}/client/?nick={Uri.EscapeDataString(_nickname)}";
                CabinetWebView.Source = new Uri(url);
            }
            catch
            {
                // WebView2 nu e disponibil - afisam mesaj in widget
                System.Windows.MessageBox.Show(
                    "WebView2 nu este instalat.\nInstalati de la: https://developer.microsoft.com/microsoft-edge/webview2/",
                    "BlackSun Cyber", MessageBoxButton.OK, MessageBoxImage.Warning);
                Collapse();
            }
        }
    }

    private void Collapse()
    {
        _isExpanded = false;
        ExpandedView.Visibility = Visibility.Collapsed;
        MiniView.Visibility = Visibility.Visible;
        this.Width = MiniW;
        this.Height = MiniH;
        PositionToCenter();
    }

    // ---- MENIU CONTEXTUAL ----
    private void ShowContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "⊞  Deschide cabinet personal" };
        openItem.Click += (_, _) => { if (!_isExpanded) Expand(); };

        var closeItem = new System.Windows.Controls.MenuItem { Header = "⊟  Minimizează cabinetul" };
        closeItem.Click += (_, _) => { if (_isExpanded) Collapse(); };

        var fullItem = new System.Windows.Controls.MenuItem { Header = "⛶  Cabinet pe tot ecranul" };
        fullItem.Click += (_, _) => OnRequestFullScreenCabinet?.Invoke();

        var centerItem = new System.Windows.Controls.MenuItem { Header = "◎  Centrează pe ecran" };
        centerItem.Click += (_, _) => PositionToCenter();

        menu.Items.Add(openItem);
        menu.Items.Add(closeItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(fullItem);
        menu.Items.Add(centerItem);
        menu.IsOpen = true;
    }

    // ---- DRAG ----
    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging)
        {
            var pos = e.GetPosition(this);
            Left += pos.X - _dragStart.X;
            Top += pos.Y - _dragStart.Y;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
        base.OnMouseUp(e);
    }

    private void PositionToCenter()
    {
        var screen = SystemParameters.WorkArea;
        var w = _isExpanded ? ExpandW : MiniW;
        var h = _isExpanded ? ExpandH : MiniH;
        // Sus-centru in loc de mijlocul ecranului
        Left = screen.Left + (screen.Width - w) / 2;
        Top = screen.Top + 12; // 12px de la marginea de sus
    }

    private void StartForegroundMonitor()
    {
        _foregroundTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _foregroundTimer.Tick -= ForegroundTimer_Tick;
        _foregroundTimer.Tick += ForegroundTimer_Tick;
        _foregroundTimer.Start();
        ApplyDesktopVisibility();
    }

    private void ForegroundTimer_Tick(object? sender, EventArgs e) => ApplyDesktopVisibility();

    private void ApplyDesktopVisibility()
    {
        if (ForegroundMonitorHelper.ShouldShowSessionWidget())
            ShowWidgetOnDesktop();
        else
            HideWidgetCompletely();
    }

    private void ShowWidgetOnDesktop()
    {
        if (!_isExpanded)
            PositionToCenter();

        Topmost = true;
        Show();
        Visibility = Visibility.Visible;
    }

    private void HideWidgetCompletely()
    {
        if (_isExpanded)
            Collapse();

        Topmost = false;
        Hide();
    }
}
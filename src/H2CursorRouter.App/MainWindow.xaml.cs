using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using H2CursorRouter.App.ViewModels;
using H2CursorRouter.Windows;
using Forms = System.Windows.Forms;

namespace H2CursorRouter.App;

public partial class MainWindow : Window
{
    private const int EmergencyHotkeyId = 1;
    private const int ProfileHotkeyStartId = 100;
    private readonly MainViewModel _viewModel;
    private readonly IHotkeyService _hotkeyService;
    private readonly bool _startInTray;
    private readonly List<int> _registeredProfileHotkeys = new();
    private HwndSource? _source;
    private Forms.NotifyIcon? _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private bool _allowExit;
    private bool _isInitialDashboardSelection = true;
    private bool _hideToTrayAfterFirstRender;
    private bool _restoreRequested;
    private bool _exitForUpdate;

    public MainWindow(MainViewModel viewModel, IHotkeyService hotkeyService, bool startInTray)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hotkeyService = hotkeyService;
        _startInTray = startInTray;
        _viewModel.HotkeysChanged += OnHotkeysChanged;
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        if (!_hotkeyService.RegisterHotkey(handle, EmergencyHotkeyId, HotkeyGesture.CtrlAltShiftEsc))
        {
            _viewModel.AddLog("Failed to register emergency hotkey Ctrl+Alt+Shift+Esc.");
        }

        RegisterProfileHotkeys(handle);
        InitializeTrayIcon();
        _ = _viewModel.RefreshDashboardStatusAsync();

        if (_startInTray && !_restoreRequested)
        {
            _hideToTrayAfterFirstRender = true;
        }
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (!_hideToTrayAfterFirstRender)
        {
            return;
        }

        _hideToTrayAfterFirstRender = false;
        HideToTray();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeyService.IsHotkeyMessage(hwnd, msg, wParam, lParam, out var id))
        {
            handled = true;
            if (id == EmergencyHotkeyId)
            {
                _viewModel.EmergencyUnlock();
            }
            else if (id >= ProfileHotkeyStartId)
            {
                _ = _viewModel.ExecuteProfileByIndexAsync(id - ProfileHotkeyStartId);
            }
        }

        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exitForUpdate && _viewModel.Updates?.CanUseApp == false)
        {
            e.Cancel = true;
            return;
        }
        if (!_allowExit)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _viewModel.HotkeysChanged -= OnHotkeysChanged;
        _source?.RemoveHook(WndProc);
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
        _viewModel.Shutdown();
    }

    private void OnHotkeysChanged(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        RegisterProfileHotkeys(handle);
    }

    private void RegisterProfileHotkeys(IntPtr handle)
    {
        foreach (var id in _registeredProfileHotkeys)
        {
            _hotkeyService.UnregisterHotkey(handle, id);
        }

        _registeredProfileHotkeys.Clear();
        for (var i = 0; i < _viewModel.Profiles.Count; i++)
        {
            var profile = _viewModel.Profiles[i];
            if (HotkeyParser.TryParse(profile.Hotkey, out var gesture))
            {
                var id = ProfileHotkeyStartId + i;
                if (_hotkeyService.RegisterHotkey(handle, id, gesture))
                {
                    _registeredProfileHotkeys.Add(id);
                }
                else
                {
                    _viewModel.AddLog($"Failed to register profile hotkey '{profile.Hotkey}' for '{profile.Name}'.");
                }
            }
        }
    }

    private void InitializeTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Stop Routing", null, (_, _) => _viewModel.StopRoutingCommand.Execute(null));
        menu.Items.Add("Emergency Unlock", null, (_, _) => _viewModel.EmergencyUnlock());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "vp-cursor-portal",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute));
        if (resource is not null)
        {
            return new System.Drawing.Icon(resource.Stream);
        }

        var executableIcon = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath)
            : null;
        return executableIcon ?? (System.Drawing.Icon)SystemIcons.Application.Clone();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _viewModel.AddLog("Window hidden to tray; routing state is unchanged.");
    }

    internal void ShowFromTray()
    {
        _restoreRequested = true;
        _hideToTrayAfterFirstRender = false;
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    internal void ExitForUpdate()
    {
        _exitForUpdate = true;
        _allowExit = true;
        Close();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
        {
            return;
        }

        if (MainTabs.SelectedIndex == 0)
        {
            if (_isInitialDashboardSelection)
            {
                _isInitialDashboardSelection = false;
                return;
            }

            _ = _viewModel.RefreshDashboardStatusAsync();
        }
        else if (MainTabs.SelectedIndex == 4)
        {
            _viewModel.RefreshDisplays();
        }
    }

    private void EditProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is ProfileRow profile)
        {
            _viewModel.SelectedProfile = profile;
            _viewModel.EditProfile(profile);
        }
    }

    private void ProfilesGrid_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.Controls.DataGrid)?.SelectedItem is ProfileRow profile)
        {
            _viewModel.EditProfile(profile);
        }
    }

    private void ZoneMoveThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZoneRow zone)
        {
            _viewModel.MoveZoneVisual(zone, e.HorizontalChange, e.VerticalChange);
        }
    }

    private void ZoneMoveThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZoneRow zone)
        {
            _viewModel.CompleteZoneVisualEdit(zone);
        }
    }

    private void ZoneResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZoneRow zone)
        {
            _viewModel.ResizeZoneVisual(zone, e.HorizontalChange, e.VerticalChange);
        }
    }

    private void ZoneVisual_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ZoneRow zone)
        {
            _viewModel.SelectedZone = zone;
            e.Handled = false;
        }
    }

    private void LayoutNumberTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        if (textBox.DataContext is MainViewModel { SelectedZone: not null } viewModel)
        {
            viewModel.CompleteZoneVisualEdit(viewModel.SelectedZone);
        }

        e.Handled = true;
    }

    private void CanvasScrollViewer_OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        LayoutTabScrollViewer.ScrollToVerticalOffset(LayoutTabScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}

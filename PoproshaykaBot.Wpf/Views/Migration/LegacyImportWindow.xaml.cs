using PoproshaykaBot.Wpf.ViewModels.Migration;
using System.Windows;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views.Migration;

public partial class LegacyImportWindow : Window
{
    private readonly LegacyImportViewModel _viewModel;
    private bool _fitScheduled;

    public LegacyImportWindow(LegacyImportViewModel viewModel, Window? owner = null)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        WindowChromeTheming.Attach(this);

        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LocationChanged += OnLocationChanged;
        DpiChanged += OnDpiChanged;
        SizeChanged += (_, _) => ScheduleFit();
        Loaded += (_, _) => ScheduleFit();
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var ownerHandle = Owner is null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
        ApplyWorkArea(ownerHandle, VisualTreeHelper.GetDpi(Owner ?? this));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel |= _viewModel.IsBusy;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        ApplyWorkArea(IntPtr.Zero, VisualTreeHelper.GetDpi(this));
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        ApplyWorkArea(IntPtr.Zero, e.NewDpi);
        ScheduleFit();
    }

    private void ApplyWorkArea(IntPtr anchor, DpiScale dpi)
    {
        var handle = anchor == IntPtr.Zero ? new WindowInteropHelper(this).Handle : anchor;
        if (!TryGetWorkArea(handle, out var info))
        {
            return;
        }

        SetCurrentValue(MaxWidthProperty, Math.Floor((info.WorkRight - info.WorkLeft) / dpi.DpiScaleX));
        SetCurrentValue(MaxHeightProperty, Math.Floor((info.WorkBottom - info.WorkTop) / dpi.DpiScaleY));
    }

    private static bool TryGetWorkArea(IntPtr handle, out MonitorInfo info)
    {
        const int MonitorDefaultToNearest = 2;
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = handle == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(handle, MonitorDefaultToNearest);
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
    }

    private void ScheduleFit()
    {
        if (_fitScheduled)
        {
            return;
        }

        _fitScheduled = true;
        _ = Dispatcher.BeginInvoke(FitInsideWorkArea, DispatcherPriority.Loaded);
    }

    private void FitInsideWorkArea()
    {
        _fitScheduled = false;
        if (!IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (!TryGetWorkArea(handle, out var work) || !GetWindowRect(handle, out var window))
        {
            return;
        }

        var left = Math.Clamp(window.Left, work.WorkLeft, Math.Max(work.WorkLeft, work.WorkRight - (window.Right - window.Left)));
        var top = Math.Clamp(window.Top, work.WorkTop, Math.Max(work.WorkTop, work.WorkBottom - (window.Bottom - window.Top)));
        if (left != window.Left || top != window.Top)
        {
            const uint NoSizeNoZOrderNoActivate = 0x0015;
            _ = SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, NoSizeNoZOrderNoActivate);
        }
    }

    private void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceList.SelectedItem is { } selected)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (SourceList.ItemContainerGenerator.ContainerFromItem(selected) is FrameworkElement card)
                {
                    card.BringIntoView();
                }
            }, DispatcherPriority.Loaded);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LegacyImportViewModel.IsCompleted) && _viewModel.IsCompleted)
        {
            ContentScroll.ScrollToTop();
            ContinueButton.Focus();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public int Flags;
    }
}

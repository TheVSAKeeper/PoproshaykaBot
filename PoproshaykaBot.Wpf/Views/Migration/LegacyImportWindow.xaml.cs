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

    public LegacyImportWindow(LegacyImportViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        LocationChanged += OnLocationChanged;
        DpiChanged += OnDpiChanged;
        Closed += OnClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var ownerHandle = Owner is null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
        if (ownerHandle != IntPtr.Zero)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        ApplyWorkArea(ownerHandle, VisualTreeHelper.GetDpi(this));
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
    }

    private void ApplyWorkArea(IntPtr anchor, DpiScale dpi)
    {
        var handle = anchor == IntPtr.Zero ? new WindowInteropHelper(this).Handle : anchor;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        const int MonitorDefaultToNearest = 2;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        SetCurrentValue(MaxWidthProperty, Math.Floor((info.WorkRight - info.WorkLeft) / dpi.DpiScaleX));
        SetCurrentValue(MaxHeightProperty, Math.Floor((info.WorkBottom - info.WorkTop) / dpi.DpiScaleY));
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

using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Globalization;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class StreamPreviewTileViewModel : DashboardTileViewModel, IDisposable
{
    private const int MinDecodeWidth = 320;
    private const int MaxDecodeWidth = 1920;
    private const int DecodeWidthStep = 64;
    private const double GrowthFactor = 1.5;

    private readonly IStreamStatus _stream;
    private readonly List<IDisposable> _subs = [];
    private string? _lastThumbnailRawUrl;

    [ObservableProperty]
    private BitmapImage? _thumbnail;

    public StreamPreviewTileViewModel(IStreamStatus stream, IEventBus bus)
        : base("stream-preview", "Превью стрима", maxHeight: 280)
    {
        ArgumentNullException.ThrowIfNull(bus);

        _stream = stream;

        _subs.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => ApplyCurrentStatus()));
        _subs.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => ApplyCurrentStatus()));
        _subs.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => ApplyCurrentStatus()));
        _subs.Add(bus.SubscribeOnUi<ChannelUpdated>(_ => ApplyCurrentStatus()));

        ApplyCurrentStatus();
    }

    public override PackIconLucideKind Icon => PackIconLucideKind.Image;

    public override bool SizesToContent => true;

    public int DecodeWidth { get; private set; } = MinDecodeWidth;

    public void FitTo(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= DecodeWidth || DecodeWidth >= MaxDecodeWidth)
        {
            return;
        }

        var wanted = Math.Max(pixelWidth, DecodeWidth * GrowthFactor);
        var rounded = (int)Math.Ceiling(wanted / DecodeWidthStep) * DecodeWidthStep;

        DecodeWidth = Math.Min(rounded, MaxDecodeWidth);

        if (_lastThumbnailRawUrl is { } raw)
        {
            Decode(raw);
        }
    }

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }

    private void ApplyCurrentStatus()
    {
        if (_stream.CurrentStatus == StreamStatus.Online && _stream.CurrentStream is { IsBare: false } info)
        {
            LoadThumbnail(info.ThumbnailUrl);
            return;
        }

        Thumbnail = null;
        _lastThumbnailRawUrl = null;
    }

    private void LoadThumbnail(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            Thumbnail = null;
            _lastThumbnailRawUrl = null;
            return;
        }

        if (raw == _lastThumbnailRawUrl)
        {
            return;
        }

        _lastThumbnailRawUrl = raw;

        Decode(raw);
    }

    private void Decode(string raw)
    {
        var height = (int)Math.Round(DecodeWidth * 9.0 / 16.0);
        var resolved = raw
            .Replace("{width}", DecodeWidth.ToString(CultureInfo.InvariantCulture))
            .Replace("{height}", height.ToString(CultureInfo.InvariantCulture));

        var uri = new Uri(resolved, UriKind.Absolute);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = uri;
        bmp.DecodePixelWidth = DecodeWidth;
        bmp.CreateOptions = BitmapCreateOptions.None;
        bmp.CacheOption = BitmapCacheOption.Default;
        bmp.EndInit();
        Thumbnail = bmp;
    }
}

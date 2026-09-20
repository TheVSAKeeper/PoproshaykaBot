using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class StreamPreviewTileViewModel : DashboardTileViewModel, IDisposable
{
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

        // TODO: картинка декодируется в 320 px и на плитке шире этого мылит; считать ширину по слоту,
        //  когда пожалуются на качество превью либо когда плитке по факту станут давать заметно
        //  больше 320 DIP
        const int decodeWidth = 320;
        var height = (int)Math.Round(decodeWidth * 9.0 / 16.0);
        var resolved = raw
            .Replace("{width}", decodeWidth.ToString())
            .Replace("{height}", height.ToString());

        var uri = new Uri(resolved, UriKind.Absolute);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = uri;
        bmp.DecodePixelWidth = decodeWidth;
        bmp.CreateOptions = BitmapCreateOptions.None;
        bmp.CacheOption = BitmapCacheOption.Default;
        bmp.EndInit();
        Thumbnail = bmp;
    }
}

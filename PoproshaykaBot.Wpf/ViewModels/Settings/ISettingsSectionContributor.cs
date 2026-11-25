using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Platforms;
using System.ComponentModel;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record SettingsSectionBlockDescriptor(
    string Key,
    string Title,
    string? Hint,
    object Content) : INotifyPropertyChanged
{
    public StreamingPlatform? Platform { get; init; }

    public ICommand? ResetCommand { get; init; }

    public string? ResetHint { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }
}

public sealed record SettingsSectionPresentation(
    SettingsSection Section,
    IReadOnlyList<SettingsSectionBlockDescriptor> Blocks,
    bool IsFullWidth,
    SettingsSubsection? Group = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public bool HasMultipleBlocks => Blocks.Count > 1 || Section.Children.Count > 0;

    public string Title => Group?.Title ?? Section.Title;
}

public sealed record SettingsSectionEvent(
    IEvent Payload,
    Func<IEventBus, Task> PublishAsync)
{
    public static SettingsSectionEvent Create<TEvent>(TEvent @event)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        return new(@event, bus => bus.PublishAsync(@event));
    }
}

public sealed record SettingsSectionSaveOutcome(
    bool Written,
    IReadOnlyList<SettingsSectionEvent> Events,
    bool RequiresHttpServerRestart = false,
    string? FailureMessage = null)
{
    public static readonly SettingsSectionSaveOutcome Clean = new(true, []);

    public static SettingsSectionSaveOutcome FromWrite(bool written)
    {
        return new(written, []);
    }

    public static SettingsSectionSaveOutcome FromWrite<TEvent>(bool written, TEvent @event)
        where TEvent : IEvent
    {
        return new(written, [SettingsSectionEvent.Create(@event)]);
    }

    public IEnumerable<TEvent> EventsOfType<TEvent>()
        where TEvent : IEvent
    {
        return Events.Select(e => e.Payload).OfType<TEvent>();
    }
}

public interface ISettingsSectionContributor
{
    int Order { get; }

    string SectionKey { get; }

    bool IsDraftless => false;

    bool IsFullWidth => false;

    SettingsSection CreateSection();

    IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks();

    void Load(SettingsSessionState session);

    bool HasExternalChanges => false;

    IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState);

    string? ValidateBeforeSave() => null;

    void BeginSave(AppSettings targetSettings);

    Task<IReadOnlyList<SettingsSectionEvent>> OnAppSettingsSavedAsync(bool appSettingsWritten)
    {
        return Task.FromResult<IReadOnlyList<SettingsSectionEvent>>([]);
    }

    Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten);

    IReadOnlyList<SettingsSectionEvent> CollectPostSaveEvents(AppSettings targetSettings)
    {
        return [];
    }

    void OnSectionShown()
    {
    }
}

using KeepShell.Services;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class ObsChatSettingsSectionTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("obs-chat-settings-section");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void OverlayThemeOptions_ContainsClassicAndFlow()
    {
        var vm = CreateViewModel(out _, out _);

        Assert.That(vm.OverlayThemeOptions, Is.EqualTo(
        [
            new OverlayThemeOption(ObsOverlayTheme.Classic, "Классический"),
            new OverlayThemeOption(ObsOverlayTheme.Flow, "Обтекаемый (Flow)"),
        ]));
    }

    [Test]
    public void LoadFrom_NormalizesNullAndUnknownToClassic_AndPreservesFlow()
    {
        var vm = CreateViewModel(out _, out _);

        vm.LoadFrom(new() { OverlayTheme = null! });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Classic));
            Assert.That(vm.IsFlowTheme, Is.False);
        }

        vm.LoadFrom(new() { OverlayTheme = "neon" });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Classic));
            Assert.That(vm.IsFlowTheme, Is.False);
        }

        vm.LoadFrom(new() { OverlayTheme = "  FLOW  " });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Flow));
            Assert.That(vm.IsFlowTheme, Is.True);
        }
    }

    [Test]
    public void ApplyTo_AndSave_RoundTripOverlayTheme()
    {
        var vm = CreateViewModel(out var obsChatStore, out _);

        vm.OverlayTheme = ObsOverlayTheme.Flow;

        var target = new ObsChatSettings();
        vm.ApplyTo(target);
        vm.Save();
        var reloaded = obsChatStore.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Flow));
            Assert.That(reloaded.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Flow));
        }
    }

    [Test]
    public void ResetAllCommand_ResetsOverlayThemeToClassic()
    {
        var vm = CreateViewModel(out _, out _);
        vm.LoadFrom(new() { OverlayTheme = ObsOverlayTheme.Flow });
        Assert.That(vm.IsFlowTheme, Is.True);

        vm.ResetAllCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Classic));
            Assert.That(vm.IsFlowTheme, Is.False);
        }
    }

    [Test]
    public void IsFlowTheme_TracksOverlayTheme_AndRaisesPropertyChanged()
    {
        var vm = CreateViewModel(out _, out _);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.OverlayTheme = ObsOverlayTheme.Flow;
        var isFlowAfterSet = vm.IsFlowTheme;

        vm.OverlayTheme = ObsOverlayTheme.Classic;
        var isFlowAfterReset = vm.IsFlowTheme;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isFlowAfterSet, Is.True);
            Assert.That(isFlowAfterReset, Is.False);
            Assert.That(changed.Count(name => name == nameof(ObsChatSettingsSectionViewModel.IsFlowTheme)), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Contributor_DirtyTrackingAndSave_HandleOverlayTheme_AndExposeSearchKeywords()
    {
        var vm = CreateViewModel(out var obsChatStore, out var settingsManager);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var gate = new SettingsWriteGate();
        var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
        var kestrel = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
        var obsIntegration = new ObsIntegrationSectionViewModel(null!, settingsManager, NullLogger<ObsIntegrationSectionViewModel>.Instance, dialogs, null!);
        var obsIntegrationStore = new ObsIntegrationStore(bus, null, Path.Combine(_directory.FullName, "obs-integration.json"), gate);
        var contributor = new ObsSettingsSectionContributor(
            obsIntegration,
            vm,
            new(kestrel, dialogs, null!),
            obsIntegrationStore,
            obsChatStore,
            dialogs);

        var session = new SettingsSessionState { Settings = settingsManager.Current };
        contributor.Load(session);

        var dirtyCount = 0;
        using var sub = contributor.SubscribeDirty(session, () => dirtyCount++, () => { });

        vm.OverlayTheme = ObsOverlayTheme.Flow;
        contributor.BeginSave(settingsManager.Current);
        var outcome = await contributor.SaveAsync(settingsManager.Current, appSettingsWritten: true);
        var saved = obsChatStore.Load();
        var section = contributor.CreateSection();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dirtyCount, Is.EqualTo(1));
            Assert.That(outcome.Written, Is.True);
            Assert.That(saved.OverlayTheme, Is.EqualTo(ObsOverlayTheme.Flow));
            Assert.That(section.Matches(["тема"]), Is.True);
            Assert.That(section.Matches(["flow"]), Is.True);
            Assert.That(section.Matches(["обтекаемый"]), Is.True);
        }
    }

    private ObsChatSettingsSectionViewModel CreateViewModel(out ObsChatStore obsChatStore, out SettingsManager settingsManager)
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var gate = new SettingsWriteGate();
        settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"), gate);
        obsChatStore = new ObsChatStore(bus, null, Path.Combine(_directory.FullName, "obs-chat.json"), gate);
        var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();

        return new(obsChatStore, settingsManager, dialogs, null!);
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod is not null
                && targetMethod.ReturnType != typeof(void)
                && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}

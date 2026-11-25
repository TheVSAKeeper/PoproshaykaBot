using KeepShell.Updates;
using PoproshaykaBot.Core.Infrastructure;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateEnvironment : IUpdateEnvironment
{
    private const string UpdatableMetadataKey = "UpdatableBuild";
    private const string RepositoryMetadataKey = "GitHubRepository";
    private const string DefaultRepositorySlug = "MaxNagibator/PoproshaykaBot";

    private readonly string? _executablePath;

    public UpdateEnvironment()
        : this(
            ResolveKind(GetEntryOrCurrentAssembly(), AppPaths.IsPortable),
            ResolveRepositorySlug(GetEntryOrCurrentAssembly()),
            ResolveCurrentVersion(GetEntryOrCurrentAssembly()),
            ResolveRuntimeMajor(GetEntryOrCurrentAssembly()),
            null,
            null)
    {
    }

    internal UpdateEnvironment(
        UpdateKind kind,
        string repositorySlug,
        Version currentVersion,
        int runtimeMajor,
        string? executablePath = null,
        string? architecture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositorySlug);
        ArgumentNullException.ThrowIfNull(currentVersion);

        Kind = kind;
        RepositorySlug = repositorySlug;
        CurrentVersion = UpdateVersioning.Normalize(currentVersion);
        RuntimeMajor = runtimeMajor;
        _executablePath = executablePath;
        Architecture = string.IsNullOrWhiteSpace(architecture) ? ResolveArchitecture() : architecture;
    }

    public UpdateKind Kind { get; }

    KeepShell.Updates.UpdateKind KeepShell.Updates.IUpdateEnvironment.Kind => Kind == UpdateKind.Unsupported
        ? KeepShell.Updates.UpdateKind.None
        : KeepShell.Updates.UpdateKind.SingleFile;

    public bool SelfContained => Kind == UpdateKind.Portable;

    public string RepositorySlug { get; }

    public Version CurrentVersion { get; }

    public int RuntimeMajor { get; }

    public string Architecture { get; }

    public string ExecutablePath => _executablePath
        ?? Environment.ProcessPath
        ?? throw new UpdateException("Не удалось определить путь к исполняемому файлу.");

    public string StagingDirectory => GetStagingDirectory(ExecutablePath);

    public static Version ResolveCurrentVersion()
    {
        return ResolveCurrentVersion(GetEntryOrCurrentAssembly());
    }

    public static string GetStagingDirectory(string executablePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);

        var directory = Path.GetDirectoryName(executablePath)
            ?? throw new UpdateException("Не удалось определить каталог исполняемого файла.");

        return Path.Combine(directory, KeepShell.Updates.UpdateEnvironment.StagingFolderName);
    }

    internal static UpdateKind ResolveKind(Assembly assembly, bool isPortable)
    {
        var isUpdatableBuild = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => string.Equals(a.Key, UpdatableMetadataKey, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));

        if (!isUpdatableBuild)
        {
            return UpdateKind.Unsupported;
        }

        return isPortable ? UpdateKind.Portable : UpdateKind.FrameworkDependent;
    }

    internal static string ResolveRepositorySlug(Assembly assembly)
    {
        var slug = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, RepositoryMetadataKey, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

        return UpdateRepository.IsValidSlug(slug) ? slug! : DefaultRepositorySlug;
    }

    internal static Version ResolveCurrentVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (UpdateVersioning.TryParseTag(informational, out var fromInformational))
        {
            return fromInformational;
        }

        return UpdateVersioning.Normalize(assembly.GetName().Version ?? new Version(0, 0));
    }

    private static Assembly GetEntryOrCurrentAssembly()
    {
        return Assembly.GetEntryAssembly() ?? typeof(UpdateEnvironment).Assembly;
    }

    private static int ResolveRuntimeMajor(Assembly assembly)
    {
        var frameworkName = assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName;

        return UpdateRuntime.ParseMajor(frameworkName) ?? Environment.Version.Major;
    }

    private static string ResolveArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            var other => other.ToString().ToLowerInvariant(),
        };
    }
}

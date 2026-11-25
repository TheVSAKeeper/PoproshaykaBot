using PoproshaykaBot.Core.Update;
using System.Reflection;
using System.Reflection.Emit;
using KeepUpdateKind = KeepShell.Updates.UpdateKind;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateEnvironmentTests
{
    [TestCase(true, true, UpdateKind.Portable)]
    [TestCase(true, false, UpdateKind.FrameworkDependent)]
    [TestCase(false, true, UpdateKind.Unsupported)]
    [TestCase(false, false, UpdateKind.Unsupported)]
    public void ResolveKind_MapsUpdatableMetadataAndPortableFlag(
        bool updatableBuild,
        bool isPortable,
        UpdateKind expected)
    {
        var assembly = CreateDynamicAssembly(
            updatableBuild ? "true" : null,
            null);

        var resolved = UpdateEnvironment.ResolveKind(assembly, isPortable);

        Assert.That(resolved, Is.EqualTo(expected));
    }

    [TestCase("fork/PoproshaykaBot", "fork/PoproshaykaBot")]
    [TestCase("  owner/repo  ", "owner/repo")]
    [TestCase("invalid slug", "MaxNagibator/PoproshaykaBot")]
    [TestCase(null, "MaxNagibator/PoproshaykaBot")]
    public void ResolveRepositorySlug_ValidatesAndFallsBackToDefault(
        string? metadataSlug,
        string expected)
    {
        var assembly = CreateDynamicAssembly(null, metadataSlug);

        var resolved = UpdateEnvironment.ResolveRepositorySlug(assembly);

        Assert.That(resolved, Is.EqualTo(expected));
    }

    [TestCase(UpdateKind.Unsupported, KeepUpdateKind.None, false)]
    [TestCase(UpdateKind.Portable, KeepUpdateKind.SingleFile, true)]
    [TestCase(UpdateKind.FrameworkDependent, KeepUpdateKind.SingleFile, false)]
    public void KeepShellInterface_MapsKindAndSelfContained(
        UpdateKind botKind,
        KeepUpdateKind expectedKeepKind,
        bool expectedSelfContained)
    {
        var env = new UpdateEnvironment(
            botKind,
            "MaxNagibator/PoproshaykaBot",
            Version.Parse("3.1.0.15"),
            10,
            @"C:\Apps\PoproshaykaBot\PoproshaykaBot.Wpf.exe",
            "x64");

        KeepShell.Updates.IUpdateEnvironment keepEnv = env;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(env.Kind, Is.EqualTo(botKind));
            Assert.That(keepEnv.Kind, Is.EqualTo(expectedKeepKind));
            Assert.That(keepEnv.SelfContained, Is.EqualTo(expectedSelfContained));
            Assert.That(keepEnv.StagingDirectory, Is.EqualTo(@"C:\Apps\PoproshaykaBot\update"));
            Assert.That(UpdateEnvironment.GetStagingDirectory(@"C:\Apps\PoproshaykaBot\PoproshaykaBot.Wpf.exe"),
                Is.EqualTo(@"C:\Apps\PoproshaykaBot\update"));
        }
    }

    [Test]
    public void ResolveCurrentVersion_ParsesInformationalVersionTag()
    {
        var assembly = CreateDynamicAssembly(null, null, "v3.1.0.15+abc1234");

        var version = UpdateEnvironment.ResolveCurrentVersion(assembly);

        Assert.That(version, Is.EqualTo(Version.Parse("3.1.0.15")));
    }

    private static Assembly CreateDynamicAssembly(
        string? updatableBuild,
        string? repositorySlug,
        string? informationalVersion = null)
    {
        var attributes = new List<CustomAttributeBuilder>();
        var metadataCtor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;

        if (updatableBuild is not null)
        {
            attributes.Add(new(metadataCtor, ["UpdatableBuild", updatableBuild]));
        }

        if (repositorySlug is not null)
        {
            attributes.Add(new(metadataCtor, ["GitHubRepository", repositorySlug]));
        }

        if (informationalVersion is not null)
        {
            var infoCtor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            attributes.Add(new(infoCtor, [informationalVersion]));
        }

        return AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("PoproshaykaBot.TestDynamic." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run,
            attributes);
    }
}

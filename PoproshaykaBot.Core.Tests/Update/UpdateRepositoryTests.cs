using PoproshaykaBot.Core.Update;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateRepositoryTests
{
    [TestCase("MaxNagibator/PoproshaykaBot")]
    [TestCase("a/b")]
    [TestCase("user-name/repo.name_1")]
    [TestCase("Org123/My-Repo.2")]
    public void IsValidSlug_AcceptsWellFormed(string slug)
    {
        Assert.That(UpdateRepository.IsValidSlug(slug), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("noslash")]
    [TestCase("a/b/c")]
    [TestCase("a//b")]
    [TestCase("/repo")]
    [TestCase("owner/")]
    [TestCase("ow ner/repo")]
    [TestCase("../etc")]
    [TestCase("owner/..")]
    [TestCase("https://github.com/a/b")]
    [TestCase("a/b?x=1")]
    public void IsValidSlug_RejectsMalformedOrUnsafe(string? slug)
    {
        Assert.That(UpdateRepository.IsValidSlug(slug), Is.False);
    }

    [Test]
    public void Resolve_PrefersValidOverride_TrimmedOtherwiseFallsBack()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UpdateRepository.Resolve("Fork/Repo", "Default/Repo"), Is.EqualTo("Fork/Repo"));
            Assert.That(UpdateRepository.Resolve("  Fork/Repo  ", "Default/Repo"), Is.EqualTo("Fork/Repo"));
            Assert.That(UpdateRepository.Resolve(null, "Default/Repo"), Is.EqualTo("Default/Repo"));
            Assert.That(UpdateRepository.Resolve("   ", "Default/Repo"), Is.EqualTo("Default/Repo"));
            Assert.That(UpdateRepository.Resolve("garbage", "Default/Repo"), Is.EqualTo("Default/Repo"));
            Assert.That(UpdateRepository.Resolve("../escape", "Default/Repo"), Is.EqualTo("Default/Repo"));
        }
    }
}

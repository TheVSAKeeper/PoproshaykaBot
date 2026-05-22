using PoproshaykaBot.Core.Update;

namespace PoproshaykaBot.Core.Tests.Update;

[TestFixture]
public sealed class UpdateRuntimeTests
{
    [TestCase("net8.0-windows", 8)]
    [TestCase("net8.0", 8)]
    [TestCase("net10.0-windows", 10)]
    [TestCase(".NETCoreApp,Version=v8.0", 8)]
    [TestCase(".NETCoreApp,Version=v9.0", 9)]
    public void ParseMajor_ExtractsRuntimeMajor(string value, int expected)
    {
        Assert.That(UpdateRuntime.ParseMajor(value), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("garbage")]
    public void ParseMajor_ReturnsNull_ForInvalidInput(string? value)
    {
        Assert.That(UpdateRuntime.ParseMajor(value), Is.Null);
    }
}

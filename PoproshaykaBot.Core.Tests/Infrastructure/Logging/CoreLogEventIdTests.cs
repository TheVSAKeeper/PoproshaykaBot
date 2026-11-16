using System.Reflection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Logging;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Logging;

[TestFixture]
internal sealed class CoreLogEventIdTests
{
    [Test]
    public void LoggerMessage_Methods_Use_Unique_EventIds()
    {
        var methods = typeof(CoreLog).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var attributes = methods
            .Select(static method => (Method: method, Attr: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(static pair => pair.Attr is not null)
            .ToArray();

        Assert.That(attributes, Is.Not.Empty);
        Assert.That(attributes.Select(static pair => pair.Attr!.EventId), Is.All.GreaterThanOrEqualTo(2000));

        var duplicates = attributes
            .GroupBy(static pair => pair.Attr!.EventId)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        Assert.That(duplicates, Is.Empty);
    }
}

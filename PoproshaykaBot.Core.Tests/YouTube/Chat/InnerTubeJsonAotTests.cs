using System.Text.Json;
using PoproshaykaBot.Core.Tests.YouTube.Fixtures;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
internal sealed class InnerTubeJsonAotTests
{
    [Test]
    public void SourceGenerated_Context_Serializes_Request_And_Deserializes_Polymorphic_Runs()
    {
        var request = new LiveChatRequest
        {
            Context = new()
            {
                Client = new()
                {
                    ClientName = InnerTubeEndpoints.WebClientName,
                    ClientVersion = "2.20261116.01.00",
                    Hl = "en",
                    Gl = "US",
                },
            },
            Continuation = "SYNTHETIC_CONT_AOT",
        };

        var jsonRequest = JsonSerializer.Serialize(request, InnerTubeJsonSerializerContext.Default.LiveChatRequest);
        Assert.That(jsonRequest, Does.Contain("\"continuation\":\"SYNTHETIC_CONT_AOT\""));

        var fixtureJson = InnerTubeFixtureLoader.ReadText("initial_mixed_with_ticker.json");
        var response = JsonSerializer.Deserialize(fixtureJson, InnerTubeJsonSerializerContext.Default.LiveChatResponse);

        Assert.That(response?.ContinuationContents?.LiveChatContinuation?.Actions, Is.Not.Null);

        var runs = response!.ContinuationContents!.LiveChatContinuation!.Actions![1]
            .AddChatItemAction!.Item!.LiveChatTextMessageRenderer!.Message!.Runs!;

        Assert.Multiple(() =>
        {
            Assert.That(runs, Has.Count.EqualTo(2));
            Assert.That(runs[0], Is.TypeOf<MessageText>());
            Assert.That(runs[1], Is.TypeOf<MessageEmoji>());
        });
    }
}

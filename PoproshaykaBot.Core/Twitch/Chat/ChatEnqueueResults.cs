namespace PoproshaykaBot.Core.Twitch.Chat;

public static class ChatEnqueueResults
{
    public static string DescribeRefusal(ChatEnqueueResult result)
    {
        return result switch
        {
            ChatEnqueueResult.ObserverMode => "режим наблюдателя",
            ChatEnqueueResult.QueueFull => "очередь отправки переполнена",
            ChatEnqueueResult.QueueClosed => "очередь отправки закрыта",
            ChatEnqueueResult.EmptyMessage => "пустое сообщение",
            _ => "причина не названа",
        };
    }
}

using PoproshaykaBot.WinForms.Models;

namespace PoproshaykaBot.WinForms;

public partial class ChatDisplay : UserControl
{
    public ChatDisplay()
    {
        InitializeComponent();
    }

    public void AddChatMessage(ChatMessageData chatMessage)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<ChatMessageData>(AddChatMessage), chatMessage);
            return;
        }

        var shouldAutoScroll = _chatRichTextBox.SelectionStart >= _chatRichTextBox.Text.Length - 1;

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(chatMessage.Timestamp, TimeZoneInfo.Local);
        var timeString = localTime.ToString("HH:mm:ss");

        string messageText;
        Color messageColor;

        switch (chatMessage.MessageType)
        {
            case ChatMessageType.UserMessage:
                var timestampPart = $"[{timeString}] ";
                var usernamePart = $"{chatMessage.DisplayName}: ";
                var messagePart = chatMessage.Message;

                _chatRichTextBox.SelectionColor = Color.Gray;
                _chatRichTextBox.AppendText(timestampPart);

                _chatRichTextBox.SelectionColor = Color.DarkBlue;
                _chatRichTextBox.AppendText(usernamePart);

                _chatRichTextBox.SelectionColor = Color.Black;
                _chatRichTextBox.AppendText(messagePart + Environment.NewLine);

                _chatRichTextBox.Select(_chatRichTextBox.Text.Length, 0);
                _chatRichTextBox.SelectionColor = Color.Black;

                if (shouldAutoScroll)
                {
                    _chatRichTextBox.SelectionStart = _chatRichTextBox.Text.Length;
                    _chatRichTextBox.ScrollToCaret();
                }

                return;

            case ChatMessageType.BotResponse:
                messageText = $"[{timeString}] {chatMessage.DisplayName}: {chatMessage.Message}";
                messageColor = Color.DarkGreen;
                break;

            case ChatMessageType.SystemNotification:
                messageText = $"[{timeString}] *** {chatMessage.Message} ***";
                messageColor = Color.DarkOrange;
                break;

            default:
                messageText = $"[{timeString}] {chatMessage.DisplayName}: {chatMessage.Message}";
                messageColor = Color.Black;
                break;
        }

        _chatRichTextBox.SelectionColor = messageColor;
        _chatRichTextBox.AppendText(messageText + Environment.NewLine);

        _chatRichTextBox.Select(_chatRichTextBox.Text.Length, 0);
        _chatRichTextBox.SelectionColor = Color.Black;

        if (shouldAutoScroll)
        {
            _chatRichTextBox.SelectionStart = _chatRichTextBox.Text.Length;
            _chatRichTextBox.ScrollToCaret();
        }
    }

    private void OnLoad(object sender, EventArgs e)
    {
        var welcomeMessage = new ChatMessageData
        {
            Timestamp = DateTime.UtcNow,
            DisplayName = "Система",
            Message = "Чат готов к отображению сообщений. Подключите бота для начала работы.",
            MessageType = ChatMessageType.SystemNotification,
        };

        AddChatMessage(welcomeMessage);
    }
}

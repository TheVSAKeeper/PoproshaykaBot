using PoproshaykaBot.WinForms.Models;

namespace PoproshaykaBot.WinForms;

public partial class ChatWindow : Form, IChatDisplay
{
    private readonly ChatHistoryManager _chatHistoryManager;

    public ChatWindow(ChatHistoryManager chatHistoryManager)
    {
        _chatHistoryManager = chatHistoryManager;
        InitializeComponent();
        RestoreChatHistory();

        _chatHistoryManager.RegisterChatDisplay(this);
    }

    public void AddChatMessage(ChatMessageData chatMessage)
    {
        _chatDisplay.AddChatMessage(chatMessage);
    }

    public void ClearChat()
    {
        _chatDisplay.ClearChat();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _chatHistoryManager.UnregisterChatDisplay(this);
        base.OnFormClosing(e);
    }

    private void RestoreChatHistory()
    {
        var history = _chatHistoryManager.GetHistory();

        foreach (var message in history)
        {
            _chatDisplay.AddChatMessage(message);
        }
    }
}

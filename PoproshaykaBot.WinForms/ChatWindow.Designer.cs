namespace PoproshaykaBot.WinForms;

partial class ChatWindow
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(ChatWindow));
        _chatDisplay = new ChatDisplay();
        SuspendLayout();
        // 
        // _chatDisplay
        // 
        _chatDisplay.Dock = DockStyle.Fill;
        _chatDisplay.Location = new Point(0, 0);
        _chatDisplay.Name = "_chatDisplay";
        _chatDisplay.Size = new Size(498, 357);
        _chatDisplay.TabIndex = 0;
        // 
        // ChatWindow
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(498, 357);
        Controls.Add(_chatDisplay);
        Icon = (Icon)resources.GetObject("$this.Icon");
        MinimumSize = new Size(300, 200);
        Name = "ChatWindow";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Чат - Отдельное окно";
        ResumeLayout(false);
    }

    private ChatDisplay _chatDisplay;

    #endregion
}

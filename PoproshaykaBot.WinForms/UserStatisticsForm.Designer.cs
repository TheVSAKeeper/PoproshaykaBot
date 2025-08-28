

namespace PoproshaykaBot.WinForms;

partial class UserStatisticsForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;
    private TextBox textBoxFilter;
    private ListBox listBoxUsers;
    private Button buttonAction;
    private Label labelUserId;
    private Label labelUserName;
    private Label labelMessageCount;
    private Label labelChessPiece;
    private NumericUpDown numericIncrement;

    /// <summary>
    /// Clean up any resources being used.
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


    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(UserStatisticsForm));
        textBoxFilter = new TextBox();
        listBoxUsers = new ListBox();
        buttonAction = new Button();
        labelUserId = new Label();
        labelUserName = new Label();
        labelMessageCount = new Label();
        labelChessPiece = new Label();
        numericIncrement = new NumericUpDown();
        ((System.ComponentModel.ISupportInitialize)numericIncrement).BeginInit();
        SuspendLayout();
        // 
        // textBoxFilter
        // 
        textBoxFilter.Location = new Point(12, 12);
        textBoxFilter.Name = "textBoxFilter";
        textBoxFilter.Size = new Size(300, 23);
        textBoxFilter.TabIndex = 0;
        textBoxFilter.TextChanged += textBoxFilter_TextChanged;
        // 
        // listBoxUsers
        // 
        listBoxUsers.FormattingEnabled = true;
        listBoxUsers.ItemHeight = 15;
        listBoxUsers.Location = new Point(12, 38);
        listBoxUsers.Name = "listBoxUsers";
        listBoxUsers.Size = new Size(300, 289);
        listBoxUsers.TabIndex = 1;
        listBoxUsers.SelectedIndexChanged += listBoxUsers_SelectedIndexChanged;
        // 
        // buttonAction
        // 
        buttonAction.Enabled = false;
        buttonAction.Location = new Point(333, 155);
        buttonAction.Name = "buttonAction";
        buttonAction.Size = new Size(200, 30);
        buttonAction.TabIndex = 7;
        buttonAction.Text = "Добавить сообщения";
        buttonAction.UseVisualStyleBackColor = true;
        buttonAction.Click += buttonAction_Click;
        // 
        // labelUserId
        // 
        labelUserId.AutoSize = true;
        labelUserId.Location = new Point(330, 15);
        labelUserId.Name = "labelUserId";
        labelUserId.Size = new Size(51, 15);
        labelUserId.TabIndex = 3;
        labelUserId.Text = "🆔 ID: —";
        // 
        // labelUserName
        // 
        labelUserName.AutoSize = true;
        labelUserName.Location = new Point(330, 40);
        labelUserName.Name = "labelUserName";
        labelUserName.Size = new Size(64, 15);
        labelUserName.TabIndex = 4;
        labelUserName.Text = "👤 Имя: —";
        // 
        // labelMessageCount
        // 
        labelMessageCount.AutoSize = true;
        labelMessageCount.Location = new Point(330, 65);
        labelMessageCount.Name = "labelMessageCount";
        labelMessageCount.Size = new Size(101, 15);
        labelMessageCount.TabIndex = 5;
        labelMessageCount.Text = "💬 Сообщений: 0";
        // 
        // labelChessPiece
        // 
        labelChessPiece.AutoSize = true;
        labelChessPiece.Font = new Font("Microsoft Sans Serif", 14F, FontStyle.Bold, GraphicsUnit.Point, 204);
        labelChessPiece.Location = new Point(330, 90);
        labelChessPiece.Name = "labelChessPiece";
        labelChessPiece.Size = new Size(108, 24);
        labelChessPiece.TabIndex = 8;
        labelChessPiece.Text = "♟ ПЕШКА";
        // 
        // numericIncrement
        // 
        numericIncrement.Location = new Point(333, 125);
        numericIncrement.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
        numericIncrement.Minimum = new decimal(new int[] { 1000000, 0, 0, int.MinValue });
        numericIncrement.Name = "numericIncrement";
        numericIncrement.Size = new Size(150, 23);
        numericIncrement.TabIndex = 6;
        // 
        // UserStatisticsForm
        // 
        ClientSize = new Size(560, 350);
        Controls.Add(numericIncrement);
        Controls.Add(labelChessPiece);
        Controls.Add(labelMessageCount);
        Controls.Add(labelUserName);
        Controls.Add(labelUserId);
        Controls.Add(buttonAction);
        Controls.Add(listBoxUsers);
        Controls.Add(textBoxFilter);
        Icon = (Icon)resources.GetObject("$this.Icon");
        Name = "UserStatisticsForm";
        Text = "📊 Статистика пользователей 👥";
        ((System.ComponentModel.ISupportInitialize)numericIncrement).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}

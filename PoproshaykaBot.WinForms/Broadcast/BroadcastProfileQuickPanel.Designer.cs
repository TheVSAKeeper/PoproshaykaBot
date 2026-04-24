namespace PoproshaykaBot.WinForms.Broadcast;

partial class BroadcastProfileQuickPanel
{
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Label _profileLabel;
    private System.Windows.Forms.ComboBox _profilesComboBox;
    private System.Windows.Forms.Button _applyButton;
    private System.Windows.Forms.Label _statusLabel;

    private void InitializeComponent()
    {
        _profileLabel = new System.Windows.Forms.Label { Text = "Профиль:", AutoSize = true, Location = new System.Drawing.Point(8, 10) };
        _profilesComboBox = new System.Windows.Forms.ComboBox
        {
            Location = new System.Drawing.Point(70, 6),
            Width = 200,
            DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
            Name = "_profilesComboBox",
        };
        _applyButton = new System.Windows.Forms.Button
        {
            Text = "Применить",
            Location = new System.Drawing.Point(280, 5),
            Width = 100,
            Name = "_applyButton",
        };
        _statusLabel = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(390, 10),
            Name = "_statusLabel",
        };

        Controls.Add(_profileLabel);
        Controls.Add(_profilesComboBox);
        Controls.Add(_applyButton);
        Controls.Add(_statusLabel);

        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Name = "BroadcastProfileQuickPanel";
        Size = new System.Drawing.Size(700, 32);
    }
}

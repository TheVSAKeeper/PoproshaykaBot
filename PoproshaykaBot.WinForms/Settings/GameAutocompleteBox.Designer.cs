namespace PoproshaykaBot.WinForms.Settings
{
    partial class GameAutocompleteBox
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox _queryTextBox;
        private System.Windows.Forms.ListBox _suggestionsListBox;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            _queryTextBox = new System.Windows.Forms.TextBox();
            _suggestionsListBox = new System.Windows.Forms.ListBox();
            SuspendLayout();
            //
            // _queryTextBox
            //
            _queryTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            _queryTextBox.Name = "_queryTextBox";
            _queryTextBox.TabIndex = 0;
            //
            // _suggestionsListBox
            //
            _suggestionsListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            _suggestionsListBox.Name = "_suggestionsListBox";
            _suggestionsListBox.TabIndex = 1;
            _suggestionsListBox.Visible = false;
            _suggestionsListBox.IntegralHeight = false;
            //
            // GameAutocompleteBox
            //
            Controls.Add(_suggestionsListBox);
            Controls.Add(_queryTextBox);
            Name = "GameAutocompleteBox";
            Size = new System.Drawing.Size(300, 150);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}

namespace PoproshaykaBot.WinForms.Settings
{
    partial class GameAutocompleteBox
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox _queryTextBox;
        private System.Windows.Forms.ListBox _suggestionsListBox;
        private System.Windows.Forms.Timer _debounceTimer;

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
            components = new System.ComponentModel.Container();
            _queryTextBox = new System.Windows.Forms.TextBox();
            _suggestionsListBox = new System.Windows.Forms.ListBox();
            _debounceTimer = new System.Windows.Forms.Timer(components);
            SuspendLayout();
            //
            // _queryTextBox
            //
            _queryTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            _queryTextBox.Name = "_queryTextBox";
            _queryTextBox.TabIndex = 0;
            _queryTextBox.TextChanged += OnTextChanged;
            //
            // _suggestionsListBox
            //
            _suggestionsListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            _suggestionsListBox.Name = "_suggestionsListBox";
            _suggestionsListBox.TabIndex = 1;
            _suggestionsListBox.Visible = false;
            _suggestionsListBox.IntegralHeight = false;
            _suggestionsListBox.Click += OnSuggestionClicked;
            //
            // _debounceTimer
            //
            _debounceTimer.Interval = 250;
            _debounceTimer.Tick += OnDebounceTick;
            //
            // GameAutocompleteBox
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
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

using System.Drawing;
using System.Windows.Forms;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Small helpers to build the dialogs of this feature in code (no designer files).
    /// </summary>
    internal static class FormBuilder
    {
        public static void SetupDialog(Form form, string title, int width, int height)
        {
            form.Text = title;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleMode = AutoScaleMode.Font;
            form.Font = new Font("Segoe UI", 9F);
            form.ClientSize = new Size(width, height);
            form.Padding = new Padding(10);
        }

        public static void SetupResizable(Form form, string title, int width, int height)
        {
            form.Text = title;
            form.FormBorderStyle = FormBorderStyle.Sizable;
            form.ShowInTaskbar = true;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleMode = AutoScaleMode.Font;
            form.Font = new Font("Segoe UI", 9F);
            form.ClientSize = new Size(width, height);
            form.MinimumSize = new Size(500, 300);
            form.Padding = new Padding(8);
        }

        /// <summary>A two-column table (label | control) that fills its parent.</summary>
        public static TableLayoutPanel FieldTable()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return table;
        }

        public static void AddRow(TableLayoutPanel table, string label, Control control, SizeType sizeType = SizeType.AutoSize, float height = 0)
        {
            var row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(sizeType == SizeType.AutoSize ? new RowStyle(SizeType.AutoSize) : new RowStyle(sizeType, height));

            if (label != null)
            {
                table.Controls.Add(new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top,
                    Margin = new Padding(3, 7, 3, 3)
                }, 0, row);
                control.Dock = DockStyle.Fill;
                table.Controls.Add(control, 1, row);
            }
            else
            {
                control.Dock = DockStyle.Fill;
                table.Controls.Add(control, 0, row);
                table.SetColumnSpan(control, 2);
            }
        }

        public static TextBox PasswordBox()
        {
            return new TextBox { UseSystemPasswordChar = true };
        }

        /// <summary>A bottom bar with right-aligned buttons.</summary>
        public static FlowLayoutPanel ButtonBar(params Button[] buttons)
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0)
            };
            foreach (var button in buttons)
                bar.Controls.Add(button);
            return bar;
        }

        public static Button Button(string text, DialogResult result = DialogResult.None)
        {
            return new Button
            {
                Text = text,
                DialogResult = result,
                AutoSize = true,
                MinimumSize = new Size(90, 27)
            };
        }

        public static ListView DetailsList()
        {
            return new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill
            };
        }

        public static void ShowError(IWin32Window owner, string message)
        {
            MessageBox.Show(owner, message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static bool Confirm(IWin32Window owner, string message)
        {
            return MessageBox.Show(owner, message, Application.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public const int MinPasswordLength = 8;

        /// <summary>Returns an error message, or null if the password is acceptable.</summary>
        public static string ValidateNewPassword(string password, string confirmation)
        {
            if ((password ?? "").Length < MinPasswordLength)
                return string.Format(MultiUserText.PasswordTooShort, MinPasswordLength);
            if (password != confirmation)
                return MultiUserText.PasswordsDoNotMatch;
            return null;
        }
    }
}

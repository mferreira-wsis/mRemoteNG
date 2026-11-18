using System;
using System.Drawing;
using System.Windows.Forms;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Lets the logged in user change their password, or lets an administrator
    /// set a new password for another user (in that case the current password is not asked).
    /// </summary>
    public class ChangePasswordForm : Form
    {
        private readonly bool _askCurrentPassword;
        private readonly TextBox _txtCurrent = FormBuilder.PasswordBox();
        private readonly TextBox _txtNew = FormBuilder.PasswordBox();
        private readonly TextBox _txtConfirm = FormBuilder.PasswordBox();
        private readonly Label _lblError = new Label { AutoSize = true, ForeColor = Color.Firebrick, Visible = false };

        public string NewPassword => _txtNew.Text;

        public ChangePasswordForm(string header, bool askCurrentPassword)
        {
            _askCurrentPassword = askCurrentPassword;
            FormBuilder.SetupDialog(this, MultiUserText.ChangePasswordTitle, 420, askCurrentPassword ? 240 : 210);

            var table = FormBuilder.FieldTable();
            if (!string.IsNullOrEmpty(header))
            {
                FormBuilder.AddRow(table, null, new Label
                {
                    Text = header,
                    AutoSize = true,
                    MaximumSize = new Size(370, 0),
                    Margin = new Padding(3, 3, 3, 10)
                });
            }
            if (askCurrentPassword)
                FormBuilder.AddRow(table, MultiUserText.CurrentPassword, _txtCurrent);
            FormBuilder.AddRow(table, MultiUserText.NewPassword, _txtNew);
            FormBuilder.AddRow(table, MultiUserText.ConfirmPassword, _txtConfirm);
            FormBuilder.AddRow(table, null, _lblError);

            var btnOk = FormBuilder.Button(MultiUserText.Ok);
            var btnCancel = FormBuilder.Button(MultiUserText.Cancel, DialogResult.Cancel);
            btnOk.Click += OnOkClick;

            Controls.Add(table);
            Controls.Add(FormBuilder.ButtonBar(btnCancel, btnOk));
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            var error = FormBuilder.ValidateNewPassword(_txtNew.Text, _txtConfirm.Text);
            if (error == null && _askCurrentPassword)
            {
                var current = MultiUserService.CurrentUser;
                var result = MultiUserService.Repository.Authenticate(current.Username, _txtCurrent.Text, out _);
                if (result != MultiUserRepository.AuthenticationResult.Success)
                    error = MultiUserText.WrongCurrentPassword;
            }

            if (error != null)
            {
                _lblError.Text = error;
                _lblError.Visible = true;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

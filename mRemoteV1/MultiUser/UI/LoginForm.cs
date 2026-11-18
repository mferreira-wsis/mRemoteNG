using System;
using System.Drawing;
using System.Windows.Forms;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Login dialog. When the database has no users yet it is shown in
    /// "create first administrator" mode.
    /// </summary>
    public class LoginForm : Form
    {
        private const int MaxAttempts = 5;

        private readonly bool _createFirstAdmin;
        private readonly TextBox _txtUsername = new TextBox();
        private readonly TextBox _txtDisplayName = new TextBox();
        private readonly TextBox _txtPassword = FormBuilder.PasswordBox();
        private readonly TextBox _txtConfirm = FormBuilder.PasswordBox();
        private readonly Label _lblError = new Label { AutoSize = true, ForeColor = Color.Firebrick, Visible = false };
        private int _failedAttempts;

        public AppUser AuthenticatedUser { get; private set; }

        public LoginForm(bool createFirstAdmin)
        {
            _createFirstAdmin = createFirstAdmin;
            FormBuilder.SetupDialog(this, MultiUserText.LoginTitle, 420, createFirstAdmin ? 280 : 200);
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            var table = FormBuilder.FieldTable();
            FormBuilder.AddRow(table, null, new Label
            {
                Text = createFirstAdmin ? MultiUserText.FirstAdminHeader : MultiUserText.LoginHeader,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(3, 3, 3, 10)
            });
            FormBuilder.AddRow(table, MultiUserText.Username, _txtUsername);
            if (createFirstAdmin)
                FormBuilder.AddRow(table, MultiUserText.DisplayName, _txtDisplayName);
            FormBuilder.AddRow(table, MultiUserText.Password, _txtPassword);
            if (createFirstAdmin)
                FormBuilder.AddRow(table, MultiUserText.ConfirmPassword, _txtConfirm);
            FormBuilder.AddRow(table, null, _lblError);

            var btnOk = FormBuilder.Button(createFirstAdmin ? MultiUserText.CreateButton : MultiUserText.LoginButton);
            var btnCancel = FormBuilder.Button(MultiUserText.Cancel, DialogResult.Cancel);
            btnOk.Click += OnOkClick;

            Controls.Add(table);
            Controls.Add(FormBuilder.ButtonBar(btnCancel, btnOk));
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            _txtUsername.Text = createFirstAdmin ? "admin" : Settings.Default.MultiUserLastUsername;
            Shown += (s, e) =>
            {
                if (_txtUsername.Text.Length > 0 && !createFirstAdmin)
                    _txtPassword.Focus();
                else
                    _txtUsername.Focus();
            };
        }

        private void ShowError(string message)
        {
            _lblError.Text = message;
            _lblError.Visible = true;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            var username = _txtUsername.Text.Trim();
            if (username.Length == 0)
            {
                ShowError(MultiUserText.UsernameRequired);
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                if (_createFirstAdmin)
                    CreateFirstAdmin(username);
                else
                    Login(username);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void CreateFirstAdmin(string username)
        {
            var error = FormBuilder.ValidateNewPassword(_txtPassword.Text, _txtConfirm.Text);
            if (error != null)
            {
                ShowError(error);
                return;
            }

            var user = new AppUser
            {
                Username = username,
                DisplayName = _txtDisplayName.Text.Trim(),
                IsAdmin = true,
                IsEnabled = true
            };
            MultiUserService.Repository.CreateUser(user, _txtPassword.Text);
            Login(username);
        }

        private void Login(string username)
        {
            var result = MultiUserService.Repository.Authenticate(username, _txtPassword.Text, out var user);
            switch (result)
            {
                case MultiUserRepository.AuthenticationResult.Success:
                    AuthenticatedUser = user;
                    Settings.Default.MultiUserLastUsername = user.Username;
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                case MultiUserRepository.AuthenticationResult.Disabled:
                    MultiUserService.LogFailedLogin(username, "Conta desativada");
                    ShowError(MultiUserText.AccountDisabled);
                    break;
                default:
                    MultiUserService.LogFailedLogin(username, "Credenciais inválidas");
                    ShowError(MultiUserText.InvalidCredentials);
                    break;
            }

            _txtPassword.Clear();
            _txtPassword.Focus();
            _failedAttempts++;
            if (_failedAttempts < MaxAttempts) return;

            MessageBox.Show(this, MultiUserText.TooManyAttempts, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Stop);
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}

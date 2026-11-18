using System;
using System.Windows.Forms;
using mRemoteNG.App;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// "Users" entry of the main menu. Only added when a user is logged in.
    /// </summary>
    public class MultiUserMenu : ToolStripMenuItem
    {
        private readonly ToolStripMenuItem _mnuLoggedInAs = new ToolStripMenuItem { Enabled = false };
        private readonly ToolStripMenuItem _mnuActiveSessions = new ToolStripMenuItem(MultiUserText.MenuActiveSessions);
        private readonly ToolStripMenuItem _mnuAccessLog = new ToolStripMenuItem(MultiUserText.MenuAccessLog);
        private readonly ToolStripMenuItem _mnuManage = new ToolStripMenuItem(MultiUserText.MenuManageUsers);
        private readonly ToolStripMenuItem _mnuChangePassword = new ToolStripMenuItem(MultiUserText.MenuChangePassword);
        private readonly ToolStripMenuItem _mnuLogout = new ToolStripMenuItem(MultiUserText.MenuLogoutAndExit);

        private ActiveSessionsForm _sessionsForm;
        private AccessLogForm _logForm;

        public Form MainForm { get; set; }

        public MultiUserMenu()
        {
            Name = "mMenMultiUser";
            Text = MultiUserText.MenuTitle;

            DropDownItems.AddRange(new ToolStripItem[]
            {
                _mnuLoggedInAs,
                new ToolStripSeparator(),
                _mnuActiveSessions,
                _mnuAccessLog,
                _mnuManage,
                new ToolStripSeparator(),
                _mnuChangePassword,
                _mnuLogout
            });

            _mnuActiveSessions.Click += (s, e) => ShowActiveSessions();
            _mnuAccessLog.Click += (s, e) => ShowAccessLog();
            _mnuManage.Click += (s, e) => ShowUserManagement();
            _mnuChangePassword.Click += (s, e) => ChangePassword();
            _mnuLogout.Click += (s, e) => Shutdown.Quit();

            DropDownOpening += (s, e) => UpdateState();
            UpdateState();
        }

        private void UpdateState()
        {
            var user = MultiUserService.CurrentUser;
            _mnuLoggedInAs.Text = string.Format(MultiUserText.LoggedInAs, user?.ToString() ?? "-");
            _mnuAccessLog.Visible = MultiUserService.IsAdmin;
            _mnuManage.Visible = MultiUserService.IsAdmin;
        }

        private void ShowActiveSessions()
        {
            if (_sessionsForm == null || _sessionsForm.IsDisposed)
            {
                _sessionsForm = new ActiveSessionsForm();
                _sessionsForm.Show(MainForm);
            }
            else
            {
                _sessionsForm.Activate();
            }
        }

        private void ShowAccessLog()
        {
            if (!MultiUserService.IsAdmin) return;
            if (_logForm == null || _logForm.IsDisposed)
            {
                _logForm = new AccessLogForm();
                _logForm.Show(MainForm);
            }
            else
            {
                _logForm.Activate();
            }
        }

        private void ShowUserManagement()
        {
            if (!MultiUserService.IsAdmin) return;
            using (var form = new UserManagementForm())
                form.ShowDialog(MainForm);
        }

        private void ChangePassword()
        {
            using (var form = new ChangePasswordForm("", true))
            {
                if (form.ShowDialog(MainForm) != DialogResult.OK) return;
                try
                {
                    MultiUserService.ChangeOwnPassword(form.NewPassword);
                    MessageBox.Show(MainForm, MultiUserText.PasswordChanged, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    FormBuilder.ShowError(MainForm, string.Format(MultiUserText.SaveFailed, ex.Message));
                }
            }
        }
    }
}

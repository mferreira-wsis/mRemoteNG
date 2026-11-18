using System;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.MultiUser.UI;

namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// Runs the login sequence at application start-up, before the connections are loaded.
    /// </summary>
    public static class MultiUserStartup
    {
        /// <summary>
        /// Returns false if the user cancelled the login (the application must exit).
        /// Returns true when the feature is disabled or the login succeeded.
        /// </summary>
        public static bool Login(IWin32Window owner)
        {
            if (!MultiUserService.IsConfigured)
                return true;

            int userCount;
            while (true)
            {
                try
                {
                    MultiUserService.Repository.EnsureSchema();
                    userCount = MultiUserService.Repository.CountUsers();
                    break;
                }
                catch (Exception ex)
                {
                    Runtime.MessageCollector.AddExceptionMessage("Multi-user: database not available", ex, MessageClass.ErrorMsg);
                    var answer = MessageBox.Show(owner, string.Format(MultiUserText.DatabaseError, ex.Message),
                        Application.ProductName, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                    if (answer != DialogResult.Retry)
                        return false;
                }
            }

            AppUser user;
            using (var loginForm = new LoginForm(userCount == 0))
            {
                if (loginForm.ShowDialog(owner) != DialogResult.OK || loginForm.AuthenticatedUser == null)
                    return false;
                user = loginForm.AuthenticatedUser;
            }

            if (user.MustChangePassword)
            {
                using (var changeForm = new ChangePasswordForm(MultiUserText.MustChangePassword, false))
                {
                    if (changeForm.ShowDialog(owner) != DialogResult.OK)
                        return false;
                    MultiUserService.Repository.SetPassword(user.UserId, changeForm.NewPassword, false);
                    user.MustChangePassword = false;
                }
            }

            MultiUserService.SetLoggedInUser(user);
            if (userCount == 0)
                MultiUserService.Log(AccessAction.UserCreated, null, $"Administrador inicial: {user.Username}");
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"Multi-user: logged in as {user.Username}", true);
            return true;
        }
    }
}

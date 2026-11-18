using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Administration of users and groups (administrators only).
    /// </summary>
    public class UserManagementForm : Form
    {
        private readonly ListView _lvUsers = FormBuilder.DetailsList();
        private readonly ListView _lvGroups = FormBuilder.DetailsList();
        private List<AppUser> _users = new List<AppUser>();
        private List<AppGroup> _groups = new List<AppGroup>();

        public UserManagementForm()
        {
            FormBuilder.SetupResizable(this, MultiUserText.ManageTitle, 760, 460);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildUsersTab());
            tabs.TabPages.Add(BuildGroupsTab());

            var btnClose = FormBuilder.Button(MultiUserText.Close, DialogResult.Cancel);
            Controls.Add(tabs);
            Controls.Add(FormBuilder.ButtonBar(btnClose));
            CancelButton = btnClose;

            Load += (s, e) => ReloadData();
        }

        #region Layout
        private TabPage BuildUsersTab()
        {
            _lvUsers.Columns.Add(MultiUserText.ColUsername, 130);
            _lvUsers.Columns.Add(MultiUserText.ColDisplayName, 160);
            _lvUsers.Columns.Add(MultiUserText.ColAdmin, 55);
            _lvUsers.Columns.Add(MultiUserText.ColEnabled, 55);
            _lvUsers.Columns.Add(MultiUserText.ColGroups, 180);
            _lvUsers.Columns.Add(MultiUserText.ColLastLogin, 120);
            _lvUsers.DoubleClick += (s, e) => EditUser();

            var btnNew = FormBuilder.Button(MultiUserText.New);
            var btnEdit = FormBuilder.Button(MultiUserText.Edit);
            var btnReset = FormBuilder.Button(MultiUserText.ResetPassword);
            var btnDelete = FormBuilder.Button(MultiUserText.Delete);
            btnNew.Click += (s, e) => NewUser();
            btnEdit.Click += (s, e) => EditUser();
            btnReset.Click += (s, e) => ResetPassword();
            btnDelete.Click += (s, e) => DeleteUser();

            return BuildTab(MultiUserText.TabUsers, _lvUsers, btnNew, btnEdit, btnReset, btnDelete);
        }

        private TabPage BuildGroupsTab()
        {
            _lvGroups.Columns.Add(MultiUserText.ColGroupName, 160);
            _lvGroups.Columns.Add(MultiUserText.ColDescription, 260);
            _lvGroups.Columns.Add(MultiUserText.ColMembers, 260);
            _lvGroups.DoubleClick += (s, e) => EditGroup();

            var btnNew = FormBuilder.Button(MultiUserText.New);
            var btnEdit = FormBuilder.Button(MultiUserText.Edit);
            var btnDelete = FormBuilder.Button(MultiUserText.Delete);
            btnNew.Click += (s, e) => NewGroup();
            btnEdit.Click += (s, e) => EditGroup();
            btnDelete.Click += (s, e) => DeleteGroup();

            return BuildTab(MultiUserText.TabGroups, _lvGroups, btnNew, btnEdit, btnDelete);
        }

        private static TabPage BuildTab(string title, Control list, params Button[] buttons)
        {
            var page = new TabPage(title) { Padding = new Padding(6) };
            var side = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Padding = new Padding(6, 0, 0, 0)
            };
            foreach (var button in buttons)
            {
                button.MinimumSize = new Size(150, 27);
                side.Controls.Add(button);
            }
            page.Controls.Add(list);
            page.Controls.Add(side);
            return page;
        }
        #endregion

        #region Data
        private void ReloadData()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                _users = MultiUserService.Repository.GetUsers();
                _groups = MultiUserService.Repository.GetGroups();
            }
            catch (Exception ex)
            {
                FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var groupNames = _groups.ToDictionary(g => g.GroupId, g => g.Name);
            var userNames = _users.ToDictionary(u => u.UserId, u => u.Username);

            _lvUsers.BeginUpdate();
            _lvUsers.Items.Clear();
            foreach (var user in _users)
            {
                var item = new ListViewItem(user.Username) { Tag = user };
                item.SubItems.Add(user.DisplayName);
                item.SubItems.Add(user.IsAdmin ? MultiUserText.Yes : MultiUserText.No);
                item.SubItems.Add(user.IsEnabled ? MultiUserText.Yes : MultiUserText.No);
                item.SubItems.Add(string.Join(", ", user.GroupIds.Where(groupNames.ContainsKey).Select(id => groupNames[id]).OrderBy(n => n)));
                item.SubItems.Add(user.LastLoginAtUtc.HasValue
                    ? DateTime.SpecifyKind(user.LastLoginAtUtc.Value, DateTimeKind.Utc).ToLocalTime().ToString("g")
                    : "");
                if (!user.IsEnabled) item.ForeColor = SystemColors.GrayText;
                _lvUsers.Items.Add(item);
            }
            _lvUsers.EndUpdate();

            _lvGroups.BeginUpdate();
            _lvGroups.Items.Clear();
            foreach (var group in _groups)
            {
                var item = new ListViewItem(group.Name) { Tag = group };
                item.SubItems.Add(group.Description);
                item.SubItems.Add(string.Join(", ", group.MemberUserIds.Where(userNames.ContainsKey).Select(id => userNames[id]).OrderBy(n => n)));
                _lvGroups.Items.Add(item);
            }
            _lvGroups.EndUpdate();
        }

        private AppUser SelectedUser => _lvUsers.SelectedItems.Count > 0 ? (AppUser)_lvUsers.SelectedItems[0].Tag : null;
        private AppGroup SelectedGroup => _lvGroups.SelectedItems.Count > 0 ? (AppGroup)_lvGroups.SelectedItems[0].Tag : null;

        private bool WouldLeaveNoAdmin(AppUser changedUser)
        {
            return !_users
                .Select(u => u.UserId == changedUser.UserId ? changedUser : u)
                .Any(u => u.IsAdmin && u.IsEnabled);
        }
        #endregion

        #region Users
        private void NewUser()
        {
            using (var form = new UserEditForm(new AppUser(), _groups, true))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    MultiUserService.Repository.CreateUser(form.User, form.Password);
                    MultiUserService.Log(AccessAction.UserCreated, null, Describe(form.User));
                }
                catch (Exception ex)
                {
                    FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                }
            }
            ReloadData();
        }

        private void EditUser()
        {
            var selected = SelectedUser;
            if (selected == null) return;

            var copy = new AppUser
            {
                UserId = selected.UserId,
                Username = selected.Username,
                DisplayName = selected.DisplayName,
                IsAdmin = selected.IsAdmin,
                IsEnabled = selected.IsEnabled,
                MustChangePassword = selected.MustChangePassword,
                GroupIds = selected.GroupIds.ToList()
            };

            using (var form = new UserEditForm(copy, _groups, false))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var edited = form.User;

                if (edited.UserId == MultiUserService.CurrentUser.UserId && (!edited.IsEnabled || !edited.IsAdmin))
                {
                    FormBuilder.ShowError(this, MultiUserText.CannotDeleteYourself);
                    return;
                }
                if (WouldLeaveNoAdmin(edited))
                {
                    FormBuilder.ShowError(this, MultiUserText.CannotRemoveLastAdmin);
                    return;
                }

                try
                {
                    MultiUserService.Repository.UpdateUser(edited);
                    MultiUserService.Log(AccessAction.UserUpdated, null, Describe(edited));
                }
                catch (Exception ex)
                {
                    FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                }
            }
            MultiUserService.ReloadPermissions(true);
            ReloadData();
        }

        private void ResetPassword()
        {
            var selected = SelectedUser;
            if (selected == null) return;

            using (var form = new ChangePasswordForm(string.Format(MultiUserText.NewPasswordFor, selected.Username), false))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var mustChange = selected.UserId != MultiUserService.CurrentUser.UserId;
                    MultiUserService.Repository.SetPassword(selected.UserId, form.NewPassword, mustChange);
                    MultiUserService.Log(AccessAction.PasswordChanged, null, $"Reposta para {selected.Username}");
                }
                catch (Exception ex)
                {
                    FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                }
            }
            ReloadData();
        }

        private void DeleteUser()
        {
            var selected = SelectedUser;
            if (selected == null) return;

            if (selected.UserId == MultiUserService.CurrentUser.UserId)
            {
                FormBuilder.ShowError(this, MultiUserText.CannotDeleteYourself);
                return;
            }
            if (!_users.Any(u => u.UserId != selected.UserId && u.IsAdmin && u.IsEnabled))
            {
                FormBuilder.ShowError(this, MultiUserText.CannotRemoveLastAdmin);
                return;
            }
            if (!FormBuilder.Confirm(this, string.Format(MultiUserText.ConfirmDeleteUser, selected.Username)))
                return;

            try
            {
                MultiUserService.Repository.DeleteUser(selected.UserId);
                MultiUserService.Log(AccessAction.UserDeleted, null, selected.Username);
            }
            catch (Exception ex)
            {
                FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
            }
            ReloadData();
        }

        private string Describe(AppUser user)
        {
            var groups = _groups.Where(g => user.GroupIds.Contains(g.GroupId)).Select(g => g.Name);
            return $"{user.Username}; admin={user.IsAdmin}; ativo={user.IsEnabled}; grupos={string.Join(",", groups)}";
        }
        #endregion

        #region Groups
        private void NewGroup()
        {
            EditGroup(new AppGroup(), AccessAction.GroupCreated);
        }

        private void EditGroup()
        {
            var selected = SelectedGroup;
            if (selected == null) return;
            EditGroup(new AppGroup
            {
                GroupId = selected.GroupId,
                Name = selected.Name,
                Description = selected.Description,
                MemberUserIds = selected.MemberUserIds.ToList()
            }, AccessAction.GroupUpdated);
        }

        private void EditGroup(AppGroup group, string logAction)
        {
            using (var form = new GroupEditForm(group, _users))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    MultiUserService.Repository.SaveGroup(form.Group);
                    var members = _users.Where(u => form.Group.MemberUserIds.Contains(u.UserId)).Select(u => u.Username);
                    MultiUserService.Log(logAction, null, $"{form.Group.Name}; membros={string.Join(",", members)}");
                }
                catch (Exception ex)
                {
                    FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                }
            }
            MultiUserService.ReloadPermissions(true);
            ReloadData();
        }

        private void DeleteGroup()
        {
            var selected = SelectedGroup;
            if (selected == null) return;
            if (!FormBuilder.Confirm(this, string.Format(MultiUserText.ConfirmDeleteGroup, selected.Name)))
                return;

            try
            {
                MultiUserService.Repository.DeleteGroup(selected.GroupId);
                MultiUserService.Log(AccessAction.GroupDeleted, null, selected.Name);
            }
            catch (Exception ex)
            {
                FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
            }
            MultiUserService.ReloadPermissions(true);
            ReloadData();
        }
        #endregion
    }

    /// <summary>Create / edit one user.</summary>
    public class UserEditForm : Form
    {
        private readonly bool _isNew;
        private readonly TextBox _txtUsername = new TextBox();
        private readonly TextBox _txtDisplayName = new TextBox();
        private readonly TextBox _txtPassword = FormBuilder.PasswordBox();
        private readonly TextBox _txtConfirm = FormBuilder.PasswordBox();
        private readonly CheckBox _chkAdmin = new CheckBox { Text = MultiUserText.IsAdmin, AutoSize = true };
        private readonly CheckBox _chkEnabled = new CheckBox { Text = MultiUserText.IsEnabled, AutoSize = true };
        private readonly CheckBox _chkMustChange = new CheckBox { Text = MultiUserText.MustChangePasswordOption, AutoSize = true };
        private readonly CheckedListBox _lstGroups = new CheckedListBox { CheckOnClick = true, IntegralHeight = false };

        public AppUser User { get; }
        public string Password => _txtPassword.Text;

        public UserEditForm(AppUser user, IList<AppGroup> groups, bool isNew)
        {
            User = user;
            _isNew = isNew;
            FormBuilder.SetupDialog(this, MultiUserText.UserEditTitle, 460, isNew ? 470 : 410);

            var table = FormBuilder.FieldTable();
            FormBuilder.AddRow(table, MultiUserText.Username, _txtUsername);
            FormBuilder.AddRow(table, MultiUserText.DisplayName, _txtDisplayName);
            if (isNew)
            {
                FormBuilder.AddRow(table, MultiUserText.Password, _txtPassword);
                FormBuilder.AddRow(table, MultiUserText.ConfirmPassword, _txtConfirm);
            }
            FormBuilder.AddRow(table, null, _chkAdmin);
            FormBuilder.AddRow(table, null, _chkEnabled);
            FormBuilder.AddRow(table, null, _chkMustChange);
            FormBuilder.AddRow(table, MultiUserText.GroupsOfUser, _lstGroups, SizeType.Percent, 100F);

            var btnOk = FormBuilder.Button(MultiUserText.Ok);
            var btnCancel = FormBuilder.Button(MultiUserText.Cancel, DialogResult.Cancel);
            btnOk.Click += OnOkClick;
            Controls.Add(table);
            Controls.Add(FormBuilder.ButtonBar(btnCancel, btnOk));
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            _txtUsername.Text = user.Username;
            _txtDisplayName.Text = user.DisplayName;
            _chkAdmin.Checked = user.IsAdmin;
            _chkEnabled.Checked = isNew || user.IsEnabled;
            _chkMustChange.Checked = isNew || user.MustChangePassword;
            foreach (var group in groups)
                _lstGroups.Items.Add(group, user.GroupIds.Contains(group.GroupId));
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            if (_txtUsername.Text.Trim().Length == 0)
            {
                FormBuilder.ShowError(this, MultiUserText.UsernameRequired);
                return;
            }
            if (_isNew)
            {
                var error = FormBuilder.ValidateNewPassword(_txtPassword.Text, _txtConfirm.Text);
                if (error != null)
                {
                    FormBuilder.ShowError(this, error);
                    return;
                }
            }

            User.Username = _txtUsername.Text.Trim();
            User.DisplayName = _txtDisplayName.Text.Trim();
            User.IsAdmin = _chkAdmin.Checked;
            User.IsEnabled = _chkEnabled.Checked;
            User.MustChangePassword = _chkMustChange.Checked;
            User.GroupIds = _lstGroups.CheckedItems.Cast<AppGroup>().Select(g => g.GroupId).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Create / edit one group and its members.</summary>
    public class GroupEditForm : Form
    {
        private readonly TextBox _txtName = new TextBox();
        private readonly TextBox _txtDescription = new TextBox();
        private readonly CheckedListBox _lstMembers = new CheckedListBox { CheckOnClick = true, IntegralHeight = false };

        public AppGroup Group { get; }

        public GroupEditForm(AppGroup group, IList<AppUser> users)
        {
            Group = group;
            FormBuilder.SetupDialog(this, MultiUserText.GroupEditTitle, 440, 400);

            var table = FormBuilder.FieldTable();
            FormBuilder.AddRow(table, MultiUserText.GroupName, _txtName);
            FormBuilder.AddRow(table, MultiUserText.GroupDescription, _txtDescription);
            FormBuilder.AddRow(table, MultiUserText.GroupMembers, _lstMembers, SizeType.Percent, 100F);

            var btnOk = FormBuilder.Button(MultiUserText.Ok);
            var btnCancel = FormBuilder.Button(MultiUserText.Cancel, DialogResult.Cancel);
            btnOk.Click += OnOkClick;
            Controls.Add(table);
            Controls.Add(FormBuilder.ButtonBar(btnCancel, btnOk));
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            _txtName.Text = group.Name;
            _txtDescription.Text = group.Description;
            foreach (var user in users)
                _lstMembers.Items.Add(user, group.MemberUserIds.Contains(user.UserId));
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            if (_txtName.Text.Trim().Length == 0)
            {
                FormBuilder.ShowError(this, MultiUserText.GroupNameRequired);
                return;
            }

            Group.Name = _txtName.Text.Trim();
            Group.Description = _txtDescription.Text.Trim();
            Group.MemberUserIds = _lstMembers.CheckedItems.Cast<AppUser>().Select(u => u.UserId).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

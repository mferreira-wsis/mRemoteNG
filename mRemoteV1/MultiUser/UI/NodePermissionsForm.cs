using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using mRemoteNG.Connection;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Selects which groups may see / connect to a folder or connection (administrators only).
    /// </summary>
    public class NodePermissionsForm : Form
    {
        private readonly ConnectionInfo _node;
        private readonly CheckedListBox _lstGroups = new CheckedListBox { CheckOnClick = true, IntegralHeight = false };
        private List<AppGroup> _groups = new List<AppGroup>();

        public NodePermissionsForm(ConnectionInfo node)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            FormBuilder.SetupDialog(this, $"{MultiUserText.PermissionsTitle} - {node.Name}", 460, 380);

            var table = FormBuilder.FieldTable();
            FormBuilder.AddRow(table, null, new Label
            {
                Text = string.Format(MultiUserText.PermissionsHeader, node.Name),
                AutoSize = true,
                MaximumSize = new Size(430, 0),
                Margin = new Padding(3, 3, 3, 10)
            });
            FormBuilder.AddRow(table, null, _lstGroups, SizeType.Percent, 100F);

            var btnOk = FormBuilder.Button(MultiUserText.Ok);
            var btnCancel = FormBuilder.Button(MultiUserText.Cancel, DialogResult.Cancel);
            btnOk.Click += OnOkClick;
            Controls.Add(table);
            Controls.Add(FormBuilder.ButtonBar(btnCancel, btnOk));
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Load += OnLoad;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            try
            {
                _groups = MultiUserService.Repository.GetGroups();
                MultiUserService.ReloadPermissions(false);
            }
            catch (Exception ex)
            {
                FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                Close();
                return;
            }

            if (_groups.Count == 0)
            {
                FormBuilder.ShowError(this, MultiUserText.NoGroupsDefined);
                Close();
                return;
            }

            var assigned = MultiUserService.GetAssignedGroups(_node);
            foreach (var group in _groups)
                _lstGroups.Items.Add(group, assigned.Contains(group.GroupId));
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            try
            {
                var selected = _lstGroups.CheckedItems.Cast<AppGroup>().Select(g => g.GroupId).ToList();
                MultiUserService.SetNodePermissions(_node, selected, _groups);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
            }
        }
    }
}

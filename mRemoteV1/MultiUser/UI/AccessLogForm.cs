using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Shows and filters the access log (who connected to which server, when, from where).
    /// </summary>
    public class AccessLogForm : Form
    {
        private const int MaxRows = 5000;

        private readonly DateTimePicker _dtFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        private readonly DateTimePicker _dtTo = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110 };
        private readonly TextBox _txtUser = new TextBox { Width = 120 };
        private readonly TextBox _txtServer = new TextBox { Width = 140 };
        private readonly ComboBox _cmbAction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly ListView _lvLog = FormBuilder.DetailsList();
        private readonly Label _lblCount = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        private List<AccessLogEntry> _entries = new List<AccessLogEntry>();

        public AccessLogForm()
        {
            FormBuilder.SetupResizable(this, MultiUserText.LogTitle, 1100, 560);

            _dtFrom.Value = DateTime.Today.AddDays(-7);
            _dtTo.Value = DateTime.Today;
            _cmbAction.Items.Add(MultiUserText.LogAllActions);
            _cmbAction.Items.AddRange(AccessAction.All.Cast<object>().ToArray());
            _cmbAction.SelectedIndex = 0;

            var btnSearch = FormBuilder.Button(MultiUserText.Search);
            var btnExport = FormBuilder.Button(MultiUserText.ExportCsv);
            btnSearch.Click += (s, e) => RunQuery();
            btnExport.Click += (s, e) => ExportCsv();

            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            AddFilter(filters, MultiUserText.LogFrom, _dtFrom);
            AddFilter(filters, MultiUserText.LogTo, _dtTo);
            AddFilter(filters, MultiUserText.LogUser, _txtUser);
            AddFilter(filters, MultiUserText.LogServer, _txtServer);
            AddFilter(filters, MultiUserText.LogAction, _cmbAction);
            filters.Controls.Add(btnSearch);
            filters.Controls.Add(btnExport);
            filters.Controls.Add(_lblCount);

            _lvLog.Columns.Add(MultiUserText.ColTime, 135);
            _lvLog.Columns.Add(MultiUserText.ColUsername, 100);
            _lvLog.Columns.Add(MultiUserText.ColAction, 100);
            _lvLog.Columns.Add(MultiUserText.ColConnection, 150);
            _lvLog.Columns.Add(MultiUserText.ColHost, 130);
            _lvLog.Columns.Add(MultiUserText.ColProtocol, 65);
            _lvLog.Columns.Add(MultiUserText.ColMachine, 100);
            _lvLog.Columns.Add(MultiUserText.ColWindowsUser, 130);
            _lvLog.Columns.Add(MultiUserText.ColDetails, 250);

            Controls.Add(_lvLog);
            Controls.Add(filters);
            AcceptButton = btnSearch;

            Load += (s, e) => RunQuery();
        }

        private static void AddFilter(FlowLayoutPanel panel, string label, Control control)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 8, 0, 3) });
            panel.Controls.Add(control);
        }

        private void RunQuery()
        {
            var query = new AccessLogQuery
            {
                FromUtc = _dtFrom.Value.Date.ToUniversalTime(),
                ToUtc = _dtTo.Value.Date.AddDays(1).ToUniversalTime(),
                Username = _txtUser.Text,
                Server = _txtServer.Text,
                Action = _cmbAction.SelectedIndex > 0 ? (string)_cmbAction.SelectedItem : "",
                MaxRows = MaxRows
            };

            try
            {
                Cursor = Cursors.WaitCursor;
                _entries = MultiUserService.Repository.QueryLog(query);
            }
            catch (Exception ex)
            {
                FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            _lvLog.BeginUpdate();
            _lvLog.Items.Clear();
            foreach (var entry in _entries)
            {
                var item = new ListViewItem(entry.EventTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(entry.Username);
                item.SubItems.Add(entry.Action);
                item.SubItems.Add(entry.ConnectionName);
                item.SubItems.Add(entry.Hostname);
                item.SubItems.Add(entry.Protocol);
                item.SubItems.Add(entry.MachineName);
                item.SubItems.Add(entry.WindowsUser);
                item.SubItems.Add(entry.Details);
                item.ForeColor = ColorFor(entry.Action);
                _lvLog.Items.Add(item);
            }
            _lvLog.EndUpdate();
            _lblCount.Text = string.Format(MultiUserText.LogCount, _entries.Count, MaxRows);
        }

        private static Color ColorFor(string action)
        {
            switch (action)
            {
                case AccessAction.LoginFailed:
                case AccessAction.AccessDenied:
                    return Color.Firebrick;
                case AccessAction.Connect:
                    return Color.DarkGreen;
                case AccessAction.Disconnect:
                    return Color.DimGray;
                default:
                    return SystemColors.WindowText;
            }
        }

        private void ExportCsv()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"mRemoteNG-acessos-{DateTime.Now:yyyyMMdd-HHmm}.csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var sb = new StringBuilder();
                sb.AppendLine(string.Join(";", new[]
                {
                    MultiUserText.ColTime, MultiUserText.ColUsername, MultiUserText.ColAction, MultiUserText.ColConnection,
                    MultiUserText.ColHost, MultiUserText.ColProtocol, MultiUserText.ColMachine, MultiUserText.ColWindowsUser,
                    MultiUserText.ColDetails
                }.Select(Csv)));

                foreach (var e in _entries)
                {
                    sb.AppendLine(string.Join(";", new[]
                    {
                        e.EventTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), e.Username, e.Action, e.ConnectionName,
                        e.Hostname, e.Protocol, e.MachineName, e.WindowsUser, e.Details
                    }.Select(Csv)));
                }

                try
                {
                    File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    FormBuilder.ShowError(this, string.Format(MultiUserText.SaveFailed, ex.Message));
                }
            }
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            // neutralise spreadsheet formulas
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0)
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}

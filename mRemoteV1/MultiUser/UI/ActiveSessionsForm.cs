using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace mRemoteNG.MultiUser.UI
{
    /// <summary>
    /// Lists every connection currently open by any mRemoteNG user. Refreshes automatically.
    /// </summary>
    public class ActiveSessionsForm : Form
    {
        private readonly ListView _lvSessions = FormBuilder.DetailsList();
        private readonly Label _lblCount = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        private readonly Timer _durationTimer = new Timer { Interval = 30 * 1000 };

        public ActiveSessionsForm()
        {
            FormBuilder.SetupResizable(this, MultiUserText.SessionsTitle, 900, 400);

            _lvSessions.Columns.Add(MultiUserText.ColUsername, 150);
            _lvSessions.Columns.Add(MultiUserText.ColConnection, 180);
            _lvSessions.Columns.Add(MultiUserText.ColHost, 160);
            _lvSessions.Columns.Add(MultiUserText.ColProtocol, 70);
            _lvSessions.Columns.Add(MultiUserText.ColMachine, 120);
            _lvSessions.Columns.Add(MultiUserText.ColSince, 120);
            _lvSessions.Columns.Add(MultiUserText.ColDuration, 80);

            var btnRefresh = FormBuilder.Button(MultiUserText.Refresh);
            btnRefresh.Click += (s, e) => ActiveSessionTracker.Instance.RefreshNow();
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            top.Controls.Add(btnRefresh);
            top.Controls.Add(_lblCount);

            Controls.Add(_lvSessions);
            Controls.Add(top);

            ActiveSessionTracker.Instance.SessionsChanged += OnSessionsChanged;
            _durationTimer.Tick += (s, e) => Populate();
            Load += (s, e) =>
            {
                Populate();
                _durationTimer.Start();
                ActiveSessionTracker.Instance.RefreshNow();
            };
            FormClosed += (s, e) =>
            {
                ActiveSessionTracker.Instance.SessionsChanged -= OnSessionsChanged;
                _durationTimer.Stop();
                _durationTimer.Dispose();
            };
        }

        private void OnSessionsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)Populate);
            }
            catch (InvalidOperationException)
            {
                // form closing
            }
        }

        private void Populate()
        {
            var sessions = ActiveSessionTracker.Instance.GetAllSessions()
                .OrderBy(s => s.ConnectionName)
                .ThenBy(s => s.StartedAtUtc)
                .ToList();

            _lvSessions.BeginUpdate();
            _lvSessions.Items.Clear();
            foreach (var session in sessions)
            {
                var isMine = session.ClientId == MultiUserService.ClientId;
                var item = new ListViewItem(session.FriendlyUserName + (isMine ? MultiUserText.You : ""));
                item.SubItems.Add(session.ConnectionName);
                item.SubItems.Add(session.Hostname);
                item.SubItems.Add(session.Protocol);
                item.SubItems.Add(session.MachineName);
                item.SubItems.Add(session.StartedAtUtc.ToLocalTime().ToString("g"));
                var duration = DateTime.UtcNow - session.StartedAtUtc;
                item.SubItems.Add(duration.TotalHours >= 1
                    ? $"{(int)duration.TotalHours}h{duration.Minutes:00}"
                    : $"{Math.Max(0, duration.Minutes)} min");

                // several people on the same host at the same time: highlight
                var sameHost = sessions.Count(s => string.Equals(s.Hostname, session.Hostname, StringComparison.OrdinalIgnoreCase));
                if (sameHost > 1)
                    item.BackColor = Color.MistyRose;
                if (isMine)
                    item.ForeColor = SystemColors.GrayText;

                _lvSessions.Items.Add(item);
            }
            _lvSessions.EndUpdate();
            _lblCount.Text = string.Format(MultiUserText.SessionsCount, sessions.Count);
        }
    }
}

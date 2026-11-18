using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Messages;

namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// Publishes the connections opened by this instance to tblActiveSessions and
    /// periodically reads the sessions of every other instance, so that the
    /// connection tree can show who is currently connected to each server.
    /// </summary>
    public class ActiveSessionTracker
    {
        public static ActiveSessionTracker Instance { get; } = new ActiveSessionTracker();

        private const int PollIntervalMs = 10 * 1000;
        private const int HeartbeatIntervalMs = 30 * 1000;

        private readonly object _lock = new object();
        private readonly Dictionary<ProtocolBase, ActiveSession> _ownSessions = new Dictionary<ProtocolBase, ActiveSession>();
        private List<ActiveSession> _allSessions = new List<ActiveSession>();
        private Timer _pollTimer;
        private Timer _heartbeatTimer;
        private bool _running;
        private int _polling;

        /// <summary>Raised on a worker thread when the list of active sessions changed.</summary>
        public event EventHandler SessionsChanged;

        private ActiveSessionTracker()
        {
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
            }
            _pollTimer = new Timer(_ => Poll(), null, 0, PollIntervalMs);
            _heartbeatTimer = new Timer(_ => Heartbeat(), null, HeartbeatIntervalMs, HeartbeatIntervalMs);
        }

        /// <summary>Stops the timers and removes every session record of this instance (blocking).</summary>
        public void Stop()
        {
            List<ActiveSession> open;
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
                open = _ownSessions.Values.ToList();
                _ownSessions.Clear();
            }

            _pollTimer?.Dispose();
            _heartbeatTimer?.Dispose();

            foreach (var session in open)
                MultiUserService.WriteLogSafe(BuildLogEntry(AccessAction.Disconnect, session, "Encerrada ao sair da aplicação"));

            try
            {
                MultiUserService.Repository.RemoveActiveSessionsOfClient(MultiUserService.ClientId);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage("Could not clear active sessions", ex, MessageClass.WarningMsg);
            }
        }

        #region Own sessions
        public void RegisterConnection(ProtocolBase protocol, ConnectionInfo connectionInfo)
        {
            if (!MultiUserService.IsActive || protocol == null || connectionInfo == null) return;

            var user = MultiUserService.CurrentUser;
            var session = new ActiveSession
            {
                SessionId = Guid.NewGuid(),
                ClientId = MultiUserService.ClientId,
                UserId = user.UserId,
                Username = user.Username,
                DisplayName = user.DisplayName,
                ConstantId = connectionInfo.ConstantID ?? "",
                ConnectionName = connectionInfo.Name ?? "",
                Hostname = connectionInfo.Hostname ?? "",
                Protocol = connectionInfo.Protocol.ToString(),
                MachineName = Environment.MachineName,
                StartedAtUtc = DateTime.UtcNow,
                LastHeartbeatUtc = DateTime.UtcNow
            };

            lock (_lock)
            {
                if (!_running) return;
                _ownSessions[protocol] = session;
                _allSessions = _allSessions.Concat(new[] { session }).ToList();
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            var othersAlreadyThere = GetSessionsOfOthers(connectionInfo).ToList();
            var details = othersAlreadyThere.Count == 0
                ? ""
                : "Já ligados: " + string.Join(", ", othersAlreadyThere.Select(s => $"{s.Username}@{s.MachineName}"));

            Task.Run(() =>
            {
                try
                {
                    MultiUserService.Repository.AddActiveSession(session);
                }
                catch (Exception ex)
                {
                    Runtime.MessageCollector.AddExceptionMessage("Could not register the active session", ex, MessageClass.WarningMsg);
                }
                MultiUserService.WriteLogSafe(BuildLogEntry(AccessAction.Connect, session, details));
            });
        }

        public void UnregisterConnection(ProtocolBase protocol)
        {
            if (protocol == null) return;
            ActiveSession session;
            lock (_lock)
            {
                if (!_running) return;
                if (!_ownSessions.TryGetValue(protocol, out session)) return;
                _ownSessions.Remove(protocol);
                _allSessions = _allSessions.Where(s => s.SessionId != session.SessionId).ToList();
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            var duration = DateTime.UtcNow - session.StartedAtUtc;
            Task.Run(() =>
            {
                try
                {
                    MultiUserService.Repository.RemoveActiveSession(session.SessionId);
                }
                catch (Exception ex)
                {
                    Runtime.MessageCollector.AddExceptionMessage("Could not remove the active session", ex, MessageClass.WarningMsg);
                }
                MultiUserService.WriteLogSafe(BuildLogEntry(AccessAction.Disconnect, session, $"Duração: {FormatDuration(duration)}"));
            });
        }

        private static AccessLogEntry BuildLogEntry(string action, ActiveSession session, string details)
        {
            return new AccessLogEntry
            {
                Action = action,
                UserId = session.UserId,
                Username = session.Username,
                ConstantId = session.ConstantId,
                ConnectionName = session.ConnectionName,
                Hostname = session.Hostname,
                Protocol = session.Protocol,
                MachineName = session.MachineName,
                WindowsUser = $"{Environment.UserDomainName}\\{Environment.UserName}",
                Details = details ?? ""
            };
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h{duration.Minutes:00}m"
                : $"{duration.Minutes}m{duration.Seconds:00}s";
        }
        #endregion

        #region Shared view
        private void Poll()
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1) return;
            try
            {
                if (!_running) return;
                var sessions = MultiUserService.Repository.GetActiveSessions();

                bool changed;
                lock (_lock)
                {
                    // the database may not have our newest sessions yet: keep them in the local view
                    var known = new HashSet<Guid>(sessions.Select(s => s.SessionId));
                    sessions.AddRange(_ownSessions.Values.Where(s => !known.Contains(s.SessionId)));

                    changed = !new HashSet<Guid>(_allSessions.Select(s => s.SessionId))
                        .SetEquals(sessions.Select(s => s.SessionId));
                    _allSessions = sessions;
                }

                if (changed)
                    SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage("Could not read the active sessions", ex, MessageClass.WarningMsg);
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private void Heartbeat()
        {
            try
            {
                if (!_running) return;
                MultiUserService.Repository.Heartbeat(MultiUserService.ClientId);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage("Active session heartbeat failed", ex, MessageClass.WarningMsg);
            }
        }

        /// <summary>Forces an immediate refresh from the database (in the background).</summary>
        public void RefreshNow()
        {
            if (_running)
                Task.Run(() => Poll());
        }

        public IReadOnlyList<ActiveSession> GetAllSessions()
        {
            lock (_lock)
                return _allSessions.ToList();
        }

        /// <summary>
        /// Sessions opened by other mRemoteNG instances (other users, or the same user on another PC)
        /// for the same tree node or for the same host name.
        /// </summary>
        public IEnumerable<ActiveSession> GetSessionsOfOthers(ConnectionInfo connectionInfo)
        {
            if (!MultiUserService.IsActive || connectionInfo == null) return Enumerable.Empty<ActiveSession>();
            List<ActiveSession> snapshot;
            lock (_lock)
                snapshot = _allSessions;

            return snapshot.Where(s => s.ClientId != MultiUserService.ClientId && Matches(s, connectionInfo)).ToList();
        }

        public bool IsInUseByOthers(ConnectionInfo connectionInfo)
        {
            return GetSessionsOfOthers(connectionInfo).Any();
        }

        private static bool Matches(ActiveSession session, ConnectionInfo connectionInfo)
        {
            if (!string.IsNullOrEmpty(session.ConstantId) &&
                string.Equals(session.ConstantId, connectionInfo.ConstantID, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrWhiteSpace(session.Hostname) &&
                   !string.IsNullOrWhiteSpace(connectionInfo.Hostname) &&
                   string.Equals(session.Hostname.Trim(), connectionInfo.Hostname.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Text describing who is connected, for tooltips and dialogs.</summary>
        public string DescribeSessionsOfOthers(ConnectionInfo connectionInfo)
        {
            var sessions = GetSessionsOfOthers(connectionInfo).ToList();
            if (sessions.Count == 0) return "";
            var lines = sessions.Select(s =>
                $"• {s.FriendlyUserName} ({s.MachineName}) desde {s.StartedAtUtc.ToLocalTime():HH:mm} via {s.Protocol}");
            return MultiUserText.InUseBy + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }
        #endregion
    }
}

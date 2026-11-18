using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// Entry point for the users / groups / permissions / access log feature.
    /// The feature is only active when connections are stored in SQL Server and
    /// "MultiUserEnabled" is checked in Options &gt; SQL Server.
    /// </summary>
    public static class MultiUserService
    {
        private static readonly object PermissionsLock = new object();
        private static Dictionary<string, HashSet<int>> _nodePermissions = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<int> _currentGroupIds = new HashSet<int>();
        private static Dictionary<int, string> _groupNames = new Dictionary<int, string>();
        private static Timer _refreshTimer;
        private const int PermissionsRefreshMs = 60 * 1000;

        public static MultiUserRepository Repository { get; private set; } = new MultiUserRepository();

        /// <summary>Unique id of this running instance of mRemoteNG.</summary>
        public static Guid ClientId { get; } = Guid.NewGuid();

        public static AppUser CurrentUser { get; private set; }

        /// <summary>True when the feature is configured. Does not mean that someone is logged in.</summary>
        public static bool IsConfigured => Settings.Default.UseSQLServer && Settings.Default.MultiUserEnabled;

        /// <summary>True when the feature is configured and a user is logged in.</summary>
        public static bool IsActive => IsConfigured && CurrentUser != null;

        public static bool IsAdmin => IsActive && CurrentUser.IsAdmin;

        /// <summary>Raised (on a worker thread) when node permissions or the current user's groups change.</summary>
        public static event EventHandler PermissionsChanged;

        #region Login / logout
        /// <summary>Called by the login form after a successful authentication.</summary>
        internal static void SetLoggedInUser(AppUser user)
        {
            CurrentUser = user ?? throw new ArgumentNullException(nameof(user));
            lock (PermissionsLock)
                _currentGroupIds = new HashSet<int>(user.GroupIds);

            ReloadPermissions(false);
            Log(AccessAction.Login);

            _refreshTimer?.Dispose();
            _refreshTimer = new Timer(_ => ReloadPermissions(true), null, PermissionsRefreshMs, PermissionsRefreshMs);
            ActiveSessionTracker.Instance.Start();
        }

        /// <summary>
        /// Closes all session records of this instance and writes the logout entry.
        /// Blocking (with a timeout) so that it completes before the process exits.
        /// </summary>
        public static void Shutdown()
        {
            if (!IsActive) return;

            _refreshTimer?.Dispose();
            _refreshTimer = null;

            var task = Task.Run(() =>
            {
                ActiveSessionTracker.Instance.Stop();
                WriteLogSafe(BuildEntry(AccessAction.Logout, null, ""));
            });
            task.Wait(TimeSpan.FromSeconds(5));
            CurrentUser = null;
        }

        public static void ChangeOwnPassword(string newPassword)
        {
            if (!IsActive) return;
            Repository.SetPassword(CurrentUser.UserId, newPassword, false);
            CurrentUser.MustChangePassword = false;
            Log(AccessAction.PasswordChanged, null, CurrentUser.Username);
        }
        #endregion

        #region Permissions
        public static void ReloadPermissions(bool raiseEventIfChanged)
        {
            if (!IsActive) return;
            try
            {
                var permissions = Repository.GetNodePermissions();
                var groups = new HashSet<int>(Repository.GetGroupIdsOfUser(CurrentUser.UserId));
                var groupNames = CurrentUser.IsAdmin
                    ? Repository.GetGroups().ToDictionary(g => g.GroupId, g => g.Name)
                    : new Dictionary<int, string>();
                bool changed;
                lock (PermissionsLock)
                {
                    _groupNames = groupNames;
                    changed = !SamePermissions(_nodePermissions, permissions) || !_currentGroupIds.SetEquals(groups);
                    _nodePermissions = permissions;
                    _currentGroupIds = groups;
                }
                CurrentUser.GroupIds = groups.ToList();

                if (changed && raiseEventIfChanged)
                    PermissionsChanged?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage("Could not load user/group permissions", ex, MessageClass.WarningMsg);
            }
        }

        private static bool SamePermissions(Dictionary<string, HashSet<int>> a, Dictionary<string, HashSet<int>> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var other) || !other.SetEquals(pair.Value))
                    return false;
            }
            return true;
        }

        /// <summary>Group ids explicitly assigned to the given node (empty = not restricted).</summary>
        public static HashSet<int> GetAssignedGroups(ConnectionInfo node)
        {
            if (node == null) return new HashSet<int>();
            lock (PermissionsLock)
            {
                return _nodePermissions.TryGetValue(node.ConstantID ?? "", out var groups)
                    ? new HashSet<int>(groups)
                    : new HashSet<int>();
            }
        }

        public static string GetGroupNames(IEnumerable<int> groupIds)
        {
            lock (PermissionsLock)
            {
                return string.Join(", ", groupIds
                    .Select(id => _groupNames.TryGetValue(id, out var name) ? name : $"#{id}")
                    .OrderBy(n => n));
            }
        }

        public static bool IsRestricted(ConnectionInfo node)
        {
            return GetAssignedGroups(node).Count > 0;
        }

        /// <summary>
        /// A node is accessible when, for itself and for every parent folder that is
        /// restricted to one or more groups, the current user belongs to at least one
        /// of those groups. Administrators can access everything.
        /// </summary>
        public static bool CanAccess(ConnectionInfo node)
        {
            if (!IsActive || node == null) return true;
            if (CurrentUser.IsAdmin) return true;
            if (node is RootNodeInfo) return true;
            if (node is PuttySessionInfo) return true;

            lock (PermissionsLock)
            {
                if (_nodePermissions.Count == 0) return true;

                for (var current = node; current != null; current = current.Parent)
                {
                    if (current is RootNodeInfo) break;
                    if (!_nodePermissions.TryGetValue(current.ConstantID ?? "", out var allowedGroups))
                        continue;
                    if (allowedGroups.Count > 0 && !allowedGroups.Overlaps(_currentGroupIds))
                        return false;
                }
            }
            return true;
        }

        public static void SetNodePermissions(ConnectionInfo node, IEnumerable<int> groupIds, IEnumerable<AppGroup> allGroups)
        {
            var ids = groupIds.ToList();
            Repository.SetNodePermissions(node.ConstantID, ids);
            var names = allGroups.Where(g => ids.Contains(g.GroupId)).Select(g => g.Name);
            Log(AccessAction.PermissionsChanged, node, ids.Count == 0 ? "(sem restrições)" : string.Join(", ", names));
            ReloadPermissions(true);
        }
        #endregion

        #region Logging
        /// <summary>Writes an entry to the access log in the background. Never throws.</summary>
        public static void Log(string action, ConnectionInfo connection = null, string details = "")
        {
            if (!IsConfigured) return;
            var entry = BuildEntry(action, connection, details);
            Task.Run(() => WriteLogSafe(entry));
        }

        /// <summary>Log entry for a failed login (no user is logged in yet).</summary>
        public static void LogFailedLogin(string username, string details)
        {
            var entry = BuildEntry(AccessAction.LoginFailed, null, details);
            entry.Username = username;
            entry.UserId = null;
            Task.Run(() => WriteLogSafe(entry));
        }

        internal static AccessLogEntry BuildEntry(string action, ConnectionInfo connection, string details)
        {
            return new AccessLogEntry
            {
                Action = action,
                UserId = CurrentUser?.UserId,
                Username = CurrentUser?.Username ?? "",
                ConstantId = connection?.ConstantID ?? "",
                ConnectionName = connection?.Name ?? "",
                Hostname = connection?.Hostname ?? "",
                Protocol = connection?.Protocol.ToString() ?? "",
                MachineName = Environment.MachineName,
                WindowsUser = $"{Environment.UserDomainName}\\{Environment.UserName}",
                Details = details ?? ""
            };
        }

        internal static void WriteLogSafe(AccessLogEntry entry)
        {
            try
            {
                Repository.WriteLog(entry);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage("Could not write to the access log", ex, MessageClass.WarningMsg);
            }
        }
        #endregion

        /// <summary>Helper to list every node of the current connection tree.</summary>
        internal static IEnumerable<ConnectionInfo> AllNodes()
        {
            var model = Runtime.ConnectionsService?.ConnectionTreeModel;
            return model == null ? Enumerable.Empty<ConnectionInfo>() : model.GetRecursiveChildList();
        }

        internal static bool IsFolder(ConnectionInfo node) => node is ContainerInfo;
    }
}

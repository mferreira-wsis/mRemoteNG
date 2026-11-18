using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using mRemoteNG.Config.DatabaseConnectors;

namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// Data access for users, groups, folder/connection permissions, the access log
    /// and the list of currently active sessions. Everything lives in the same
    /// SQL Server database that mRemoteNG uses to store connections.
    /// </summary>
    public class MultiUserRepository
    {
        /// <summary>A session whose heartbeat is older than this is considered dead (crash, network loss...).</summary>
        public const int StaleSessionSeconds = 120;

        private const string SchemaSql = @"
IF OBJECT_ID(N'dbo.tblUsers', N'U') IS NULL
CREATE TABLE dbo.tblUsers (
    UserId              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Username            NVARCHAR(128)  NOT NULL UNIQUE,
    DisplayName         NVARCHAR(256)  NULL,
    PasswordHash        VARBINARY(64)  NOT NULL,
    PasswordSalt        VARBINARY(64)  NOT NULL,
    PasswordIterations  INT            NOT NULL,
    IsAdmin             BIT            NOT NULL DEFAULT 0,
    IsEnabled           BIT            NOT NULL DEFAULT 1,
    MustChangePassword  BIT            NOT NULL DEFAULT 0,
    CreatedAt           DATETIME       NOT NULL DEFAULT GETUTCDATE(),
    LastLoginAt         DATETIME       NULL
);

IF OBJECT_ID(N'dbo.tblGroups', N'U') IS NULL
CREATE TABLE dbo.tblGroups (
    GroupId      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name         NVARCHAR(128) NOT NULL UNIQUE,
    Description  NVARCHAR(512) NULL
);

IF OBJECT_ID(N'dbo.tblUserGroups', N'U') IS NULL
CREATE TABLE dbo.tblUserGroups (
    UserId   INT NOT NULL REFERENCES dbo.tblUsers(UserId) ON DELETE CASCADE,
    GroupId  INT NOT NULL REFERENCES dbo.tblGroups(GroupId) ON DELETE CASCADE,
    PRIMARY KEY (UserId, GroupId)
);

IF OBJECT_ID(N'dbo.tblNodePermissions', N'U') IS NULL
CREATE TABLE dbo.tblNodePermissions (
    ConstantID  NVARCHAR(128) NOT NULL,
    GroupId     INT NOT NULL REFERENCES dbo.tblGroups(GroupId) ON DELETE CASCADE,
    PRIMARY KEY (ConstantID, GroupId)
);

IF OBJECT_ID(N'dbo.tblAccessLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblAccessLog (
        LogId           BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventTime       DATETIME       NOT NULL DEFAULT GETUTCDATE(),
        UserId          INT            NULL,
        Username        NVARCHAR(128)  NULL,
        Action          NVARCHAR(32)   NOT NULL,
        ConstantID      NVARCHAR(128)  NULL,
        ConnectionName  NVARCHAR(256)  NULL,
        Hostname        NVARCHAR(512)  NULL,
        Protocol        NVARCHAR(32)   NULL,
        MachineName     NVARCHAR(128)  NULL,
        WindowsUser     NVARCHAR(256)  NULL,
        Details         NVARCHAR(1024) NULL
    );
    CREATE INDEX IX_tblAccessLog_EventTime ON dbo.tblAccessLog(EventTime);
END

IF OBJECT_ID(N'dbo.tblActiveSessions', N'U') IS NULL
CREATE TABLE dbo.tblActiveSessions (
    SessionId       UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ClientId        UNIQUEIDENTIFIER NOT NULL,
    UserId          INT              NOT NULL,
    Username        NVARCHAR(128)    NULL,
    DisplayName     NVARCHAR(256)    NULL,
    ConstantID      NVARCHAR(128)    NULL,
    ConnectionName  NVARCHAR(256)    NULL,
    Hostname        NVARCHAR(512)    NULL,
    Protocol        NVARCHAR(32)     NULL,
    MachineName     NVARCHAR(128)    NULL,
    StartedAt       DATETIME         NOT NULL DEFAULT GETUTCDATE(),
    LastHeartbeat   DATETIME         NOT NULL DEFAULT GETUTCDATE()
);
";

        private readonly Func<SqlDatabaseConnector> _connectorFactory;

        public MultiUserRepository() : this(DatabaseConnectorFactory.SqlDatabaseConnectorFromSettings)
        {
        }

        public MultiUserRepository(Func<SqlDatabaseConnector> connectorFactory)
        {
            _connectorFactory = connectorFactory ?? throw new ArgumentNullException(nameof(connectorFactory));
        }

        #region Infrastructure
        private T Execute<T>(Func<SqlConnection, T> action)
        {
            using (var connector = _connectorFactory())
            {
                connector.Connect();
                return action(connector.SqlConnection);
            }
        }

        private void Execute(Action<SqlConnection> action)
        {
            Execute<object>(c => { action(c); return null; });
        }

        private static SqlCommand Command(SqlConnection connection, string sql, params object[] nameValuePairs)
        {
            var command = new SqlCommand(sql, connection);
            for (var i = 0; i + 1 < nameValuePairs.Length; i += 2)
                command.Parameters.AddWithValue((string)nameValuePairs[i], nameValuePairs[i + 1] ?? DBNull.Value);
            return command;
        }

        private static string Str(IDataRecord r, string column)
        {
            var o = r[column];
            return o == DBNull.Value ? "" : Convert.ToString(o);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return value.Length <= max ? value : value.Substring(0, max);
        }

        public void EnsureSchema()
        {
            Execute(c =>
            {
                using (var cmd = Command(c, SchemaSql))
                    cmd.ExecuteNonQuery();
            });
        }
        #endregion

        #region Users
        public int CountUsers()
        {
            return Execute(c =>
            {
                using (var cmd = Command(c, "SELECT COUNT(*) FROM dbo.tblUsers"))
                    return Convert.ToInt32(cmd.ExecuteScalar());
            });
        }

        public List<AppUser> GetUsers()
        {
            return Execute(c =>
            {
                var users = new List<AppUser>();
                using (var cmd = Command(c, "SELECT UserId, Username, DisplayName, IsAdmin, IsEnabled, MustChangePassword, CreatedAt, LastLoginAt FROM dbo.tblUsers ORDER BY Username"))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        users.Add(ReadUser(reader));
                }

                var memberships = GetMemberships(c);
                foreach (var user in users)
                    user.GroupIds = memberships.Where(m => m.Key == user.UserId).Select(m => m.Value).ToList();
                return users;
            });
        }

        private static AppUser ReadUser(IDataRecord r)
        {
            return new AppUser
            {
                UserId = (int)r["UserId"],
                Username = Str(r, "Username"),
                DisplayName = Str(r, "DisplayName"),
                IsAdmin = (bool)r["IsAdmin"],
                IsEnabled = (bool)r["IsEnabled"],
                MustChangePassword = (bool)r["MustChangePassword"],
                CreatedAtUtc = (DateTime)r["CreatedAt"],
                LastLoginAtUtc = r["LastLoginAt"] == DBNull.Value ? (DateTime?)null : (DateTime)r["LastLoginAt"]
            };
        }

        private static List<KeyValuePair<int, int>> GetMemberships(SqlConnection c)
        {
            var list = new List<KeyValuePair<int, int>>();
            using (var cmd = Command(c, "SELECT UserId, GroupId FROM dbo.tblUserGroups"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    list.Add(new KeyValuePair<int, int>((int)reader["UserId"], (int)reader["GroupId"]));
            }
            return list;
        }

        public AppUser GetUser(int userId)
        {
            return GetUsers().FirstOrDefault(u => u.UserId == userId);
        }

        public enum AuthenticationResult
        {
            Success,
            InvalidCredentials,
            Disabled
        }

        /// <summary>
        /// Validates username/password. On success <paramref name="user"/> is filled and LastLoginAt updated.
        /// </summary>
        public AuthenticationResult Authenticate(string username, string password, out AppUser user)
        {
            AppUser found = null;
            var result = Execute(c =>
            {
                byte[] hash = null, salt = null;
                var iterations = 0;
                using (var cmd = Command(c,
                    "SELECT UserId, Username, DisplayName, IsAdmin, IsEnabled, MustChangePassword, CreatedAt, LastLoginAt, PasswordHash, PasswordSalt, PasswordIterations FROM dbo.tblUsers WHERE Username = @u",
                    "@u", username ?? ""))
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        found = ReadUser(reader);
                        hash = (byte[])reader["PasswordHash"];
                        salt = (byte[])reader["PasswordSalt"];
                        iterations = (int)reader["PasswordIterations"];
                    }
                }

                if (found == null)
                {
                    // spend the same time as a real verification to avoid username enumeration by timing
                    PasswordHasher.Hash(password ?? "");
                    return AuthenticationResult.InvalidCredentials;
                }

                if (!PasswordHasher.Verify(password ?? "", hash, salt, iterations))
                    return AuthenticationResult.InvalidCredentials;

                if (!found.IsEnabled)
                    return AuthenticationResult.Disabled;

                using (var cmd = Command(c, "UPDATE dbo.tblUsers SET LastLoginAt = GETUTCDATE() WHERE UserId = @id", "@id", found.UserId))
                    cmd.ExecuteNonQuery();

                found.GroupIds = GetMemberships(c).Where(m => m.Key == found.UserId).Select(m => m.Value).ToList();
                return AuthenticationResult.Success;
            });

            user = result == AuthenticationResult.Success ? found : null;
            return result;
        }

        public int CreateUser(AppUser user, string password)
        {
            var hashed = PasswordHasher.Hash(password);
            return Execute(c =>
            {
                using (var tx = c.BeginTransaction())
                {
                    int newId;
                    using (var cmd = Command(c,
                        @"INSERT INTO dbo.tblUsers (Username, DisplayName, PasswordHash, PasswordSalt, PasswordIterations, IsAdmin, IsEnabled, MustChangePassword)
                          OUTPUT INSERTED.UserId
                          VALUES (@u, @d, @h, @s, @i, @a, @e, @m)",
                        "@u", user.Username.Trim(), "@d", user.DisplayName, "@h", hashed.Hash, "@s", hashed.Salt,
                        "@i", hashed.Iterations, "@a", user.IsAdmin, "@e", user.IsEnabled, "@m", user.MustChangePassword))
                    {
                        cmd.Transaction = tx;
                        newId = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    ReplaceUserGroups(c, tx, newId, user.GroupIds);
                    tx.Commit();
                    user.UserId = newId;
                    return newId;
                }
            });
        }

        public void UpdateUser(AppUser user)
        {
            Execute(c =>
            {
                using (var tx = c.BeginTransaction())
                {
                    using (var cmd = Command(c,
                        @"UPDATE dbo.tblUsers SET Username = @u, DisplayName = @d, IsAdmin = @a, IsEnabled = @e, MustChangePassword = @m WHERE UserId = @id",
                        "@u", user.Username.Trim(), "@d", user.DisplayName, "@a", user.IsAdmin, "@e", user.IsEnabled,
                        "@m", user.MustChangePassword, "@id", user.UserId))
                    {
                        cmd.Transaction = tx;
                        cmd.ExecuteNonQuery();
                    }

                    ReplaceUserGroups(c, tx, user.UserId, user.GroupIds);
                    tx.Commit();
                }
            });
        }

        private static void ReplaceUserGroups(SqlConnection c, SqlTransaction tx, int userId, IEnumerable<int> groupIds)
        {
            using (var cmd = Command(c, "DELETE FROM dbo.tblUserGroups WHERE UserId = @id", "@id", userId))
            {
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }

            foreach (var groupId in (groupIds ?? Enumerable.Empty<int>()).Distinct())
            {
                using (var cmd = Command(c, "INSERT INTO dbo.tblUserGroups (UserId, GroupId) VALUES (@u, @g)", "@u", userId, "@g", groupId))
                {
                    cmd.Transaction = tx;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void SetPassword(int userId, string newPassword, bool mustChangePassword)
        {
            var hashed = PasswordHasher.Hash(newPassword);
            Execute(c =>
            {
                using (var cmd = Command(c,
                    "UPDATE dbo.tblUsers SET PasswordHash = @h, PasswordSalt = @s, PasswordIterations = @i, MustChangePassword = @m WHERE UserId = @id",
                    "@h", hashed.Hash, "@s", hashed.Salt, "@i", hashed.Iterations, "@m", mustChangePassword, "@id", userId))
                    cmd.ExecuteNonQuery();
            });
        }

        public void DeleteUser(int userId)
        {
            Execute(c =>
            {
                using (var cmd = Command(c, "DELETE FROM dbo.tblUsers WHERE UserId = @id", "@id", userId))
                    cmd.ExecuteNonQuery();
            });
        }
        #endregion

        #region Groups
        public List<AppGroup> GetGroups()
        {
            return Execute(c =>
            {
                var groups = new List<AppGroup>();
                using (var cmd = Command(c, "SELECT GroupId, Name, Description FROM dbo.tblGroups ORDER BY Name"))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        groups.Add(new AppGroup
                        {
                            GroupId = (int)reader["GroupId"],
                            Name = Str(reader, "Name"),
                            Description = Str(reader, "Description")
                        });
                    }
                }

                var memberships = GetMemberships(c);
                foreach (var group in groups)
                    group.MemberUserIds = memberships.Where(m => m.Value == group.GroupId).Select(m => m.Key).ToList();
                return groups;
            });
        }

        public int SaveGroup(AppGroup group)
        {
            return Execute(c =>
            {
                using (var tx = c.BeginTransaction())
                {
                    if (group.GroupId == 0)
                    {
                        using (var cmd = Command(c, "INSERT INTO dbo.tblGroups (Name, Description) OUTPUT INSERTED.GroupId VALUES (@n, @d)",
                            "@n", group.Name.Trim(), "@d", group.Description))
                        {
                            cmd.Transaction = tx;
                            group.GroupId = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                    }
                    else
                    {
                        using (var cmd = Command(c, "UPDATE dbo.tblGroups SET Name = @n, Description = @d WHERE GroupId = @id",
                            "@n", group.Name.Trim(), "@d", group.Description, "@id", group.GroupId))
                        {
                            cmd.Transaction = tx;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    using (var cmd = Command(c, "DELETE FROM dbo.tblUserGroups WHERE GroupId = @id", "@id", group.GroupId))
                    {
                        cmd.Transaction = tx;
                        cmd.ExecuteNonQuery();
                    }

                    foreach (var userId in group.MemberUserIds.Distinct())
                    {
                        using (var cmd = Command(c, "INSERT INTO dbo.tblUserGroups (UserId, GroupId) VALUES (@u, @g)", "@u", userId, "@g", group.GroupId))
                        {
                            cmd.Transaction = tx;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                    return group.GroupId;
                }
            });
        }

        public void DeleteGroup(int groupId)
        {
            Execute(c =>
            {
                using (var cmd = Command(c, "DELETE FROM dbo.tblGroups WHERE GroupId = @id", "@id", groupId))
                    cmd.ExecuteNonQuery();
            });
        }

        public List<int> GetGroupIdsOfUser(int userId)
        {
            return Execute(c =>
            {
                var ids = new List<int>();
                using (var cmd = Command(c, "SELECT GroupId FROM dbo.tblUserGroups WHERE UserId = @id", "@id", userId))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        ids.Add((int)reader["GroupId"]);
                }
                return ids;
            });
        }
        #endregion

        #region Node permissions
        /// <summary>
        /// Returns ConstantID -> set of group ids allowed. Nodes not present have no restriction.
        /// </summary>
        public Dictionary<string, HashSet<int>> GetNodePermissions()
        {
            return Execute(c =>
            {
                var result = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = Command(c, "SELECT ConstantID, GroupId FROM dbo.tblNodePermissions"))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = Str(reader, "ConstantID");
                        if (!result.TryGetValue(id, out var set))
                            result[id] = set = new HashSet<int>();
                        set.Add((int)reader["GroupId"]);
                    }
                }
                return result;
            });
        }

        public void SetNodePermissions(string constantId, IEnumerable<int> groupIds)
        {
            Execute(c =>
            {
                using (var tx = c.BeginTransaction())
                {
                    using (var cmd = Command(c, "DELETE FROM dbo.tblNodePermissions WHERE ConstantID = @c", "@c", constantId))
                    {
                        cmd.Transaction = tx;
                        cmd.ExecuteNonQuery();
                    }

                    foreach (var groupId in groupIds.Distinct())
                    {
                        using (var cmd = Command(c, "INSERT INTO dbo.tblNodePermissions (ConstantID, GroupId) VALUES (@c, @g)", "@c", constantId, "@g", groupId))
                        {
                            cmd.Transaction = tx;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
            });
        }
        #endregion

        #region Access log
        public void WriteLog(AccessLogEntry entry)
        {
            Execute(c =>
            {
                using (var cmd = Command(c,
                    @"INSERT INTO dbo.tblAccessLog (UserId, Username, Action, ConstantID, ConnectionName, Hostname, Protocol, MachineName, WindowsUser, Details)
                      VALUES (@uid, @u, @a, @c, @n, @h, @p, @m, @w, @d)",
                    "@uid", entry.UserId, "@u", Truncate(entry.Username, 128), "@a", Truncate(entry.Action, 32),
                    "@c", Truncate(entry.ConstantId, 128), "@n", Truncate(entry.ConnectionName, 256), "@h", Truncate(entry.Hostname, 512),
                    "@p", Truncate(entry.Protocol, 32), "@m", Truncate(entry.MachineName, 128), "@w", Truncate(entry.WindowsUser, 256),
                    "@d", Truncate(entry.Details, 1024)))
                    cmd.ExecuteNonQuery();
            });
        }

        public List<AccessLogEntry> QueryLog(AccessLogQuery query)
        {
            return Execute(c =>
            {
                var sql = "SELECT TOP (@max) LogId, EventTime, UserId, Username, Action, ConstantID, ConnectionName, Hostname, Protocol, MachineName, WindowsUser, Details FROM dbo.tblAccessLog WHERE 1 = 1";
                var cmd = new SqlCommand { Connection = c };
                cmd.Parameters.AddWithValue("@max", query.MaxRows <= 0 ? 5000 : query.MaxRows);

                if (query.FromUtc.HasValue)
                {
                    sql += " AND EventTime >= @from";
                    cmd.Parameters.AddWithValue("@from", query.FromUtc.Value);
                }
                if (query.ToUtc.HasValue)
                {
                    sql += " AND EventTime < @to";
                    cmd.Parameters.AddWithValue("@to", query.ToUtc.Value);
                }
                if (!string.IsNullOrWhiteSpace(query.Username))
                {
                    sql += " AND Username LIKE @user";
                    cmd.Parameters.AddWithValue("@user", "%" + EscapeLike(query.Username.Trim()) + "%");
                }
                if (!string.IsNullOrWhiteSpace(query.Server))
                {
                    sql += " AND (ConnectionName LIKE @srv OR Hostname LIKE @srv)";
                    cmd.Parameters.AddWithValue("@srv", "%" + EscapeLike(query.Server.Trim()) + "%");
                }
                if (!string.IsNullOrWhiteSpace(query.Action))
                {
                    sql += " AND Action = @action";
                    cmd.Parameters.AddWithValue("@action", query.Action);
                }

                cmd.CommandText = sql + " ORDER BY EventTime DESC, LogId DESC";

                var entries = new List<AccessLogEntry>();
                using (cmd)
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new AccessLogEntry
                        {
                            LogId = (long)reader["LogId"],
                            EventTimeUtc = DateTime.SpecifyKind((DateTime)reader["EventTime"], DateTimeKind.Utc),
                            UserId = reader["UserId"] == DBNull.Value ? (int?)null : (int)reader["UserId"],
                            Username = Str(reader, "Username"),
                            Action = Str(reader, "Action"),
                            ConstantId = Str(reader, "ConstantID"),
                            ConnectionName = Str(reader, "ConnectionName"),
                            Hostname = Str(reader, "Hostname"),
                            Protocol = Str(reader, "Protocol"),
                            MachineName = Str(reader, "MachineName"),
                            WindowsUser = Str(reader, "WindowsUser"),
                            Details = Str(reader, "Details")
                        });
                    }
                }
                return entries;
            });
        }

        private static string EscapeLike(string value)
        {
            return value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        }
        #endregion

        #region Active sessions
        public void AddActiveSession(ActiveSession session)
        {
            Execute(c =>
            {
                using (var cmd = Command(c,
                    @"INSERT INTO dbo.tblActiveSessions (SessionId, ClientId, UserId, Username, DisplayName, ConstantID, ConnectionName, Hostname, Protocol, MachineName)
                      VALUES (@s, @cl, @uid, @u, @d, @c, @n, @h, @p, @m)",
                    "@s", session.SessionId, "@cl", session.ClientId, "@uid", session.UserId, "@u", Truncate(session.Username, 128),
                    "@d", Truncate(session.DisplayName, 256), "@c", Truncate(session.ConstantId, 128), "@n", Truncate(session.ConnectionName, 256),
                    "@h", Truncate(session.Hostname, 512), "@p", Truncate(session.Protocol, 32), "@m", Truncate(session.MachineName, 128)))
                    cmd.ExecuteNonQuery();
            });
        }

        public void RemoveActiveSession(Guid sessionId)
        {
            Execute(c =>
            {
                using (var cmd = Command(c, "DELETE FROM dbo.tblActiveSessions WHERE SessionId = @s", "@s", sessionId))
                    cmd.ExecuteNonQuery();
            });
        }

        public void RemoveActiveSessionsOfClient(Guid clientId)
        {
            Execute(c =>
            {
                using (var cmd = Command(c, "DELETE FROM dbo.tblActiveSessions WHERE ClientId = @c", "@c", clientId))
                    cmd.ExecuteNonQuery();
            });
        }

        /// <summary>
        /// Keeps the sessions of this client alive and removes sessions of crashed clients.
        /// </summary>
        public void Heartbeat(Guid clientId)
        {
            Execute(c =>
            {
                using (var cmd = Command(c,
                    @"UPDATE dbo.tblActiveSessions SET LastHeartbeat = GETUTCDATE() WHERE ClientId = @c;
                      DELETE FROM dbo.tblActiveSessions WHERE LastHeartbeat < DATEADD(SECOND, -@stale * 5, GETUTCDATE());",
                    "@c", clientId, "@stale", StaleSessionSeconds))
                    cmd.ExecuteNonQuery();
            });
        }

        public List<ActiveSession> GetActiveSessions()
        {
            return Execute(c =>
            {
                var sessions = new List<ActiveSession>();
                using (var cmd = Command(c,
                    @"SELECT SessionId, ClientId, UserId, Username, DisplayName, ConstantID, ConnectionName, Hostname, Protocol, MachineName, StartedAt, LastHeartbeat
                      FROM dbo.tblActiveSessions
                      WHERE LastHeartbeat >= DATEADD(SECOND, -@stale, GETUTCDATE())
                      ORDER BY StartedAt",
                    "@stale", StaleSessionSeconds))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sessions.Add(new ActiveSession
                        {
                            SessionId = (Guid)reader["SessionId"],
                            ClientId = (Guid)reader["ClientId"],
                            UserId = (int)reader["UserId"],
                            Username = Str(reader, "Username"),
                            DisplayName = Str(reader, "DisplayName"),
                            ConstantId = Str(reader, "ConstantID"),
                            ConnectionName = Str(reader, "ConnectionName"),
                            Hostname = Str(reader, "Hostname"),
                            Protocol = Str(reader, "Protocol"),
                            MachineName = Str(reader, "MachineName"),
                            StartedAtUtc = DateTime.SpecifyKind((DateTime)reader["StartedAt"], DateTimeKind.Utc),
                            LastHeartbeatUtc = DateTime.SpecifyKind((DateTime)reader["LastHeartbeat"], DateTimeKind.Utc)
                        });
                    }
                }
                return sessions;
            });
        }
        #endregion
    }
}

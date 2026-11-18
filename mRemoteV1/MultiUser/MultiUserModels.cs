using System;
using System.Collections.Generic;

namespace mRemoteNG.MultiUser
{
    public class AppUser
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool MustChangePassword { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastLoginAtUtc { get; set; }
        public List<int> GroupIds { get; set; } = new List<int>();

        public string FriendlyName => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

        public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? Username : $"{DisplayName} ({Username})";
    }

    public class AppGroup
    {
        public int GroupId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<int> MemberUserIds { get; set; } = new List<int>();

        public override string ToString() => Name;
    }

    /// <summary>
    /// A connection that is currently open by some mRemoteNG instance (this one or another).
    /// </summary>
    public class ActiveSession
    {
        public Guid SessionId { get; set; }
        public Guid ClientId { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ConstantId { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string MachineName { get; set; } = "";
        public DateTime StartedAtUtc { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }

        public string FriendlyUserName => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
    }

    public class AccessLogEntry
    {
        public long LogId { get; set; }
        public DateTime EventTimeUtc { get; set; }
        public int? UserId { get; set; }
        public string Username { get; set; } = "";
        public string Action { get; set; } = "";
        public string ConstantId { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string WindowsUser { get; set; } = "";
        public string Details { get; set; } = "";
    }

    public class AccessLogQuery
    {
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public string Username { get; set; } = "";
        public string Server { get; set; } = "";
        public string Action { get; set; } = "";
        public int MaxRows { get; set; } = 5000;
    }

    /// <summary>
    /// Values written to tblAccessLog.Action
    /// </summary>
    public static class AccessAction
    {
        public const string Login = "Login";
        public const string LoginFailed = "LoginFailed";
        public const string Logout = "Logout";
        public const string Connect = "Connect";
        public const string Disconnect = "Disconnect";
        public const string AccessDenied = "AccessDenied";
        public const string UserCreated = "UserCreated";
        public const string UserUpdated = "UserUpdated";
        public const string UserDeleted = "UserDeleted";
        public const string PasswordChanged = "PasswordChanged";
        public const string GroupCreated = "GroupCreated";
        public const string GroupUpdated = "GroupUpdated";
        public const string GroupDeleted = "GroupDeleted";
        public const string PermissionsChanged = "PermissionsChanged";

        public static readonly string[] All =
        {
            Login, LoginFailed, Logout, Connect, Disconnect, AccessDenied,
            UserCreated, UserUpdated, UserDeleted, PasswordChanged,
            GroupCreated, GroupUpdated, GroupDeleted, PermissionsChanged
        };
    }
}

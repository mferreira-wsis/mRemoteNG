namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// UI strings of the users / groups feature, kept in one place so they can be translated easily.
    /// </summary>
    internal static class MultiUserText
    {
        public const string MenuTitle = "&Utilizadores";
        public const string MenuActiveSessions = "Sessões ativas...";
        public const string MenuAccessLog = "Registo de acessos...";
        public const string MenuManageUsers = "Gerir utilizadores e grupos...";
        public const string MenuChangePassword = "Alterar palavra-passe...";
        public const string MenuLogoutAndExit = "Terminar sessão e sair";
        public const string LoggedInAs = "Sessão: {0}";
        public const string TreeMenuPermissions = "Permissões de acesso...";

        public const string InUseBy = "⚠ Em uso por:";
        public const string RestrictedTo = "Acesso restrito a: {0}";

        public const string LoginTitle = "mRemoteNG - Iniciar sessão";
        public const string LoginHeader = "Introduza as suas credenciais";
        public const string FirstAdminHeader = "Ainda não existem utilizadores. Crie o administrador inicial.";
        public const string Username = "Utilizador:";
        public const string DisplayName = "Nome:";
        public const string Password = "Palavra-passe:";
        public const string ConfirmPassword = "Confirmar:";
        public const string CurrentPassword = "Palavra-passe atual:";
        public const string NewPassword = "Nova palavra-passe:";
        public const string Ok = "OK";
        public const string Cancel = "Cancelar";
        public const string Close = "Fechar";
        public const string LoginButton = "Entrar";
        public const string CreateButton = "Criar";
        public const string InvalidCredentials = "Utilizador ou palavra-passe inválidos.";
        public const string AccountDisabled = "Esta conta está desativada. Contacte um administrador.";
        public const string TooManyAttempts = "Demasiadas tentativas falhadas. A aplicação vai fechar.";
        public const string PasswordsDoNotMatch = "As palavras-passe não coincidem.";
        public const string PasswordTooShort = "A palavra-passe tem de ter pelo menos {0} caracteres.";
        public const string UsernameRequired = "Indique o nome de utilizador.";
        public const string DatabaseError = "Não foi possível aceder à base de dados SQL Server:\r\n\r\n{0}\r\n\r\nTentar novamente?";
        public const string MustChangePassword = "Tem de definir uma nova palavra-passe antes de continuar.";
        public const string ChangePasswordTitle = "Alterar palavra-passe";
        public const string PasswordChanged = "Palavra-passe alterada.";
        public const string WrongCurrentPassword = "A palavra-passe atual está errada.";

        public const string ManageTitle = "Utilizadores e grupos";
        public const string TabUsers = "Utilizadores";
        public const string TabGroups = "Grupos";
        public const string New = "Novo...";
        public const string Edit = "Editar...";
        public const string Delete = "Eliminar";
        public const string ResetPassword = "Repor palavra-passe...";
        public const string ColUsername = "Utilizador";
        public const string ColDisplayName = "Nome";
        public const string ColAdmin = "Admin";
        public const string ColEnabled = "Ativo";
        public const string ColGroups = "Grupos";
        public const string ColLastLogin = "Último login";
        public const string ColGroupName = "Grupo";
        public const string ColDescription = "Descrição";
        public const string ColMembers = "Membros";
        public const string Yes = "Sim";
        public const string No = "Não";
        public const string UserEditTitle = "Utilizador";
        public const string GroupEditTitle = "Grupo";
        public const string IsAdmin = "Administrador (vê e gere tudo)";
        public const string IsEnabled = "Conta ativa";
        public const string MustChangePasswordOption = "Obrigar a mudar a palavra-passe no próximo login";
        public const string GroupsOfUser = "Grupos:";
        public const string GroupName = "Nome:";
        public const string GroupDescription = "Descrição:";
        public const string GroupMembers = "Membros:";
        public const string ConfirmDeleteUser = "Eliminar o utilizador \"{0}\"?\r\nO histórico de acessos é mantido.";
        public const string ConfirmDeleteGroup = "Eliminar o grupo \"{0}\"?\r\nAs pastas restritas apenas a este grupo deixam de ter essa restrição.";
        public const string CannotDeleteYourself = "Não pode eliminar ou desativar a sua própria conta.";
        public const string CannotRemoveLastAdmin = "Tem de existir pelo menos um administrador ativo.";
        public const string GroupNameRequired = "Indique o nome do grupo.";
        public const string NewPasswordFor = "Nova palavra-passe para \"{0}\"";
        public const string SaveFailed = "Não foi possível guardar:\r\n\r\n{0}";

        public const string PermissionsTitle = "Permissões de acesso";
        public const string PermissionsHeader = "Grupos que podem ver e ligar a \"{0}\".\r\nSem nenhum grupo marcado, o acesso não é restrito por este elemento.\r\nAs restrições aplicam-se a tudo o que estiver dentro da pasta. Os administradores têm sempre acesso.";
        public const string NoGroupsDefined = "Ainda não existem grupos. Crie-os em Utilizadores > Gerir utilizadores e grupos.";

        public const string AccessDenied = "Não tem permissão para aceder a \"{0}\".";

        public const string LogTitle = "Registo de acessos";
        public const string LogFrom = "De:";
        public const string LogTo = "Até:";
        public const string LogUser = "Utilizador:";
        public const string LogServer = "Servidor:";
        public const string LogAction = "Ação:";
        public const string LogAllActions = "(todas)";
        public const string Search = "Pesquisar";
        public const string ExportCsv = "Exportar CSV...";
        public const string ColTime = "Data/hora";
        public const string ColAction = "Ação";
        public const string ColConnection = "Ligação";
        public const string ColHost = "Host";
        public const string ColProtocol = "Protocolo";
        public const string ColMachine = "Máquina";
        public const string ColWindowsUser = "Conta Windows";
        public const string ColDetails = "Detalhes";
        public const string LogCount = "{0} registos (máx. {1})";

        public const string SessionsTitle = "Sessões ativas";
        public const string ColSince = "Desde";
        public const string ColDuration = "Duração";
        public const string Refresh = "Atualizar";
        public const string SessionsCount = "{0} sessões ativas";
        public const string You = " (você)";

        public const string OptionsCheckbox = "Ativar utilizadores, grupos, registo de acessos e sessões partilhadas";
    }
}

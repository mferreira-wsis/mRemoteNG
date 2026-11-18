# Utilizadores, grupos, registo de acessos e sessões partilhadas

Esta funcionalidade só fica ativa quando as ligações estão guardadas em **SQL Server**.
Tudo é guardado na mesma base de dados que o mRemoteNG já usa para as ligações (`tblCons`).

## Ativar

1. *Tools > Options > SQL Server*: configurar o servidor SQL e marcar
   **"Ativar utilizadores, grupos, registo de acessos e sessões partilhadas"**.
2. Reiniciar o mRemoteNG.
3. No primeiro arranque, sem utilizadores na base de dados, é pedido para criar o **administrador inicial**.
4. A partir daí, cada arranque pede utilizador e palavra-passe antes de carregar as ligações.

As tabelas são criadas automaticamente no primeiro arranque. Para isso, a conta SQL configurada
precisa de permissão `CREATE TABLE` (por exemplo `db_ddladmin`). Em alternativa, um DBA pode
correr o script no fim deste documento.

## O que faz

| Funcionalidade | Onde |
|---|---|
| Login com utilizador/palavra-passe próprios da aplicação (PBKDF2-SHA256, 100 000 iterações) | Arranque |
| Gestão de utilizadores (admin, ativo, obrigar a mudar a palavra-passe, grupos) | Menu *Utilizadores > Gerir utilizadores e grupos* (só admins) |
| Gestão de grupos e membros | Idem, separador *Grupos* |
| Restringir uma pasta ou ligação a um ou mais grupos | Botão direito na árvore > *Permissões de acesso...* (só admins) |
| Registo de acessos: login, login falhado, logout, ligar, desligar (com duração), acesso negado, alterações de utilizadores, grupos e permissões | Menu *Utilizadores > Registo de acessos* (só admins), com filtros e exportação CSV |
| Ver quem está ligado a que servidor | Menu *Utilizadores > Sessões ativas* (todos) |
| Indicação visual na árvore quando um colega está ligado a um servidor | Nome a **laranja**, **ponto vermelho** no ícone e tooltip "Em uso por: Nome (PC) desde HH:mm" |

### Regras de permissões

* Um elemento sem grupos atribuídos não tem restrições próprias.
* Uma restrição aplica-se ao elemento e a tudo o que estiver dentro dele (pastas e ligações).
* Para ver ou ligar a um servidor, o utilizador tem de pertencer a pelo menos um dos grupos
  de **cada** pasta restrita no caminho até esse servidor.
* Os administradores veem e acedem a tudo.
* Os elementos sem permissão ficam **escondidos** na árvore, mas não são apagados do modelo.
  Por isso, guardar as ligações não apaga o que o utilizador não vê.

### Sessões ativas

* Cada instância do mRemoteNG regista as ligações que abre em `tblActiveSessions` e envia um
  *heartbeat* de 30 em 30 segundos.
* As outras instâncias leem a tabela de 10 em 10 segundos e atualizam a árvore.
* Uma sessão sem *heartbeat* há mais de 2 minutos (crash, perda de rede) deixa de ser mostrada,
  e é apagada ao fim de 10 minutos.
* Um servidor aparece "em uso" quando outra instância tem aberta a mesma entrada da árvore
  (`ConstantID`) **ou** uma entrada com o mesmo hostname. Isto apanha entradas duplicadas.
* É apenas indicativo: não impede a ligação.

## Limitações de segurança (importante)

As verificações são feitas **no cliente**. Todos os postos usam a mesma conta SQL, por isso
quem tiver essas credenciais (que ficam no `user.config` de cada posto, cifradas com uma chave
conhecida) consegue ler ou alterar diretamente as tabelas, incluindo `tblCons` e `tblUsers`.
Também é possível desligar a funcionalidade editando o `user.config`.
Esta funcionalidade serve para organizar, auditar e evitar conflitos entre colegas. Não substitui
o controlo de acessos nos próprios servidores (AD, grupos de RDP, LAPS, etc.).

Se for preciso garantir o registo de auditoria, dê à conta SQL apenas `INSERT` em `tblAccessLog`
(sem `UPDATE`/`DELETE`). Para isso, crie as tabelas com um DBA usando o script abaixo.

## Script SQL

```sql
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
```

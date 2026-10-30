/*
  GestionRequerimientoDP — script de base de datos para SQL Server 2025
  Generado desde las migraciones de EF Core (dotnet ef migrations script --idempotent).
  Es idempotente: se puede ejecutar varias veces; solo aplica las migraciones que falten.
  Ejecutar en SSMS 22 (o sqlcmd) con un usuario con permisos para crear bases de datos.
  Los datos de ejemplo (usuarios, planilla, solicitudes) no van aquí: los carga la app al iniciar
  en Development (Database:Seed = true) solo si la tabla Usuarios está vacía.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
GO

IF DB_ID(N'GestionRequerimientoDP') IS NULL
    CREATE DATABASE [GestionRequerimientoDP];
GO

USE [GestionRequerimientoDP];
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE TABLE [Notificaciones] (
        [Id] int NOT NULL IDENTITY,
        [Kind] nvarchar(20) NOT NULL,
        [To] nvarchar(2000) NOT NULL,
        [Cc] nvarchar(2000) NOT NULL,
        [Subject] nvarchar(400) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [ReqIds] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SentAt] datetimeoffset NULL,
        [Status] nvarchar(20) NOT NULL,
        [Error] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_Notificaciones] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE TABLE [OkLog] (
        [Id] bigint NOT NULL IDENTITY,
        [ReqId] int NOT NULL,
        [Accion] nvarchar(20) NOT NULL,
        [UsuarioId] int NULL,
        [At] datetimeoffset NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_OkLog] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE TABLE [Usuarios] (
        [Id] int NOT NULL IDENTITY,
        [Nombre] nvarchar(120) NOT NULL,
        [Email] nvarchar(200) NOT NULL,
        [Rol] nvarchar(20) NOT NULL,
        [Activo] bit NOT NULL,
        [DomainAccount] nvarchar(200) NULL,
        [PasswordHash] nvarchar(400) NULL,
        CONSTRAINT [PK_Usuarios] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE TABLE [Solicitudes] (
        [Id] int NOT NULL IDENTITY,
        [Codigo] nvarchar(20) NOT NULL,
        [SolicitanteId] int NOT NULL,
        [CreadaAt] datetimeoffset NOT NULL,
        [DescargadaAt] datetimeoffset NULL,
        [Nota] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_Solicitudes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Solicitudes_Usuarios_SolicitanteId] FOREIGN KEY ([SolicitanteId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE TABLE [UsuarioEquipos] (
        [UsuarioId] int NOT NULL,
        [Equipo] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_UsuarioEquipos] PRIMARY KEY ([UsuarioId], [Equipo]),
        CONSTRAINT [FK_UsuarioEquipos_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE TABLE [Requerimientos] (
        [Id] int NOT NULL IDENTITY,
        [Tipo] nvarchar(20) NOT NULL,
        [Fecha] date NOT NULL,
        [Pais] nvarchar(5) NOT NULL,
        [SL] nvarchar(80) NOT NULL,
        [Tecnica] nvarchar(80) NOT NULL,
        [Cliente] nvarchar(200) NOT NULL,
        [Proyecto] nvarchar(300) NOT NULL,
        [Job] nvarchar(60) NOT NULL,
        [Entregable] nvarchar(120) NOT NULL,
        [Descripcion] nvarchar(1000) NOT NULL,
        [Comercial] nvarchar(200) NOT NULL,
        [AnalistaId] int NULL,
        [Estado] nvarchar(20) NOT NULL,
        [Creado] datetimeoffset NOT NULL,
        [Actualizado] datetimeoffset NOT NULL,
        [MaterialesAt] date NULL,
        [Nota] nvarchar(4000) NOT NULL,
        [OkAt] datetimeoffset NULL,
        [OkById] int NULL,
        [OkNota] nvarchar(1000) NOT NULL,
        [Horas] decimal(6,2) NULL,
        [Registros] int NULL,
        [Metodo] nvarchar(20) NULL,
        [AtrasoResp] nvarchar(20) NULL,
        [AtrasoMotivo] nvarchar(120) NOT NULL,
        [AtrasoCampo] nvarchar(20) NOT NULL,
        [AtrasoDetalle] nvarchar(1000) NOT NULL,
        [SolicitudId] int NULL,
        [AlertAt] date NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Requerimientos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Requerimientos_Solicitudes_SolicitudId] FOREIGN KEY ([SolicitudId]) REFERENCES [Solicitudes] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Requerimientos_Usuarios_AnalistaId] FOREIGN KEY ([AnalistaId]) REFERENCES [Usuarios] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Requerimientos_Usuarios_OkById] FOREIGN KEY ([OkById]) REFERENCES [Usuarios] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Notificaciones_Status_CreatedAt] ON [Notificaciones] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_OkLog_ReqId] ON [OkLog] ([ReqId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Requerimientos_AnalistaId] ON [Requerimientos] ([AnalistaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Requerimientos_Job] ON [Requerimientos] ([Job]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Requerimientos_OkById] ON [Requerimientos] ([OkById]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Requerimientos_SolicitudId] ON [Requerimientos] ([SolicitudId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Requerimientos_Tipo_Fecha] ON [Requerimientos] ([Tipo], [Fecha]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Solicitudes_Codigo] ON [Solicitudes] ([Codigo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE INDEX [IX_Solicitudes_SolicitanteId] ON [Solicitudes] ([SolicitanteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Usuarios_DomainAccount] ON [Usuarios] ([DomainAccount]) WHERE [DomainAccount] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Usuarios_Email] ON [Usuarios] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261025034750_Inicial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261025034750_Inicial', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [Aprobacion] nvarchar(20) NOT NULL DEFAULT N'Aprobado';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [CreadoAt] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [Origen] nvarchar(20) NOT NULL DEFAULT N'Admin';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [Rut] nvarchar(12) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [SolicitudMotivo] nvarchar(500) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    ALTER TABLE [Usuarios] ADD [UltimoAcceso] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    CREATE TABLE [AuditoriaAccesos] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetimeoffset NOT NULL,
        [Usuario] nvarchar(200) NOT NULL,
        [Accion] nvarchar(60) NOT NULL,
        [Metodo] nvarchar(40) NOT NULL,
        [Ip] nvarchar(60) NOT NULL,
        [Detalle] nvarchar(1000) NOT NULL,
        CONSTRAINT [PK_AuditoriaAccesos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    CREATE TABLE [PasswordResetTokens] (
        [Id] bigint NOT NULL IDENTITY,
        [UsuarioId] int NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [UsedAt] datetimeoffset NULL,
        [Ip] nvarchar(60) NOT NULL,
        CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PasswordResetTokens_Usuarios_UsuarioId] FOREIGN KEY ([UsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Usuarios_Rut] ON [Usuarios] ([Rut]) WHERE [Rut] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    CREATE INDEX [IX_AuditoriaAccesos_At] ON [AuditoriaAccesos] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PasswordResetTokens_TokenHash] ON [PasswordResetTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    CREATE INDEX [IX_PasswordResetTokens_UsuarioId] ON [PasswordResetTokens] ([UsuarioId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030133049_AccesoYAuditoria'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030133049_AccesoYAuditoria', N'10.0.13');
END;

COMMIT;
GO


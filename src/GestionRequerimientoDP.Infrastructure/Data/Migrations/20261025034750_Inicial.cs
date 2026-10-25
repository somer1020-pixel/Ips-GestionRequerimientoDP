using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionRequerimientoDP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    To = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Cc = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReqIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OkLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReqId = table.Column<int>(type: "int", nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OkLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    DomainAccount = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Solicitudes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SolicitanteId = table.Column<int>(type: "int", nullable: false),
                    CreadaAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DescargadaAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Solicitudes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Solicitudes_Usuarios_SolicitanteId",
                        column: x => x.SolicitanteId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioEquipos",
                columns: table => new
                {
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Equipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioEquipos", x => new { x.UsuarioId, x.Equipo });
                    table.ForeignKey(
                        name: "FK_UsuarioEquipos_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Requerimientos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Pais = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    SL = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Tecnica = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Cliente = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Proyecto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Job = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Entregable = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Comercial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AnalistaId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Creado = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actualizado = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MaterialesAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OkAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OkById = table.Column<int>(type: "int", nullable: true),
                    OkNota = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Horas = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Registros = table.Column<int>(type: "int", nullable: true),
                    Metodo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AtrasoResp = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AtrasoMotivo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AtrasoCampo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AtrasoDetalle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SolicitudId = table.Column<int>(type: "int", nullable: true),
                    AlertAt = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requerimientos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Requerimientos_Solicitudes_SolicitudId",
                        column: x => x.SolicitudId,
                        principalTable: "Solicitudes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Requerimientos_Usuarios_AnalistaId",
                        column: x => x.AnalistaId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Requerimientos_Usuarios_OkById",
                        column: x => x.OkById,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_Status_CreatedAt",
                table: "Notificaciones",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OkLog_ReqId",
                table: "OkLog",
                column: "ReqId");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_AnalistaId",
                table: "Requerimientos",
                column: "AnalistaId");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_Job",
                table: "Requerimientos",
                column: "Job");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_OkById",
                table: "Requerimientos",
                column: "OkById");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_SolicitudId",
                table: "Requerimientos",
                column: "SolicitudId");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_Tipo_Fecha",
                table: "Requerimientos",
                columns: new[] { "Tipo", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_Codigo",
                table: "Solicitudes",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_SolicitanteId",
                table: "Solicitudes",
                column: "SolicitanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_DomainAccount",
                table: "Usuarios",
                column: "DomainAccount",
                unique: true,
                filter: "[DomainAccount] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notificaciones");

            migrationBuilder.DropTable(
                name: "OkLog");

            migrationBuilder.DropTable(
                name: "Requerimientos");

            migrationBuilder.DropTable(
                name: "UsuarioEquipos");

            migrationBuilder.DropTable(
                name: "Solicitudes");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}

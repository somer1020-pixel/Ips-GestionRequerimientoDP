using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionRequerimientoDP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IndicesHistoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_SolicitanteId",
                table: "Solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_AnalistaId",
                table: "Requerimientos");

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_CreadaAt",
                table: "Solicitudes",
                column: "CreadaAt");

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_SolicitanteId_CreadaAt",
                table: "Solicitudes",
                columns: new[] { "SolicitanteId", "CreadaAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_Abiertos",
                table: "Requerimientos",
                columns: new[] { "Tipo", "Fecha" },
                filter: "[Estado] <> N'Ok'")
                .Annotation("SqlServer:Include", new[] { "AnalistaId", "Estado", "SolicitudId" });

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_AbiertosPorAnalista",
                table: "Requerimientos",
                columns: new[] { "AnalistaId", "Fecha" },
                filter: "[Estado] <> N'Ok'");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_AnalistaId_Fecha",
                table: "Requerimientos",
                columns: new[] { "AnalistaId", "Fecha" })
                .Annotation("SqlServer:Include", new[] { "Estado", "OkAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_Fecha",
                table: "Requerimientos",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_OkAt",
                table: "Requerimientos",
                column: "OkAt",
                filter: "[OkAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_SinAsignar",
                table: "Requerimientos",
                column: "SolicitudId",
                filter: "[AnalistaId] IS NULL AND [SolicitudId] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_Tipo_Analista",
                table: "Requerimientos",
                columns: new[] { "Tipo", "AnalistaId" })
                .Annotation("SqlServer:Include", new[] { "OkAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_CreatedAt",
                table: "Notificaciones",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_CreadaAt",
                table: "Solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_Solicitudes_SolicitanteId_CreadaAt",
                table: "Solicitudes");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_Abiertos",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_AbiertosPorAnalista",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_AnalistaId_Fecha",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_Fecha",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_OkAt",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_SinAsignar",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Requerimientos_Tipo_Analista",
                table: "Requerimientos");

            migrationBuilder.DropIndex(
                name: "IX_Notificaciones_CreatedAt",
                table: "Notificaciones");

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_SolicitanteId",
                table: "Solicitudes",
                column: "SolicitanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Requerimientos_AnalistaId",
                table: "Requerimientos",
                column: "AnalistaId");
        }
    }
}

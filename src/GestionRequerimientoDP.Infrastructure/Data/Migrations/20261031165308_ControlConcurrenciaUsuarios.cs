using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionRequerimientoDP.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ControlConcurrenciaUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Usuarios",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PasswordResetTokens",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PasswordResetTokens");
        }
    }
}

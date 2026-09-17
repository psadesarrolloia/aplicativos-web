using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Identidad.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTokenExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokensExtension",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Prefijo = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    HashSecreto = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoUsoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevocadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokensExtension", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokensExtension_Prefijo",
                table: "TokensExtension",
                column: "Prefijo");

            migrationBuilder.CreateIndex(
                name: "IX_TokensExtension_UsuarioId",
                table: "TokensExtension",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokensExtension");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Identidad.Migrations
{
    /// <inheritdoc />
    public partial class AccesosWeb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "MetodoSegundoFactor",
                table: "AspNetUsers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Perfil",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Usuario");

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoAccesoUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccesosEmpresa",
                columns: table => new
                {
                    UsuarioId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    UsuarioSage = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModificadoPor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModificadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccesosEmpresa", x => new { x.UsuarioId, x.Ruc });
                    table.ForeignKey(
                        name: "FK_AccesosEmpresa_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccesosLlave",
                columns: table => new
                {
                    UsuarioId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Llave = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OtorgadoPor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OtorgadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccesosLlave", x => new { x.UsuarioId, x.Ruc, x.Llave });
                    table.ForeignKey(
                        name: "FK_AccesosLlave_AccesosEmpresa_UsuarioId_Ruc",
                        columns: x => new { x.UsuarioId, x.Ruc },
                        principalTable: "AccesosEmpresa",
                        principalColumns: new[] { "UsuarioId", "Ruc" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AspNetUsers_NormalizedEmail",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccesosEmpresa_Ruc",
                table: "AccesosEmpresa",
                column: "Ruc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccesosLlave");

            migrationBuilder.DropTable(
                name: "AccesosEmpresa");

            migrationBuilder.DropIndex(
                name: "UX_AspNetUsers_NormalizedEmail",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MetodoSegundoFactor",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Perfil",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UltimoAccesoUtc",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}

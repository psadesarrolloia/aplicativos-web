using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Modules.Reportes.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguracionReportes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesReporte",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reporte = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Logo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    LogoTipo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ActualizadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesReporte", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesReporte_Ruc_Reporte",
                table: "ConfiguracionesReporte",
                columns: new[] { "Ruc", "Reporte" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesReporte");
        }
    }
}

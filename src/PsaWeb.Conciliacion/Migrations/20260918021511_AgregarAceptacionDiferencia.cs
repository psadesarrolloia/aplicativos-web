using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Conciliacion.Migrations
{
    /// <inheritdoc />
    public partial class AgregarAceptacionDiferencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComentarioAceptacion",
                table: "ComprobantesSriDescargados",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DiferenciaAceptada",
                table: "ComprobantesSriDescargados",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DiferenciaAceptadaPor",
                table: "ComprobantesSriDescargados",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiferenciaAceptadaUtc",
                table: "ComprobantesSriDescargados",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComentarioAceptacion",
                table: "ComprobantesSriDescargados");

            migrationBuilder.DropColumn(
                name: "DiferenciaAceptada",
                table: "ComprobantesSriDescargados");

            migrationBuilder.DropColumn(
                name: "DiferenciaAceptadaPor",
                table: "ComprobantesSriDescargados");

            migrationBuilder.DropColumn(
                name: "DiferenciaAceptadaUtc",
                table: "ComprobantesSriDescargados");
        }
    }
}

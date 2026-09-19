using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Conciliacion.Migrations
{
    /// <inheritdoc />
    public partial class EstadoSriEmitidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EstadosSriComprobantes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    CodDoc = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RefId = table.Column<int>(type: "int", nullable: false),
                    ClaveAcceso = table.Column<string>(type: "nvarchar(49)", maxLength: 49, nullable: true),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Fuente = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Detalle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RespuestaCruda = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FechaVerificacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadosSriComprobantes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EstadosSriComprobantes_Ruc_ClaveAcceso",
                table: "EstadosSriComprobantes",
                columns: new[] { "Ruc", "ClaveAcceso" });

            migrationBuilder.CreateIndex(
                name: "IX_EstadosSriComprobantes_Ruc_CodDoc_RefId",
                table: "EstadosSriComprobantes",
                columns: new[] { "Ruc", "CodDoc", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstadosSriComprobantes_Ruc_FechaEmision",
                table: "EstadosSriComprobantes",
                columns: new[] { "Ruc", "FechaEmision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EstadosSriComprobantes");
        }
    }
}

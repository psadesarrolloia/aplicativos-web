using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.SageBridge.Cola.Migrations
{
    /// <inheritdoc />
    public partial class AuditoriaRegistrosSage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditoriaRegistrosSage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Modulo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Accion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Documento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tercero = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TrabajoId = table.Column<long>(type: "bigint", nullable: true),
                    Resultado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HuellaPayload = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HuellaOrigen = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriaRegistrosSage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaRegistrosSage_FechaUtc",
                table: "AuditoriaRegistrosSage",
                column: "FechaUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaRegistrosSage_Ruc_Documento",
                table: "AuditoriaRegistrosSage",
                columns: new[] { "Ruc", "Documento" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaRegistrosSage_TrabajoId",
                table: "AuditoriaRegistrosSage",
                column: "TrabajoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriaRegistrosSage");
        }
    }
}

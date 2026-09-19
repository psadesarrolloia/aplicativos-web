using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Conciliacion.Migrations
{
    /// <inheritdoc />
    public partial class SolicitudesAnulacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SolicitudesAnulacion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    CodDoc = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    RefId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DatilId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    Ambiente = table.Column<short>(type: "smallint", nullable: false),
                    SolicitadaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FechaSolicitud = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaResolucion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResueltaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UltimoEstadoSri = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FechaUltimaVerificacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Detalle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesAnulacion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesAnulacion_Estado",
                table: "SolicitudesAnulacion",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesAnulacion_Ruc_CodDoc_RefId",
                table: "SolicitudesAnulacion",
                columns: new[] { "Ruc", "CodDoc", "RefId" },
                unique: true,
                filter: "[Estado] = 'Solicitada'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SolicitudesAnulacion");
        }
    }
}

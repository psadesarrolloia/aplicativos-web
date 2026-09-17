using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Conciliacion.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprobantesSriDescargados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    ClaveAcceso = table.Column<string>(type: "nvarchar(49)", maxLength: 49, nullable: false),
                    RucEmisor = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    RazonSocialEmisor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TipoComprobante = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SerieComprobante = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaAutorizacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    IdentificacionReceptor = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NumeroDocumentoModificado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    FechaVerificacionEstado = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaDescargaUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubidoPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprobantesSriDescargados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesSriDescargados_Ruc_ClaveAcceso",
                table: "ComprobantesSriDescargados",
                columns: new[] { "Ruc", "ClaveAcceso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesSriDescargados_Ruc_FechaEmision",
                table: "ComprobantesSriDescargados",
                columns: new[] { "Ruc", "FechaEmision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprobantesSriDescargados");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Recibidos.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConflictosXmlRecibidos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    ClaveAcceso = table.Column<string>(type: "nvarchar(49)", maxLength: 49, nullable: false),
                    Xml = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    HashSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubidoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FechaUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConflictosXmlRecibidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DescargasXmlFallidas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    ClaveAcceso = table.Column<string>(type: "nvarchar(49)", maxLength: 49, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    UltimoIntentoUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DescargasXmlFallidas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "XmlComprobantesRecibidos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    ClaveAcceso = table.Column<string>(type: "nvarchar(49)", maxLength: 49, nullable: false),
                    TipoComprobante = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RucEmisor = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    FechaAutorizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Xml = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    HashSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubidoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FechaCargaUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XmlComprobantesRecibidos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DescargasXmlFallidas_Ruc_ClaveAcceso",
                table: "DescargasXmlFallidas",
                columns: new[] { "Ruc", "ClaveAcceso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XmlComprobantesRecibidos_Ruc_ClaveAcceso",
                table: "XmlComprobantesRecibidos",
                columns: new[] { "Ruc", "ClaveAcceso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XmlComprobantesRecibidos_Ruc_FechaEmision",
                table: "XmlComprobantesRecibidos",
                columns: new[] { "Ruc", "FechaEmision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConflictosXmlRecibidos");

            migrationBuilder.DropTable(
                name: "DescargasXmlFallidas");

            migrationBuilder.DropTable(
                name: "XmlComprobantesRecibidos");
        }
    }
}

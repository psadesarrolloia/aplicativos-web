using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.SageBridge.Cola.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmpresasBridge",
                columns: table => new
                {
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Habilitada = table.Column<bool>(type: "bit", nullable: false),
                    Ventana = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    AccesoSage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AccesoVerificadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ModificadoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModificadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmpresasBridge", x => x.Ruc);
                });

            migrationBuilder.CreateTable(
                name: "LatidosBridge",
                columns: table => new
                {
                    Instancia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UltimoLatidoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IniciadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VersionAnfitrion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    VersionLogica = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    EmpresaAbierta = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatidosBridge", x => x.Instancia);
                });

            migrationBuilder.CreateTable(
                name: "TrabajosSage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClaveIdempotencia = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultadoJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    CreadoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NoAntesDeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TomadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TomadoHastaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IniciadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TerminadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrabajosSage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrabajosSage_CreadoUtc",
                table: "TrabajosSage",
                column: "CreadoUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TrabajosSage_Estado_NoAntesDeUtc",
                table: "TrabajosSage",
                columns: new[] { "Estado", "NoAntesDeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrabajosSage_Ruc_Estado",
                table: "TrabajosSage",
                columns: new[] { "Ruc", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_TrabajosSage_Ruc_Tipo_ClaveIdempotencia",
                table: "TrabajosSage",
                columns: new[] { "Ruc", "Tipo", "ClaveIdempotencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmpresasBridge");

            migrationBuilder.DropTable(
                name: "LatidosBridge");

            migrationBuilder.DropTable(
                name: "TrabajosSage");
        }
    }
}

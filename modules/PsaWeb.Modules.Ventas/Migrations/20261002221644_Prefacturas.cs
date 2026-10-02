using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Modules.Ventas.Migrations
{
    /// <inheritdoc />
    public partial class Prefacturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesVentas",
                columns: table => new
                {
                    Ruc = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CorreoContabilidad = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CorreoAdicional = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VigenciaDias = table.Column<int>(type: "int", nullable: false),
                    CodigoImpuesto = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PorcentajeIva = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ActualizadoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesVentas", x => x.Ruc);
                });

            migrationBuilder.CreateTable(
                name: "Prefacturas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EmpresaNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    FechaEmision = table.Column<DateTime>(type: "date", nullable: false),
                    ValidaHasta = table.Column<DateTime>(type: "date", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ClienteNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClienteContacto = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ClienteTelefono = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ClienteEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ListaDePrecios = table.Column<int>(type: "int", nullable: false),
                    DiasCredito = table.Column<int>(type: "int", nullable: false),
                    Terminos = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Vendedor = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Etiqueta = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OrdenCliente = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DireccionEnvio = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    NotaCliente = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NotaInterna = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CodigoImpuesto = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PorcentajeIva = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreadaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorreoEstado = table.Column<int>(type: "int", nullable: false),
                    CorreoDestinatarios = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorreoError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorreoEnviadoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FacturaSage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FacturadaPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FacturadaEn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prefacturas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrefacturaLineas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrefacturaId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PrecioLista = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    PrecioUnitario = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PrecioManual = table.Column<bool>(type: "bit", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExistenciaAlEmitir = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrefacturaLineas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrefacturaLineas_Prefacturas_PrefacturaId",
                        column: x => x.PrefacturaId,
                        principalTable: "Prefacturas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrefacturaLineas_PrefacturaId",
                table: "PrefacturaLineas",
                column: "PrefacturaId");

            migrationBuilder.CreateIndex(
                name: "IX_Prefacturas_Ruc_CreadaEn",
                table: "Prefacturas",
                columns: new[] { "Ruc", "CreadaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Prefacturas_Ruc_Numero",
                table: "Prefacturas",
                columns: new[] { "Ruc", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesVentas");

            migrationBuilder.DropTable(
                name: "PrefacturaLineas");

            migrationBuilder.DropTable(
                name: "Prefacturas");
        }
    }
}

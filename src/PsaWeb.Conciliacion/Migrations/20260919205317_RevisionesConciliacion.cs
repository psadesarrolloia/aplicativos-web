using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Conciliacion.Migrations
{
    /// <inheritdoc />
    public partial class RevisionesConciliacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevisionesConciliacion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ruc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Clave = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Huella = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Comentario = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RevisadaPor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RevisadaUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionesConciliacion", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevisionesConciliacion_Ruc_Clave",
                table: "RevisionesConciliacion",
                columns: new[] { "Ruc", "Clave" },
                unique: true);

            // Las aceptaciones hechas con el mecanismo anterior (columnas en ComprobantesSriDescargados)
            // pasan a la tabla nueva. Huella vacía = "aplica siempre": no se puede reconstruir la
            // huella de entonces, y no hay que reabrirle al revisor lo que ya había aceptado.
            migrationBuilder.Sql("""
                INSERT INTO RevisionesConciliacion (Ruc, Clave, Huella, Comentario, RevisadaPor, RevisadaUtc)
                SELECT Ruc, N'C:' + ClaveAcceso, N'',
                       ISNULL(NULLIF(LTRIM(RTRIM(ComentarioAceptacion)), N''), N'(aceptada antes de exigir comentario)'),
                       ISNULL(DiferenciaAceptadaPor, N''),
                       ISNULL(DiferenciaAceptadaUtc, GETUTCDATE())
                FROM ComprobantesSriDescargados
                WHERE DiferenciaAceptada = 1
                """);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            // Devuelve a las columnas viejas solo las revisiones de filas con comprobante del SRI
            // (las "Solo en Sage" no tenían dónde guardarse en el esquema anterior).
            migrationBuilder.Sql("""
                UPDATE c
                SET DiferenciaAceptada = 1, DiferenciaAceptadaPor = r.RevisadaPor,
                    DiferenciaAceptadaUtc = r.RevisadaUtc, ComentarioAceptacion = r.Comentario
                FROM ComprobantesSriDescargados c
                JOIN RevisionesConciliacion r ON r.Ruc = c.Ruc AND r.Clave = N'C:' + c.ClaveAcceso
                """);

            migrationBuilder.DropTable(
                name: "RevisionesConciliacion");
        }
    }
}

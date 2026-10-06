using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PsaWeb.Identidad.Migrations
{
    /// <inheritdoc />
    public partial class PerfilUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Perfil",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Consulta",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Usuario");

            // Perfil único (2026-10-06): las cuentas sin panel («Usuario») pasan al perfil que les corresponde por sus módulos en las empresas
            // activas — misma regla que PerfilesWeb.Deducir: anulaciones => Supervisor; emisión/registro => Digitador; solo ventas => Vendedor;
            // el resto => Consulta. No toca módulos ni empresas.
            migrationBuilder.Sql("""
                SET QUOTED_IDENTIFIER ON;
                WITH L AS (
                    SELECT a.UsuarioId, l.Llave
                    FROM AccesosEmpresa a JOIN AccesosLlave l ON l.UsuarioId = a.UsuarioId AND l.Ruc = a.Ruc
                    WHERE a.Activo = 1)
                UPDATE u SET Perfil =
                    CASE
                        WHEN EXISTS (SELECT 1 FROM L WHERE L.UsuarioId = u.Id AND L.Llave IN ('auCanceInv', 'auCanceNc', 'auCanceLiq', 'auCanceTwh')) THEN 'Supervisor'
                        WHEN EXISTS (SELECT 1 FROM L WHERE L.UsuarioId = u.Id AND L.Llave IN ('mksaleinv', 'mksalenc', 'mkpurchliq', 'mkpurchtwh',
                            'mksinBatch', 'mkncBatch', 'mkliqBatch', 'mkTwhBatch', 'mkpurchinv', 'mkimpliq')) THEN 'Digitador'
                        WHEN EXISTS (SELECT 1 FROM L WHERE L.UsuarioId = u.Id)
                         AND NOT EXISTS (SELECT 1 FROM L WHERE L.UsuarioId = u.Id AND L.Llave NOT IN ('quSalesStk', 'quSalesQte', 'mkSalesQte')) THEN 'Vendedor'
                        ELSE 'Consulta'
                    END
                FROM AspNetUsers u
                WHERE u.Perfil = 'Usuario';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET QUOTED_IDENTIFIER ON; UPDATE AspNetUsers SET Perfil = 'Usuario' WHERE Perfil NOT IN ('SuperAdmin', 'Admin');");

            migrationBuilder.AlterColumn<string>(
                name: "Perfil",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Usuario",
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Consulta");
        }
    }
}

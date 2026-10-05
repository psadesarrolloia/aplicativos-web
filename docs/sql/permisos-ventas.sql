/* ============================================================================
   Portal de ventas (inventario, precios y prefacturas) — permisos en allowAction
   Base: PeachEBills   ·   Idempotente (se puede correr varias veces)

   Filas NUEVAS (4), ver docs/PLAN-PORTAL-VENTAS.md:

     quSalesStk   Ver inventario y precios de venta           <- copia roles de qusaleinv
     quSalesQte   Ver prefacturas (cotizaciones)              <- copia roles de qusaleinv
     mkSalesQte   Emitir prefacturas (PDF + correo a Conta.)  <- copia roles de mksaleinv
     auSalesQte   Contabilidad: ver todas, marcar facturada y anular prefacturas
                                                              <- copia roles de mksaleinv

   Reglas (ReglasVentas): quien emite ve SOLO sus prefacturas; auSalesQte (Contabilidad) ve las de todos los vendedores,
   anota el número de la factura de Sage y anula. Emitir o cerrar también permiten ver.

   Mientras no se apliquen, el portal usa GateProvisional (ReglasVentas.PermisosProvisionales = true): lo habilitan las llaves de
   facturación electrónica de venta (qusaleinv ver · mksaleinv emitir y cerrar), que son las que este script copia. Al aplicar:
   poner PermisosProvisionales = false y dejar en AppCatalogo solo las 4 llaves nuevas.

   IMPORTANTE: los VENDEDORES normalmente NO tienen qusaleinv/mksaleinv. Para ellos se crea un rol (p. ej. «Vendedor») con
   quSalesStk + quSalesQte + mkSalesQte y se les asigna por empresa desde el administrador de roles; el script solo propone
   el reparto inicial según los roles que ya facturan (Contabilidad). Si no corresponde, borrar la fila de @Nuevos antes de aplicar.

   Uso:  sqlcmd -S <servidor> -d PeachEBills -E -C -v Aplicar=0 -i permisos-ventas.sql   (vista previa)
         sqlcmd -S <servidor> -d PeachEBills -E -C -v Aplicar=1 -i permisos-ventas.sql   (aplica)
   ============================================================================ */
USE [PeachEBills];
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar bit = CASE WHEN N'$(Aplicar)' = N'1' THEN 1 ELSE 0 END;
PRINT CASE WHEN @Aplicar = 1 THEN N'== MODO APLICAR ==' ELSE N'== MODO VISTA PREVIA (no cambia nada; use -v Aplicar=1 para aplicar) ==' END;

DECLARE @Nuevos TABLE (codigo nvarchar(20) PRIMARY KEY, nombre nvarchar(100), equivalente nvarchar(20));
INSERT INTO @Nuevos (codigo, nombre, equivalente) VALUES
 (N'quSalesStk', N'Ver inventario y precios de venta',                    N'qusaleinv'),
 (N'quSalesQte', N'Ver prefacturas (cotizaciones)',                       N'qusaleinv'),
 (N'mkSalesQte', N'Emitir prefacturas (cotizaciones)',                    N'mksaleinv'),
 (N'auSalesQte', N'Conta: ver todas, facturar y anular prefacturas',      N'mksaleinv');

-- allowAction: allowCode = nvarchar(10), allowName = nvarchar(50) (medido en PeachEBills).
IF EXISTS (SELECT 1 FROM @Nuevos WHERE LEN(codigo) > 10 OR LEN(nombre) > 50) THROW 50001, N'Una llave nueva excede el ancho de allowAction.', 1;

-- Roles que recibirían cada llave nueva (los que hoy tienen su equivalente, activo).
SELECT DISTINCT n.codigo, r.adrRol
INTO #Asignar
FROM @Nuevos n
JOIN adrAllowRol r ON r.adrAllowCode = n.equivalente AND r.adrActive = 1;

BEGIN TRY
    BEGIN TRAN;

    IF @Aplicar = 1
    BEGIN
        INSERT INTO allowAction (allowName, allowCode)
        SELECT n.nombre, n.codigo
        FROM @Nuevos n
        WHERE NOT EXISTS (SELECT 1 FROM allowAction a WHERE a.allowCode = n.codigo);

        INSERT INTO adrAllowRol (adrRol, adraid, adrAllowCode, adrActive)
        SELECT s.adrRol, a.aid, s.codigo, 1
        FROM #Asignar s
        JOIN allowAction a ON a.allowCode = s.codigo
        WHERE NOT EXISTS (SELECT 1 FROM adrAllowRol x WHERE x.adrRol = s.adrRol AND x.adrAllowCode = s.codigo);
    END

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

-- Qué se agregaría / quedó agregado ------------------------------------------
SELECT n.codigo AS llave_nueva,
       CASE WHEN EXISTS (SELECT 1 FROM allowAction a WHERE a.allowCode = n.codigo) THEN N'ya existe' ELSE N'se creará' END AS en_allowAction,
       n.equivalente AS copia_roles_de,
       s.adrRol AS rol_id,
       ro.rolName AS rol,
       CASE WHEN EXISTS (SELECT 1 FROM adrAllowRol x WHERE x.adrRol = s.adrRol AND x.adrAllowCode = n.codigo)
            THEN N'ya asignada' ELSE N'se asignará' END AS asignacion
FROM @Nuevos n
LEFT JOIN #Asignar s ON s.codigo = n.codigo
LEFT JOIN roles ro ON ro.rolid = s.adrRol
ORDER BY n.codigo, s.adrRol;

DROP TABLE #Asignar;

/* Rollback (solo si hiciera falta):
   DELETE FROM adrAllowRol WHERE adrAllowCode IN (N'quSalesStk',N'quSalesQte',N'mkSalesQte',N'auSalesQte');
   DELETE FROM allowAction WHERE allowCode IN (N'quSalesStk',N'quSalesQte',N'mkSalesQte',N'auSalesQte');
*/

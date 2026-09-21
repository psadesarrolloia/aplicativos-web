/* ============================================================================
   Reportes migrados de Access (Cartera / Bancos) — permisos en allowAction
   Base: PeachEBills   ·   Idempotente (se puede correr varias veces)

   Filas NUEVAS (3), patrón qu* (consulta). Mientras no se asignen a ningún rol el módulo se ve
   para todas las empresas (GateProvisional en AppCatalogo); al asignarlas, revertir cada entrada
   de AppCatalogo a  new[] { Permisos.VerReporte... }  (1 línea por reporte).

     quRptPwc     Ver reporte PWC (facturas por cobrar)         <- copia roles de qusaleinv
     quRptComis   Ver reporte de comisiones por recibos          <- copia roles de qusaleinv
     quRptChq     Imprimir cheques y comprobantes de egreso     <- copia roles de qupurchtwh

   El "equivalente" es sólo una propuesta de a quién dárselas: el área decide. En modo VISTA PREVIA
   se listan los roles que las recibirían; si no corresponde, borrar la fila de @Nuevos antes de aplicar
   y asignarlas a mano desde el administrador de roles.

   Uso:  sqlcmd -S <servidor> -d PeachEBills -E -C -v Aplicar=0 -i permisos-reportes-access.sql   (vista previa)
         sqlcmd -S <servidor> -d PeachEBills -E -C -v Aplicar=1 -i permisos-reportes-access.sql   (aplica)
   ============================================================================ */
USE [PeachEBills];
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar bit = CASE WHEN N'$(Aplicar)' = N'1' THEN 1 ELSE 0 END;
PRINT CASE WHEN @Aplicar = 1 THEN N'== MODO APLICAR ==' ELSE N'== MODO VISTA PREVIA (no cambia nada; use -v Aplicar=1 para aplicar) ==' END;

DECLARE @Nuevos TABLE (codigo nvarchar(20) PRIMARY KEY, nombre nvarchar(100), equivalente nvarchar(20));
INSERT INTO @Nuevos (codigo, nombre, equivalente) VALUES
 (N'quRptPwc',   N'Ver reporte PWC (facturas por cobrar)',             N'qusaleinv'),
 (N'quRptComis', N'Ver reporte de comisiones por recibos',            N'qusaleinv'),
 (N'quRptChq',   N'Imprimir cheques y comprobantes de egreso',        N'qupurchtwh');

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
   DELETE FROM adrAllowRol WHERE adrAllowCode IN (N'quRptPwc',N'quRptComis',N'quRptChq');
   DELETE FROM allowAction WHERE allowCode IN (N'quRptPwc',N'quRptComis',N'quRptChq');
*/

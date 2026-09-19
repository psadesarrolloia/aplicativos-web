/* ============================================================================
   Comprobantes electrónicos v2 — F1: permisos unificados en allowAction
   Base: PeachEBills   ·   Idempotente (se puede correr varias veces)

   Cada tipo de comprobante queda con las mismas 4 llaves:
       ver  ·  hacer  ·  lote  ·  autorizar anulación

   Filas NUEVAS (7):                                     se asignan a los roles que hoy
     auCanceInv   Autorizar anulación de factura         ← tienen auCanceTwh  (Supervisor)
     auCanceNc    Autorizar anulación de nota de crédito ← tienen auCanceTwh  (Supervisor)
     auCanceLiq   Autorizar anulación de liquidación     ← tienen auCanceTwh  (Supervisor)
     mkncBatch    Procesar por Lote: notas de crédito    ← tienen mksinBatch
     qupurchliq   Ver liquidaciones de compra            ← tienen qupurchtwh
     mkpurchliq   Hacer liquidación de compra            ← tienen mkpurchtwh
     mkliqBatch   Procesar por Lote: liquidaciones       ← tienen mkTwhBatch
   (las liquidaciones son documentos de compra: siguen las llaves de retenciones)

   Uso:  sqlcmd -S <servidor> -d PeachEBills -E -C -v Aplicar=0 -i permisos-comprobantes-v2.sql   (vista previa)
         sqlcmd -S <servidor> -d PeachEBills -E -C -v Aplicar=1 -i permisos-comprobantes-v2.sql   (aplica)
   ============================================================================ */
USE [PeachEBills];
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar bit = CASE WHEN N'$(Aplicar)' = N'1' THEN 1 ELSE 0 END;
PRINT CASE WHEN @Aplicar = 1 THEN N'== MODO APLICAR ==' ELSE N'== MODO VISTA PREVIA (no cambia nada; use -v Aplicar=1 para aplicar) ==' END;

DECLARE @Nuevos TABLE (codigo nvarchar(20) PRIMARY KEY, nombre nvarchar(100), equivalente nvarchar(20));
INSERT INTO @Nuevos (codigo, nombre, equivalente) VALUES
 (N'auCanceInv', N'Autorizar anulaci' + NCHAR(243) + N'n de factura',                                       N'auCanceTwh'),
 (N'auCanceNc',  N'Autorizar anulaci' + NCHAR(243) + N'n de nota de cr' + NCHAR(233) + N'dito',             N'auCanceTwh'),
 (N'auCanceLiq', N'Autorizar anulaci' + NCHAR(243) + N'n de liquidaci' + NCHAR(243) + N'n de compra',       N'auCanceTwh'),
 (N'mkncBatch',  N'Procesar por Lote: Notas de cr' + NCHAR(233) + N'dito electr' + NCHAR(243) + N'nicas',   N'mksinBatch'),
 (N'qupurchliq', N'Ver Liquidaciones de compra',                                                              N'qupurchtwh'),
 (N'mkpurchliq', N'Hacer Liquidaci' + NCHAR(243) + N'n de compra electr' + NCHAR(243) + N'nica',            N'mkpurchtwh'),
 (N'mkliqBatch', N'Procesar por Lote: Liquidaciones de compra electr' + NCHAR(243) + N'nicas',              N'mkTwhBatch');

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

-- Estado final por tipo de comprobante ---------------------------------------
SELECT r.adrRol AS rol_id, ro.rolName AS rol, r.adrAllowCode AS llave
FROM adrAllowRol r
LEFT JOIN roles ro ON ro.rolid = r.adrRol
WHERE r.adrActive = 1
  AND r.adrAllowCode IN (N'qusaleinv', N'mksaleinv', N'mksinBatch', N'auCanceInv',
                         N'qusalenc',  N'mksalenc',  N'mkncBatch',  N'auCanceNc',
                         N'qupurchtwh', N'mkpurchtwh', N'mkTwhBatch', N'auCanceTwh',
                         N'qupurchliq', N'mkpurchliq', N'mkliqBatch', N'auCanceLiq')
ORDER BY r.adrRol, r.adrAllowCode;

DROP TABLE #Asignar;

/* Rollback (solo si hiciera falta):
   DELETE FROM adrAllowRol WHERE adrAllowCode IN (N'auCanceInv',N'auCanceNc',N'auCanceLiq',N'mkncBatch',N'qupurchliq',N'mkpurchliq',N'mkliqBatch');
   DELETE FROM allowAction WHERE allowCode IN (N'auCanceInv',N'auCanceNc',N'auCanceLiq',N'mkncBatch',N'qupurchliq',N'mkpurchliq',N'mkliqBatch');
*/

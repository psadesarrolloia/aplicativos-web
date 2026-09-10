-- Da acceso al usuario web 'lparedes' a SANCEV, DGRV y Roller Dance en PeachEBills.
-- (rol 6 = "Hacer Comprobantes Electronicos": qu/mk saleinv, qu/mk salenc, qu/mk purchtwh)
-- Idempotente: se puede correr varias veces.
SET NOCOUNT ON;

DECLARE @rucs TABLE (RUC nvarchar(13), Nombre nvarchar(80));
INSERT INTO @rucs VALUES
    ('1791313747001', 'SANCEV'),
    ('1791033973001', 'DGRV'),
    ('1793198281001', 'ROLLER DANCE');

DECLARE @ruc nvarchar(13);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT RUC FROM @rucs;
OPEN cur;
FETCH NEXT FROM cur INTO @ruc;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM UserTransmitter WHERE [user] = 'lparedes' AND RUC = @ruc)
        INSERT INTO UserTransmitter ([user], RUC, [Order])
        VALUES ('lparedes', @ruc,
            (SELECT ISNULL(MAX([Order]), 0) + 1 FROM UserTransmitter WHERE [user] = 'lparedes'));

    IF NOT EXISTS (SELECT 1 FROM udrUserRolesTr WHERE udruser = 'lparedes' AND udrrol = 6 AND udrRucTransmitter = @ruc)
        INSERT INTO udrUserRolesTr (udruser, udrrol, udrRucTransmitter)
        VALUES ('lparedes', 6, @ruc);

    FETCH NEXT FROM cur INTO @ruc;
END
CLOSE cur;
DEALLOCATE cur;

-- Verificacion
SELECT ut.[user], ut.RUC, t.Name,
       (SELECT COUNT(*) FROM udrUserRolesTr r WHERE r.udruser = 'lparedes' AND r.udrRucTransmitter = ut.RUC) AS roles
FROM UserTransmitter ut
JOIN Transmitter t ON t.Ruc = ut.RUC
WHERE ut.[user] = 'lparedes'
  AND ut.RUC IN ('1791313747001', '1791033973001', '1793198281001')
ORDER BY t.Name;

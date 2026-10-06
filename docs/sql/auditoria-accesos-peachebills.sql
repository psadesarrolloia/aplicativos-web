/* ============================================================================
   Auditoría de accesos — TODOS los usuarios de PeachEBills (los del .exe). SOLO LECTURA.
   Base: PeachEBills. Sirve para sembrar la tabla de accesos web (usuario x empresa x módulo)
   con "los mismos permisos de hoy en el .exe".

   Uso:  sqlcmd -S localhost -d PeachEBills -E -C -i auditoria-accesos-peachebills.sql -o auditoria-peachebills.txt -s "|" -W -f 65001
         (-f 65001 = salida UTF-8, para que los acentos no salgan como «�»)
   No lee contraseñas: de la tabla [user] solo se listan los NOMBRES de columna (sección 1) y username/email.
   ============================================================================ */
SET NOCOUNT ON;

PRINT '== 1. Columnas de la tabla [user] (solo nombres, para saber si hay marca de activo) ==';
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'user'
ORDER BY ORDINAL_POSITION;

PRINT '== 2. Usuario x empresa x roles (todos los usuarios de PeachEBills) ==';
SELECT p.username, p.email, t.NameAlias AS empresa, ut.RUC,
       ISNULL(ts.IsActive, 0) AS empresa_activa,
       STUFF((SELECT ', ' + ro.rolName
              FROM dbo.udrUserRolesTr r JOIN dbo.roles ro ON ro.rolid = r.udrrol
              WHERE r.udruser = ut.[user] AND r.udrRucTransmitter = ut.RUC
              FOR XML PATH('')), 1, 2, '') AS roles
FROM dbo.[user] p
JOIN dbo.UserTransmitter ut ON ut.[user] = p.username
JOIN dbo.Transmitter t ON t.Ruc = ut.RUC
LEFT JOIN dbo.TransmitterStatus ts ON ts.TransmitterRuc = ut.RUC
ORDER BY p.username, t.NameAlias;

PRINT '== 3. Usuarios de PeachEBills SIN ninguna empresa ==';
SELECT p.username, p.email
FROM dbo.[user] p
WHERE NOT EXISTS (SELECT 1 FROM dbo.UserTransmitter ut WHERE ut.[user] = p.username)
ORDER BY p.username;

PRINT '== 4. Permisos de cada rol (todas las llaves, no solo las de la web) ==';
SELECT ro.rolid, ro.rolName, rp.adrAllowCode, a.allowName, rp.adrActive
FROM dbo.roles ro
JOIN dbo.adrAllowRol rp ON rp.adrRol = ro.rolid
LEFT JOIN dbo.allowAction a ON a.allowCode = rp.adrAllowCode
ORDER BY ro.rolName, rp.adrAllowCode;

PRINT '== 5. Catálogo completo de allowAction ==';
SELECT aid, allowCode, allowName
FROM dbo.allowAction
ORDER BY allowCode;

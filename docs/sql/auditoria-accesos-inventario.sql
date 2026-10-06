/* ============================================================================
   Auditoría de usuarios y accesos — INVENTARIO DE SOLO LECTURA (sin INSERT/UPDATE/DELETE)
   Bases: PeachEBills + PsaWebPlataforma (misma instancia de SERWEBPSA01)

   Uso:  sqlcmd -S localhost -d PeachEBills -E -C -i auditoria-accesos-inventario.sql -o auditoria-accesos.txt -s "|" -W
         (o pegar por bloques en SSMS). NO incluye contraseñas, hashes, tokens ni cadenas de conexión;
         solo se leen usuarios, empresas, roles y códigos de permiso.

   Resultados:
     1  Usuarios web (PsaWebPlataforma): activo, 2FA, bloqueo, último acceso, vínculo con PeachEBills
     2  Cuentas web sin usuario en PeachEBills (huérfanas) y viceversa
     3  Usuarios de PeachEBills con empresas pero sin rol en alguna de ellas (y roles sin empresa)
     4  Matriz usuario x empresa x rol
     5  Matriz usuario x empresa x permiso (solo códigos que usa la web)
     6  Roles y cuántos permisos web tienen (quién recibe cada llave)
     7  Llaves de la web: ¿existen en allowAction? ¿a cuántos roles están asignadas?
     8  Empresas: activas/inactivas y cuántos usuarios entran
     9  Tokens de la extensión de Chrome (sin secretos)
    10  Eventos de acceso de los últimos 30 días (resumen) y últimos accesos
   ============================================================================ */
SET NOCOUNT ON;

/* Códigos de permiso que la web conoce (Permisos.cs) */
DECLARE @Llaves TABLE (codigo nvarchar(20) PRIMARY KEY, modulo nvarchar(60), escribe bit);
INSERT INTO @Llaves VALUES
 (N'qusaleinv',  N'Facturas venta (ver)', 0), (N'mksaleinv', N'Facturas venta (emitir)', 0), (N'mksinBatch', N'Facturas venta (lote)', 0),
 (N'qusalenc',   N'Notas crédito (ver)', 0),  (N'mksalenc',  N'Notas crédito (emitir)', 0), (N'mkncBatch',  N'Notas crédito (lote)', 0),
 (N'qupurchliq', N'Liquidaciones compra (ver)', 0), (N'mkpurchliq', N'Liquidaciones compra (emitir)', 0), (N'mkliqBatch', N'Liquidaciones (lote)', 0),
 (N'auCanceInv', N'Autorizar anulación factura', 0), (N'auCanceNc', N'Autorizar anulación NC', 0), (N'auCanceLiq', N'Autorizar anulación liquidación', 0),
 (N'qupurchtwh', N'Retenciones (ver)', 0), (N'mkpurchtwh', N'Retenciones (emitir)', 0), (N'mkTwhBatch', N'Retenciones (lote / todas las empresas)', 0), (N'auCanceTwh', N'Autorizar anulación retención', 0),
 (N'qupurchinv', N'Compras (ver) [ESCRIBE EN SAGE]', 1), (N'mkpurchinv', N'Compras (registrar) [ESCRIBE EN SAGE]', 1),
 (N'quimpliq',   N'Liq. importaciones (ver) [ESCRIBE]', 1), (N'mkimpliq', N'Liq. importaciones (registrar) [ESCRIBE]', 1),
 (N'quSalesStk', N'Ventas: inventario y precios', 0), (N'quSalesQte', N'Ventas: ver prefacturas', 0), (N'mkSalesQte', N'Ventas: emitir prefactura', 0), (N'auSalesQte', N'Ventas: Contabilidad', 0),
 (N'quats',      N'ATS', 0), (N'quKardex', N'Kardex', 0),
 (N'quRptPwc',   N'Reporte PWC', 0), (N'quRptComis', N'Reporte Comisiones', 0), (N'quRptChq', N'Cheques', 0),
 (N'quconcsri',  N'Conciliación SRI', 0), (N'setDatilP', N'Config Datil', 0), (N'setODBC', N'Config ODBC', 0);

/* ---------------------------------------------------------------- 1 */
PRINT '== 1. Usuarios web (PsaWebPlataforma) ==';
SELECT u.UserName, u.PeachUsername, u.NombreCompleto, u.Email, u.Activo,
       u.TwoFactorEnabled AS dos_fa,
       CASE WHEN u.LockoutEnd > SYSDATETIMEOFFSET() THEN 'BLOQUEADO' ELSE '' END AS bloqueo,
       u.AccessFailedCount AS fallos, u.CreadoUtc,
       (SELECT MAX(e.Utc) FROM PsaWebPlataforma.dbo.EventosAuth e WHERE e.Usuario = u.UserName AND e.Tipo = 'login-ok') AS ultimo_login_ok,
       (SELECT COUNT(*) FROM PsaWebPlataforma.dbo.TokensExtension t WHERE t.UsuarioId = u.Id AND t.RevocadoUtc IS NULL) AS tokens_extension_activos
FROM PsaWebPlataforma.dbo.AspNetUsers u
ORDER BY u.UserName;

/* ---------------------------------------------------------------- 2 */
PRINT '== 2a. Cuentas web cuyo PeachUsername no existe en PeachEBills.user (huérfanas) ==';
SELECT u.UserName, u.PeachUsername, u.Activo
FROM PsaWebPlataforma.dbo.AspNetUsers u
WHERE NOT EXISTS (SELECT 1 FROM dbo.[user] p WHERE p.username = u.PeachUsername COLLATE DATABASE_DEFAULT);

PRINT '== 2b. Usuarios de PeachEBills sin cuenta web (informativo) ==';
SELECT p.username, p.email
FROM dbo.[user] p
WHERE NOT EXISTS (SELECT 1 FROM PsaWebPlataforma.dbo.AspNetUsers u WHERE u.PeachUsername COLLATE DATABASE_DEFAULT = p.username)
ORDER BY p.username;

/* ---------------------------------------------------------------- 3 */
PRINT '== 3a. Cuentas web con empresa pero SIN rol en esa empresa (entran y no ven nada / o ven módulos GateProvisional) ==';
SELECT u.UserName, ut.RUC, t.NameAlias
FROM PsaWebPlataforma.dbo.AspNetUsers u
JOIN dbo.UserTransmitter ut ON ut.[user] = u.PeachUsername COLLATE DATABASE_DEFAULT
JOIN dbo.Transmitter t ON t.Ruc = ut.RUC
WHERE NOT EXISTS (SELECT 1 FROM dbo.udrUserRolesTr r WHERE r.udruser = ut.[user] AND r.udrRucTransmitter = ut.RUC)
ORDER BY u.UserName, t.NameAlias;

PRINT '== 3b. Cuentas web SIN ninguna empresa ==';
SELECT u.UserName, u.PeachUsername, u.Activo
FROM PsaWebPlataforma.dbo.AspNetUsers u
WHERE NOT EXISTS (SELECT 1 FROM dbo.UserTransmitter ut WHERE ut.[user] = u.PeachUsername COLLATE DATABASE_DEFAULT);

PRINT '== 3c. Rol asignado en una empresa a la que el usuario NO tiene acceso (UserTransmitter) ==';
SELECT r.udruser, r.udrRucTransmitter, ro.rolName
FROM dbo.udrUserRolesTr r
JOIN dbo.roles ro ON ro.rolid = r.udrrol
WHERE NOT EXISTS (SELECT 1 FROM dbo.UserTransmitter ut WHERE ut.[user] = r.udruser AND ut.RUC = r.udrRucTransmitter);

/* ---------------------------------------------------------------- 4 */
PRINT '== 4. Matriz usuario x empresa x rol (solo cuentas web) ==';
SELECT u.UserName, u.Activo, u.TwoFactorEnabled AS dos_fa, t.Name AS empresa, ut.RUC,
       ISNULL(ts.IsActive, 0) AS empresa_activa,
       STUFF((SELECT ', ' + ro.rolName
              FROM dbo.udrUserRolesTr r JOIN dbo.roles ro ON ro.rolid = r.udrrol
              WHERE r.udruser = ut.[user] AND r.udrRucTransmitter = ut.RUC
              FOR XML PATH('')), 1, 2, '') AS roles
FROM PsaWebPlataforma.dbo.AspNetUsers u
JOIN dbo.UserTransmitter ut ON ut.[user] = u.PeachUsername COLLATE DATABASE_DEFAULT
JOIN dbo.Transmitter t ON t.Ruc = ut.RUC
LEFT JOIN dbo.TransmitterStatus ts ON ts.TransmitterRuc = ut.RUC
ORDER BY u.UserName, t.Name;

/* ---------------------------------------------------------------- 5 */
PRINT '== 5. Permisos efectivos web por usuario x empresa (códigos de la web; roles activos) ==';
SELECT u.UserName, t.Name AS empresa,
       STUFF((SELECT ', ' + x.codigo
              FROM (SELECT DISTINCT rp.adrAllowCode AS codigo
                    FROM dbo.udrUserRolesTr r
                    JOIN dbo.adrAllowRol rp ON rp.adrRol = r.udrrol AND rp.adrActive = 1
                    JOIN @Llaves k ON k.codigo = rp.adrAllowCode
                    WHERE r.udruser = ut.[user] AND r.udrRucTransmitter = ut.RUC) x
              ORDER BY x.codigo
              FOR XML PATH('')), 1, 2, '') AS permisos_web,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.udrUserRolesTr r JOIN dbo.adrAllowRol rp ON rp.adrRol = r.udrrol AND rp.adrActive = 1
                         JOIN @Llaves k ON k.codigo = rp.adrAllowCode AND k.escribe = 1
                         WHERE r.udruser = ut.[user] AND r.udrRucTransmitter = ut.RUC) THEN 'SI' ELSE '' END AS tiene_llave_de_escritura
FROM PsaWebPlataforma.dbo.AspNetUsers u
JOIN dbo.UserTransmitter ut ON ut.[user] = u.PeachUsername COLLATE DATABASE_DEFAULT
JOIN dbo.Transmitter t ON t.Ruc = ut.RUC
ORDER BY u.UserName, t.Name;

/* ---------------------------------------------------------------- 6 */
PRINT '== 6. Roles: cuántos permisos web tienen y a cuántos usuarios/empresas están asignados ==';
SELECT ro.rolid, ro.rolName,
       (SELECT COUNT(*) FROM dbo.adrAllowRol rp JOIN @Llaves k ON k.codigo = rp.adrAllowCode WHERE rp.adrRol = ro.rolid AND rp.adrActive = 1) AS llaves_web,
       (SELECT COUNT(*) FROM dbo.adrAllowRol rp WHERE rp.adrRol = ro.rolid AND rp.adrActive = 1) AS permisos_total,
       (SELECT COUNT(*) FROM dbo.udrUserRolesTr r WHERE r.udrrol = ro.rolid) AS asignaciones
FROM dbo.roles ro
ORDER BY ro.rolName;

/* ---------------------------------------------------------------- 7 */
PRINT '== 7. Llaves de la web: existencia y reparto (las que dicen 0 roles = GateProvisional abierto) ==';
SELECT k.codigo, k.modulo,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.allowAction a WHERE a.allowCode = k.codigo) THEN 'existe' ELSE 'NO EXISTE' END AS en_allowAction,
       (SELECT COUNT(DISTINCT rp.adrRol) FROM dbo.adrAllowRol rp WHERE rp.adrAllowCode = k.codigo AND rp.adrActive = 1) AS roles_con_la_llave
FROM @Llaves k
ORDER BY roles_con_la_llave, k.codigo;

/* ---------------------------------------------------------------- 8 */
PRINT '== 8. Empresas: estado y usuarios web con acceso ==';
SELECT t.Ruc, t.Name, t.NameAlias, ISNULL(ts.IsActive, 0) AS activa, t.ToSendToDatil,
       (SELECT COUNT(*) FROM dbo.UserTransmitter ut JOIN PsaWebPlataforma.dbo.AspNetUsers u ON u.PeachUsername COLLATE DATABASE_DEFAULT = ut.[user] WHERE ut.RUC = t.Ruc) AS usuarios_web
FROM dbo.Transmitter t
LEFT JOIN dbo.TransmitterStatus ts ON ts.TransmitterRuc = t.Ruc
ORDER BY t.Name;

/* ---------------------------------------------------------------- 9 */
PRINT '== 9. Tokens de la extensión de Chrome (sin secretos) ==';
SELECT u.UserName, t.Prefijo, t.CreadoUtc, t.UltimoUsoUtc, t.RevocadoUtc
FROM PsaWebPlataforma.dbo.TokensExtension t
JOIN PsaWebPlataforma.dbo.AspNetUsers u ON u.Id = t.UsuarioId
ORDER BY u.UserName, t.CreadoUtc DESC;

/* ---------------------------------------------------------------- 10 */
PRINT '== 10a. Eventos de auth, últimos 30 días, por tipo ==';
SELECT Tipo, COUNT(*) AS eventos, COUNT(DISTINCT Ip) AS ips_distintas
FROM PsaWebPlataforma.dbo.EventosAuth
WHERE Utc >= DATEADD(DAY, -30, SYSUTCDATETIME())
GROUP BY Tipo ORDER BY eventos DESC;

PRINT '== 10b. IPs con más fallos de login (30 días) ==';
SELECT TOP 20 Ip, COUNT(*) AS fallos, COUNT(DISTINCT Usuario) AS usuarios_probados
FROM PsaWebPlataforma.dbo.EventosAuth
WHERE Utc >= DATEADD(DAY, -30, SYSUTCDATETIME()) AND Tipo IN ('login-fail', '2fa-fail', 'lockout')
GROUP BY Ip ORDER BY fallos DESC;

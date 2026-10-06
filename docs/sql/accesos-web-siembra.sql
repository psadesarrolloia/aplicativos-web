/* ============================================================================
   AW-4 — Siembra de la tabla de accesos web (docs/PLAN-ACCESOS-WEB.md §7)
   Base: PsaWebPlataforma (lee PeachEBills de la MISMA instancia)   ·   Idempotente   ·   Vista previa por defecto

   Qué hace:
     - Crea las cuentas web personales que falten (SIN clave: cada persona la define desde la invitación por correo) y actualiza
       nombre / correo / perfil de las que ya existen (p. ej. lparedes).
     - Les asigna empresas, usuario de Sage por empresa y llaves según la plantilla de cada persona:
         TODO       -> todas las llaves (Super Admin y Admin)
         EXE        -> las llaves web que hoy le dan sus roles del .exe en PeachEBills (usuario de origen) + Conciliación SRI;
                       si en esa empresa el .exe no le da ninguna, la plantilla DIGITADOR
         SUPERVISOR -> lo mismo que EXE + autorizar anulaciones (auCance*) en TODAS sus empresas (nivel entre Digitador y Admin)
         DIGITADOR  -> plantilla Digitador/a (rol «Hacer Comprobantes Electrónicos» + Conciliación SRI)
         VENDEDOR   -> inventario + prefacturas propias + emitir
         NINGUNA    -> solo acceso a las empresas (perfil Consulta: los módulos se marcan a mano en el panel)
     - Solo AGREGA: no quita empresas ni llaves que alguien haya dado desde el panel.
     - Deshabilita la cuenta de prueba «pruebas1».
     - Deja un evento de auditoría por persona (usuario «siembra-AW4»).

   Los DATOS (personas, correos, empresas, usuarios de Sage) NO están en este archivo: los arma
   docs/sql/generar-datos-siembra.ps1 desde la matriz Usuarios\Matriz usuarios WebApps PSA.xlsx (carpeta fuera del repo) en
   un archivo accesos-web-datos.sql que se incluye con :r (desde la carpeta actual). Nunca lleva claves.

   Uso (en SERWEBPSA01, con el sitio YA desplegado con la migración AccesosWeb). -I es OBLIGATORIO (índice filtrado del correo):
     cd C:\Deploy        (accesos-web-datos.sql tiene que estar en la carpeta desde la que se corre: lo incluye «:r»)
     sqlcmd -S localhost -d PsaWebPlataforma -E -C -I -f 65001 -v Aplicar=0 -i accesos-web-siembra.sql
     sqlcmd -S localhost -d PsaWebPlataforma -E -C -I -f 65001 -v Aplicar=1 -i accesos-web-siembra.sql

   Las listas de llaves de abajo replican src/PsaWeb.Seguridad/LlavesWeb.cs (una prueba unitaria verifica que coincidan).
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Aplicar bit = CASE WHEN N'$(Aplicar)' = N'1' THEN 1 ELSE 0 END;
DECLARE @Quien nvarchar(50) = N'siembra-AW4';
PRINT CASE WHEN @Aplicar = 1 THEN N'== MODO APLICAR ==' ELSE N'== MODO VISTA PREVIA (no cambia nada; use -v Aplicar=1 para aplicar) ==' END;

IF DB_NAME() <> N'PsaWebPlataforma' THROW 50001, N'Correr sobre la base PsaWebPlataforma (-d PsaWebPlataforma).', 1;
IF OBJECT_ID(N'dbo.AccesosEmpresa') IS NULL OR COL_LENGTH(N'dbo.AspNetUsers', N'Perfil') IS NULL
    THROW 50002, N'Falta la migración AccesosWeb: desplegar primero el sitio (deploy-accesos-web.ps1) y dejarlo arrancar.', 1;

/* ---------------------------------------------------------------- datos */
CREATE TABLE #Personas (
    UsuarioWeb  nvarchar(50)  COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY,
    Nombre      nvarchar(120) COLLATE DATABASE_DEFAULT NOT NULL,
    Correo      nvarchar(256) COLLATE DATABASE_DEFAULT NOT NULL,
    Perfil      nvarchar(20)  COLLATE DATABASE_DEFAULT NOT NULL,   -- SuperAdmin | Admin | Supervisor | Digitador | Vendedor | Consulta
    Plantilla   nvarchar(20)  COLLATE DATABASE_DEFAULT NOT NULL,   -- TODO | EXE | SUPERVISOR | DIGITADOR | VENDEDOR | NINGUNA
    PeachOrigen nvarchar(50)  COLLATE DATABASE_DEFAULT NULL);      -- usuario del .exe en PeachEBills (para EXE)
CREATE TABLE #Empresas (
    UsuarioWeb  nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
    Ruc         nvarchar(13) COLLATE DATABASE_DEFAULT NOT NULL,
    UsuarioSage nvarchar(50) COLLATE DATABASE_DEFAULT NULL,
    PRIMARY KEY (UsuarioWeb, Ruc));

:r accesos-web-datos.sql

/* ---------------------------------------------------------------- llaves (= LlavesWeb.cs) */
CREATE TABLE #Llaves (Llave nvarchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, Digitador bit NOT NULL, Vendedor bit NOT NULL);
INSERT INTO #Llaves (Llave, Digitador, Vendedor) VALUES
 (N'quCierre', 0, 0), (N'quRptPwc', 1, 0), (N'quRptComis', 1, 0),
 (N'qupurchinv', 0, 0), (N'mkpurchinv', 0, 0), (N'quimpliq', 0, 0), (N'mkimpliq', 0, 0),
 (N'quRptChq', 1, 0), (N'quats', 0, 0), (N'quconcsri', 1, 0),
 (N'qusaleinv', 1, 0), (N'mksaleinv', 1, 0), (N'mksinBatch', 0, 0), (N'auCanceInv', 0, 0),
 (N'qupurchtwh', 1, 0), (N'mkpurchtwh', 1, 0), (N'mkTwhBatch', 0, 0), (N'auCanceTwh', 0, 0),
 (N'qupurchliq', 1, 0), (N'mkpurchliq', 1, 0), (N'mkliqBatch', 0, 0), (N'auCanceLiq', 0, 0),
 (N'qusalenc', 1, 0), (N'mksalenc', 1, 0), (N'mkncBatch', 0, 0), (N'auCanceNc', 0, 0),
 (N'quSalesStk', 0, 1), (N'quSalesQte', 0, 1), (N'mkSalesQte', 0, 1), (N'auSalesQte', 0, 0),
 (N'quKardex', 0, 0);

/* ---------------------------------------------------------------- validaciones (abortan antes de tocar nada) */
DECLARE @Errores TABLE (Error nvarchar(400));
INSERT INTO @Errores SELECT N'Perfil inválido para ' + UsuarioWeb + N': ' + Perfil FROM #Personas WHERE Perfil NOT IN (N'SuperAdmin', N'Admin', N'Supervisor', N'Digitador', N'Vendedor', N'Consulta');
INSERT INTO @Errores SELECT N'Plantilla inválida para ' + UsuarioWeb + N': ' + Plantilla FROM #Personas WHERE Plantilla NOT IN (N'TODO', N'EXE', N'SUPERVISOR', N'DIGITADOR', N'VENDEDOR', N'NINGUNA');
INSERT INTO @Errores SELECT N'Correo inválido para ' + UsuarioWeb + N': ' + Correo FROM #Personas WHERE Correo NOT LIKE N'_%@_%._%' OR Correo LIKE N'% %';
INSERT INTO @Errores SELECT N'Correo repetido en los datos: ' + Correo FROM #Personas GROUP BY Correo HAVING COUNT(*) > 1;
INSERT INTO @Errores
    SELECT N'El correo ' + p.Correo + N' ya lo usa la cuenta web ' + u.UserName
    FROM #Personas p JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(p.Correo) AND u.NormalizedUserName <> UPPER(p.UsuarioWeb);
INSERT INTO @Errores SELECT N'Sin empresas: ' + p.UsuarioWeb FROM #Personas p WHERE NOT EXISTS (SELECT 1 FROM #Empresas e WHERE e.UsuarioWeb = p.UsuarioWeb);
INSERT INTO @Errores SELECT N'Empresa de una persona que no está en #Personas: ' + e.UsuarioWeb FROM #Empresas e WHERE NOT EXISTS (SELECT 1 FROM #Personas p WHERE p.UsuarioWeb = e.UsuarioWeb);
INSERT INTO @Errores
    SELECT N'RUC inexistente en PeachEBills: ' + e.Ruc + N' (' + e.UsuarioWeb + N')'
    FROM #Empresas e WHERE NOT EXISTS (SELECT 1 FROM PeachEBills.dbo.Transmitter t WHERE t.Ruc COLLATE DATABASE_DEFAULT = e.Ruc);
INSERT INTO @Errores
    SELECT N'Usuario de origen inexistente en PeachEBills: ' + p.PeachOrigen + N' (' + p.UsuarioWeb + N')'
    FROM #Personas p
    WHERE p.PeachOrigen IS NOT NULL AND NOT EXISTS (SELECT 1 FROM PeachEBills.dbo.[user] x WHERE x.username COLLATE DATABASE_DEFAULT = p.PeachOrigen);
IF EXISTS (SELECT 1 FROM @Errores)
BEGIN
    SELECT Error FROM @Errores;
    THROW 50003, N'Los datos tienen errores (ver la lista de arriba). No se aplicó nada.', 1;
END

/* ---------------------------------------------------------------- plan: persona x empresa x llave */
-- Llaves que el .exe le da hoy al usuario de origen en cada empresa (roles activos), limitadas a las que la web conoce.
SELECT DISTINCT e.UsuarioWeb, e.Ruc, k.Llave
INTO #DelExe
FROM #Empresas e
JOIN #Personas p ON p.UsuarioWeb = e.UsuarioWeb AND p.Plantilla IN (N'EXE', N'SUPERVISOR') AND p.PeachOrigen IS NOT NULL
JOIN PeachEBills.dbo.udrUserRolesTr r ON r.udruser COLLATE DATABASE_DEFAULT = p.PeachOrigen AND r.udrRucTransmitter COLLATE DATABASE_DEFAULT = e.Ruc
JOIN PeachEBills.dbo.adrAllowRol a ON a.adrRol = r.udrrol AND a.adrActive = 1
JOIN #Llaves k ON k.Llave = a.adrAllowCode COLLATE DATABASE_DEFAULT;

SELECT e.UsuarioWeb, e.Ruc, k.Llave, CAST(N'plantilla' AS nvarchar(20)) AS Origen
INTO #Plan
FROM #Empresas e
JOIN #Personas p ON p.UsuarioWeb = e.UsuarioWeb
JOIN #Llaves k ON p.Plantilla = N'TODO'
              OR (p.Plantilla = N'VENDEDOR' AND k.Vendedor = 1)
              OR (p.Plantilla = N'DIGITADOR' AND k.Digitador = 1)
WHERE p.Plantilla IN (N'TODO', N'VENDEDOR', N'DIGITADOR');

INSERT INTO #Plan (UsuarioWeb, Ruc, Llave, Origen)
SELECT d.UsuarioWeb, d.Ruc, d.Llave, N'.exe' FROM #DelExe d;
INSERT INTO #Plan (UsuarioWeb, Ruc, Llave, Origen)   -- + Conciliación SRI donde el .exe da algo
SELECT DISTINCT d.UsuarioWeb, d.Ruc, N'quconcsri', N'.exe + conciliación' FROM #DelExe d
WHERE NOT EXISTS (SELECT 1 FROM #DelExe x WHERE x.UsuarioWeb = d.UsuarioWeb AND x.Ruc = d.Ruc AND x.Llave = N'quconcsri');
INSERT INTO #Plan (UsuarioWeb, Ruc, Llave, Origen)   -- EXE / SUPERVISOR sin nada del .exe en esa empresa: plantilla Digitador/a
SELECT e.UsuarioWeb, e.Ruc, k.Llave, N'sin .exe: digitador'
FROM #Empresas e
JOIN #Personas p ON p.UsuarioWeb = e.UsuarioWeb AND p.Plantilla IN (N'EXE', N'SUPERVISOR')
JOIN #Llaves k ON k.Digitador = 1
WHERE NOT EXISTS (SELECT 1 FROM #DelExe d WHERE d.UsuarioWeb = e.UsuarioWeb AND d.Ruc = e.Ruc);
INSERT INTO #Plan (UsuarioWeb, Ruc, Llave, Origen)   -- SUPERVISOR: autorizar anulaciones en todas sus empresas
SELECT e.UsuarioWeb, e.Ruc, k.Llave, N'supervisor'
FROM #Empresas e
JOIN #Personas p ON p.UsuarioWeb = e.UsuarioWeb AND p.Plantilla = N'SUPERVISOR'
JOIN #Llaves k ON k.Llave LIKE N'auCance%';

/* ---------------------------------------------------------------- vista previa */
PRINT N'== 1. Cuentas ==';
SELECT p.UsuarioWeb, p.Nombre, p.Correo, p.Perfil, p.Plantilla, p.PeachOrigen,
       CASE WHEN u.Id IS NULL THEN N'NUEVA (sin clave; invitación por correo)' ELSE N'existe: se actualiza nombre/correo/perfil' END AS Accion,
       u.Email AS CorreoActual, u.Perfil AS PerfilActual
FROM #Personas p
LEFT JOIN dbo.AspNetUsers u ON u.NormalizedUserName = UPPER(p.UsuarioWeb)
ORDER BY CASE p.Perfil WHEN N'SuperAdmin' THEN 0 WHEN N'Admin' THEN 1 ELSE 2 END, p.UsuarioWeb;

PRINT N'== 2. Empresas y llaves por cuenta ==';
SELECT e.UsuarioWeb, ISNULL(t.NameAlias, t.Name) AS Empresa, e.Ruc, e.UsuarioSage,
       (SELECT STRING_AGG(x.Llave, N' ') WITHIN GROUP (ORDER BY x.Llave) FROM (SELECT DISTINCT Llave FROM #Plan pl WHERE pl.UsuarioWeb = e.UsuarioWeb AND pl.Ruc = e.Ruc) x) AS Llaves,
       (SELECT STRING_AGG(o.Origen, N', ') FROM (SELECT DISTINCT Origen FROM #Plan pl WHERE pl.UsuarioWeb = e.UsuarioWeb AND pl.Ruc = e.Ruc) o) AS Origen
FROM #Empresas e
LEFT JOIN PeachEBills.dbo.Transmitter t ON t.Ruc COLLATE DATABASE_DEFAULT = e.Ruc
ORDER BY e.UsuarioWeb, Empresa;

PRINT N'== 3. Avisos (no bloquean) ==';
SELECT e.UsuarioWeb, e.Ruc, N'sin usuario de Sage: no podrá registrar en Sage en esta empresa' AS Aviso FROM #Empresas e WHERE e.UsuarioSage IS NULL
UNION ALL
SELECT DISTINCT pl.UsuarioWeb, pl.Ruc, N'conserva autorizar anulaciones que hoy le da el .exe en esta empresa' FROM #Plan pl
JOIN #Personas p ON p.UsuarioWeb = pl.UsuarioWeb AND p.Plantilla = N'EXE' WHERE pl.Llave LIKE N'auCance%'
UNION ALL
SELECT DISTINCT pl.UsuarioWeb, pl.Ruc, N'el .exe no le da nada acá: se usa la plantilla Digitador/a' FROM #Plan pl WHERE pl.Origen = N'sin .exe: digitador'
UNION ALL
SELECT UserName, N'', N'cuenta de prueba: se DESHABILITA' FROM dbo.AspNetUsers WHERE NormalizedUserName = N'PRUEBAS1' AND Activo = 1
ORDER BY 1, 2;

/* ---------------------------------------------------------------- aplicar */
IF @Aplicar = 1
BEGIN
    BEGIN TRAN;

    DECLARE @Tocados TABLE (UsuarioId nvarchar(450));   -- cuentas con algún cambio real en esta corrida (para la auditoría)

    -- Cuentas nuevas: sin clave, correo sin confirmar (se confirma al definir la clave desde la invitación).
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
        ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount,
        NombreCompleto, Activo, PeachUsername, CreadoUtc, Perfil, MetodoSegundoFactor, UltimoAccesoUtc)
    OUTPUT inserted.Id INTO @Tocados
    SELECT LOWER(CONVERT(nvarchar(36), NEWID())), p.UsuarioWeb, UPPER(p.UsuarioWeb), p.Correo, UPPER(p.Correo), 0, NULL,
        UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')), LOWER(CONVERT(nvarchar(36), NEWID())), NULL, 0, 0, NULL, 1, 0,
        p.Nombre, 1, ISNULL(p.PeachOrigen, p.UsuarioWeb), SYSUTCDATETIME(), p.Perfil, NULL, NULL
    FROM #Personas p
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u WHERE u.NormalizedUserName = UPPER(p.UsuarioWeb));

    -- Cuentas existentes con diferencias: nombre, correo (si cambia queda sin confirmar) y perfil; nuevo sello = se re-validan sus sesiones.
    UPDATE u SET
        NombreCompleto = p.Nombre,
        EmailConfirmed = CASE WHEN u.NormalizedEmail = UPPER(p.Correo) THEN u.EmailConfirmed ELSE 0 END,
        Email = p.Correo, NormalizedEmail = UPPER(p.Correo),
        Perfil = p.Perfil,
        SecurityStamp = CASE WHEN u.Perfil <> p.Perfil OR ISNULL(u.NormalizedEmail, N'') <> UPPER(p.Correo)
                             THEN UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'')) ELSE u.SecurityStamp END,
        ConcurrencyStamp = LOWER(CONVERT(nvarchar(36), NEWID()))
    OUTPUT inserted.Id INTO @Tocados
    FROM dbo.AspNetUsers u JOIN #Personas p ON u.NormalizedUserName = UPPER(p.UsuarioWeb)
    WHERE ISNULL(u.NombreCompleto, N'') <> p.Nombre OR ISNULL(u.NormalizedEmail, N'') <> UPPER(p.Correo) OR u.Perfil <> p.Perfil;

    -- Empresas: crea las que faltan; en las existentes activa y completa el usuario de Sage si no tenía.
    INSERT INTO dbo.AccesosEmpresa (UsuarioId, Ruc, Activo, UsuarioSage, ModificadoPor, ModificadoUtc)
    OUTPUT inserted.UsuarioId INTO @Tocados
    SELECT u.Id, e.Ruc, 1, e.UsuarioSage, @Quien, SYSUTCDATETIME()
    FROM #Empresas e JOIN dbo.AspNetUsers u ON u.NormalizedUserName = UPPER(e.UsuarioWeb)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AccesosEmpresa a WHERE a.UsuarioId = u.Id AND a.Ruc = e.Ruc);

    UPDATE a SET Activo = 1, UsuarioSage = ISNULL(a.UsuarioSage, e.UsuarioSage), ModificadoPor = @Quien, ModificadoUtc = SYSUTCDATETIME()
    OUTPUT inserted.UsuarioId INTO @Tocados
    FROM dbo.AccesosEmpresa a
    JOIN dbo.AspNetUsers u ON u.Id = a.UsuarioId
    JOIN #Empresas e ON u.NormalizedUserName = UPPER(e.UsuarioWeb) AND e.Ruc = a.Ruc
    WHERE a.Activo = 0 OR (a.UsuarioSage IS NULL AND e.UsuarioSage IS NOT NULL);

    -- Llaves: solo agrega.
    INSERT INTO dbo.AccesosLlave (UsuarioId, Ruc, Llave, OtorgadoPor, OtorgadoUtc)
    OUTPUT inserted.UsuarioId INTO @Tocados
    SELECT DISTINCT u.Id, pl.Ruc, pl.Llave, @Quien, SYSUTCDATETIME()
    FROM #Plan pl JOIN dbo.AspNetUsers u ON u.NormalizedUserName = UPPER(pl.UsuarioWeb)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.AccesosLlave l WHERE l.UsuarioId = u.Id AND l.Ruc = pl.Ruc AND l.Llave = pl.Llave);

    -- Cuenta de prueba.
    UPDATE dbo.AspNetUsers SET Activo = 0, SecurityStamp = UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''))
    OUTPUT inserted.Id INTO @Tocados
    WHERE NormalizedUserName = N'PRUEBAS1' AND Activo = 1;
    IF @@ROWCOUNT > 0
        INSERT INTO dbo.EventosAuth (Utc, Usuario, Tipo, Ip, Detalle) VALUES (SYSUTCDATETIME(), @Quien, N'admin-deshabilitado', NULL, N'pruebas1');

    -- Auditoría: una fila por persona con algún cambio en esta corrida (re-correr sin cambios no deja ruido).
    INSERT INTO dbo.EventosAuth (Utc, Usuario, Tipo, Ip, Detalle)
    SELECT SYSUTCDATETIME(), @Quien, N'acceso-otorgado', NULL,
           LEFT(p.UsuarioWeb + N' (' + p.Perfil + N', ' + p.Plantilla + N'): '
                + CAST((SELECT COUNT(*) FROM #Empresas e WHERE e.UsuarioWeb = p.UsuarioWeb) AS nvarchar(10)) + N' empresas, '
                + CAST((SELECT COUNT(DISTINCT pl.Ruc + pl.Llave) FROM #Plan pl WHERE pl.UsuarioWeb = p.UsuarioWeb) AS nvarchar(10)) + N' llaves', 400)
    FROM #Personas p
    JOIN dbo.AspNetUsers u ON u.NormalizedUserName = UPPER(p.UsuarioWeb)
    WHERE u.Id IN (SELECT UsuarioId FROM @Tocados);

    COMMIT;
    PRINT N'== Aplicado. ==';
END

/* ---------------------------------------------------------------- verificación (estado real en la base) */
PRINT N'== 4. Estado en la base (cuentas sembradas) ==';
SELECT u.UserName, u.Email, u.Perfil, u.Activo, CASE WHEN u.PasswordHash IS NULL THEN N'sin clave (invitar)' ELSE N'con clave' END AS Clave,
       (SELECT COUNT(*) FROM dbo.AccesosEmpresa a WHERE a.UsuarioId = u.Id AND a.Activo = 1) AS Empresas,
       (SELECT COUNT(*) FROM dbo.AccesosLlave l JOIN dbo.AccesosEmpresa a ON a.UsuarioId = l.UsuarioId AND a.Ruc = l.Ruc AND a.Activo = 1
         WHERE l.UsuarioId = u.Id) AS Llaves
FROM dbo.AspNetUsers u
WHERE u.NormalizedUserName IN (SELECT UPPER(UsuarioWeb) FROM #Personas) OR u.NormalizedUserName = N'PRUEBAS1'
ORDER BY u.UserName;

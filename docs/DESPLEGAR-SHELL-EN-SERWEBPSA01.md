# Desplegar el shell + Retenciones en `SERWEBPSA01`

Estado: la plataforma web (shell de login local + 2FA, selector de empresa,
dashboard) con **Cierre de Caja** y **Retenciones** re-cableados a la empresa de
sesión. Reemplaza al sitio standalone del piloto (Windows Auth): **un solo sitio**.

> El runbook viejo `DESPLEGAR-EN-PSACONTABILIDAD2.md` describe el modelo
> *standalone* (Negotiate / Windows Auth, sin `Plataforma:ConnectionString`).
> Sigue sirviendo de referencia para IIS + ODBC Pervasive; acá cambia la
> autenticación y se agrega la BD `PsaWebPlataforma`.

`SERWEBPSA01` = `192.168.0.11`: corre el IIS del piloto (`:8088`) y el SQL Server
de producción `PeachEBills` (instancia por defecto). Entrar como `Administrator`
local. PREDATOR no tiene ruta a `192.168.0.x`: **todo esto se hace en el server**.

---

## 0. Qué cambia respecto del piloto standalone

| | Piloto standalone (hoy) | Shell (este deploy) |
|---|---|---|
| Autenticación | Windows Auth de IIS (SSO AD) | **Cookie de ASP.NET Core Identity** (login local + 2FA TOTP) |
| IIS · Windows Auth | Habilitada | **Deshabilitada** |
| IIS · Autenticación anónima | Deshabilitada | **Habilitada** (la app maneja la sesión) |
| BD extra | — | **`PsaWebPlataforma`** en el SQL local |
| Entrada | va directo a `/cierre-de-caja` | `/bienvenida` → `/ingresar` (+2FA) → `/seleccionar-empresa` → `/` |
| Rate limiter | — | activo (login: 10/IP/5 min) |

Lo que **no** cambia: publicación self-contained `win-x86`, `hostingModel="inprocess"`,
app pool en 32 bits (ODBC Pervasive), carpeta física `C:\inetpub\CierreDeCaja`,
puerto `8088`.

---

## 1. Paquete de publicación (en PREDATOR)

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish
Compress-Archive -Path publish\* -DestinationPath PsaWeb.Host-publish.zip -Force
```

Copiar `PsaWeb.Host-publish.zip` al server (USB / recurso compartido).
En `SERWEBPSA01`, con el sitio detenido, extraer sobre `C:\inetpub\CierreDeCaja`
(reemplazando todo). Conservar/crear la subcarpeta `logs\`.

---

## 2. Base de datos `PsaWebPlataforma`

Identity (usuarios, hashes, 2FA, tokens) y la auditoría de login viven en una BD
**dedicada**, separada de `PeachEBills`.

1. Crear la BD vacía en la instancia por defecto:

   ```sql
   CREATE DATABASE [PsaWebPlataforma];
   ALTER DATABASE [PsaWebPlataforma] SET COMPATIBILITY_LEVEL = 150;
   ```

2. El **esquema se crea solo** en el primer arranque de la app
   (`seeder.MigrarAsync()` corre siempre que haya `Plataforma:ConnectionString`,
   salvo que se ponga `Plataforma__MigrarAlArrancar=false`). No hace falta
   `dotnet ef` en el server.

3. Login para el app pool (ver §4): con `Trusted_Connection`, el login SQL es la
   identidad del pool (`IIS APPPOOL\CierreDeCaja` o la cuenta de servicio).
   Necesita **`db_owner` en `PsaWebPlataforma`** (crea tablas en la 1ª corrida y
   luego lee/escribe); alternativa mínima: `db_ddladmin` + `db_datareader` +
   `db_datawriter`.

   ```sql
   USE [PsaWebPlataforma];
   CREATE USER [IIS APPPOOL\CierreDeCaja] FOR LOGIN [IIS APPPOOL\CierreDeCaja];
   ALTER ROLE db_owner ADD MEMBER [IIS APPPOOL\CierreDeCaja];
   ```

---

## 3. `PeachEBills` en el server (para Retenciones)

- Subir el nivel de compatibilidad (evita `OPENJSON` en LINQ):

  ```sql
  ALTER DATABASE [PeachEBills] SET COMPATIBILITY_LEVEL = 150;
  ```

- El app pool necesita en `PeachEBills`: `db_datareader` **y `db_datawriter`**
  (el alta de retención escribe `TaxWithHoldings` + `THDetails` + `DatilRequests`).

  ```sql
  USE [PeachEBills];
  CREATE USER [IIS APPPOOL\CierreDeCaja] FOR LOGIN [IIS APPPOOL\CierreDeCaja];
  ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\CierreDeCaja];
  ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\CierreDeCaja];
  ```

---

## 4. App pool e IIS

El pool `CierreDeCaja` ya existe. Verificar / ajustar:

- **.NET CLR: Sin código administrado.**
- **Habilitar aplicaciones de 32 bits = True** (ODBC Pervasive).
- Identidad: `ApplicationPoolIdentity` si esa cuenta ya tiene NTFS sobre la
  carpeta de Sage 50 y los logins SQL de §2/§3; si no, la cuenta de servicio de
  dominio que ya usa el piloto.
- `Start Mode = AlwaysRunning` e `Idle Time-out = 0` (ya aplicado en el piloto).

Autenticación del **sitio** (`CierreDeCaja` → *Authentication*) — **invertir** lo del piloto:

- **Autenticación anónima: Habilitada.**
- **Autenticación de Windows: Deshabilitada.**

(Con `Plataforma:ConnectionString` presente la app usa cookie de Identity; si
Windows Auth de IIS queda habilitada, `w3wp` intercepta y rompe el login local.)

```powershell
Import-Module WebAdministration
Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/anonymousAuthentication `
  -Name enabled -Value true -PSPath 'IIS:\' -Location 'CierreDeCaja'
Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/windowsAuthentication `
  -Name enabled -Value false -PSPath 'IIS:\' -Location 'CierreDeCaja'
```

---

## 5. Variables de entorno del sitio

IIS → sitio `CierreDeCaja` → *Configuration Editor* →
`system.webServer/aspNetCore/environmentVariables`, **o** editando
`<aspNetCore><environmentVariables>` en `web.config` (`__` = separador de sección):

```xml
<aspNetCore processPath=".\PsaWeb.Host.exe" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>

    <!-- Fuerza Production (no Development: sin seed de usuario de prueba). -->
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />

    <!-- Identidad de la plataforma: prende el shell (cookie + login local + 2FA). -->
    <environmentVariable name="Plataforma__ConnectionString"
      value="Server=localhost;Database=PsaWebPlataforma;Trusted_Connection=True;TrustServerCertificate=True" />

    <!-- Quién es admin (índice por variable). Debe coincidir con AdminInicial:Usuario. -->
    <environmentVariable name="Plataforma__Admins__0" value="lparedes" />

    <!-- SOLO para el 1er arranque: crea el admin si la BD no tiene ningún usuario.
         Quitar estas dos variables después de entrar y cambiar la clave. -->
    <environmentVariable name="Plataforma__AdminInicial__Usuario" value="lparedes" />
    <environmentVariable name="Plataforma__AdminInicial__Clave" value="<clave-fuerte-12+>" />

    <!-- Sage 50 del piloto (Cierre de Caja standalone / fallback sin empresa de sesión). -->
    <environmentVariable name="Sage50__ConnectionString"
      value="Driver={Pervasive ODBC Client Interface};ServerName=localhost;DBQ=ROLLERDANCEE202526;UID=Peachtree;PWD=<clave>;" />

    <!-- Retenciones: se activa por tener PeachEbills configurado. -->
    <environmentVariable name="PeachEbills__ConnectionString"
      value="Server=localhost;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True" />
    <!-- Emisión: arranca en DryRun (arma y valida, NO envía a Datil ni guarda). -->
    <environmentVariable name="Datil__DryRun" value="true" />
    <!-- Worker en segundo plano: apagado hasta validar DryRun=false a mano. -->
    <environmentVariable name="Retenciones__Worker__Habilitado" value="false" />
    <environmentVariable name="Retenciones__Worker__Intervalo" value="01:00:00" />
    <environmentVariable name="Retenciones__Worker__RetrasoInicial" value="00:02:00" />

  </environmentVariables>
</aspNetCore>
```

Clave del admin inicial: cumple la política (12+ caracteres, mayúscula, minúscula,
dígito, símbolo, 4 caracteres únicos). Va **solo** en la variable de entorno del
sitio, nunca en el repo. Tras el primer login: entrar a `/mi-cuenta/seguridad`,
activar 2FA, y **quitar `Plataforma__AdminInicial__*`**.

---

## 6. Primer arranque y verificación

```powershell
Restart-WebAppPool CierreDeCaja
```

Para ver el arranque: `stdoutLogEnabled="true"` en `web.config`, reproducir,
volver a `false`. Log esperado:

```
PsaWebPlataforma: migraciones aplicadas.
Admin inicial lparedes creado. Verificá que esté en Plataforma:Admins y quitá Plataforma:AdminInicial:* del entorno.
Plataforma (identidad local): ACTIVA.
Retenciones: módulo ACTIVO (PeachEBills configurado).
Cierre de Caja: repositorio ODBC / Sage 50.
Worker de retenciones DESHABILITADO (Retenciones:Worker:Habilitado=false).
```

Prueba de humo (desde un PC de la oficina, `http://192.168.0.11:8088/`):

1. Redirige a `/bienvenida` → botón **Ingresar** → `/ingresar`.
2. Login `lparedes` + la clave inicial. (Aún sin 2FA: entra directo.)
3. `/seleccionar-empresa`: aparecen las empresas de `lparedes`. Elegir una +
   ambiente **Pruebas**. → dashboard con las apps habilitadas.
4. **Cierre de Caja**: consultar un rango con datos → totales OK. Exportar a Excel.
5. **Retenciones**: la card "Mi empresa" (o "Ver todas" si el usuario tiene
   `mkTwhBatch`), banner **DRY-RUN activo**, "Retenciones guardadas" con la
   columna PDF; clic en una fila abre el popup de detalle.
6. `/mi-cuenta/seguridad` → activar 2FA (escanear QR, confirmar código, guardar
   los 10 códigos de recuperación).
7. `/admin/usuarios` y `/admin/auditoria` deben cargar (gate de admin).
8. Quitar `Plataforma__AdminInicial__*`, `Restart-WebAppPool`, y volver a entrar
   (ahora pide 2FA).

---

## 7. Puesta en marcha de Retenciones (emisión real)

**No** tocar hasta que 1–8 de arriba estén OK.

1. `Datil__DryRun=true`, worker apagado: en `/retenciones` revisar el panorama y
   correr **"Ejecutar"** una vez. Todo debe quedar en "armadas OK", 0 guardadas.
2. Cargar las credenciales reales de Datil (vienen de la tabla `DatilAPI` de
   `PeachEBills` — verificar que estén las de producción, no las de pruebas).
3. `Datil__DryRun=false`, `Restart-WebAppPool`. Correr **"Ejecutar (mi empresa)"**
   para **una** empresa de prueba con pocas pendientes. Verificar en el portal de
   Datil que la retención se emitió y que quedó en `TaxWithHoldings` +
   `DatilRequests`. **Esto autoriza comprobantes ante el SRI: es irreversible.**
4. Si todo cuadra: `Retenciones__Worker__Habilitado=true` para emisión automática
   cross-company cada hora. El botón y el worker comparten candado de un cupo.

---

## 8. Pendiente tras el deploy

- **Binding HTTPS** con certificado (la cookie de auth es `SecurePolicy.SameAsRequest`:
  sobre HTTP no viaja como `Secure`). Agregar binding 443/8443 + `UseHttpsRedirection`
  ya deja de tirar el warning "Failed to determine https port".
- Registro DNS interno `apps.paredes.com.ec` / `cierredecaja.paredes.com.ec` →
  `192.168.0.11`.
- Backup de `PsaWebPlataforma` en el plan de backups del server.
- Canal público (VPN / RD Gateway / proxy pre-auth) = decisión F6 para las 26 apps;
  hasta entonces, acceso remoto por el RDS existente (Opción A).

---

## Problemas típicos (además de los del runbook viejo)

| Síntoma | Causa | Solución |
|---|---|---|
| El login local nunca aparece / 401 de Windows | Windows Auth de IIS sigue habilitada en el sitio | §4: anónima ON, Windows OFF, `Restart-WebAppPool` |
| 500.30 al arrancar, log habla de SQL / `PsaWebPlataforma` | El app pool no puede crear/abrir la BD | §2: BD creada + login con `db_owner` |
| `login-fail` siempre, aun con la clave correcta | Se reinició el pool y se recreó otro admin, o la clave no cumple política | Ver el log del 1er arranque; si dice "ya hay usuarios: no se creó nada", usar la clave original |
| Entra pero `/seleccionar-empresa` sale vacío | El `PeachUsername` del admin no matchea `user.username` en `PeachEBills` | Crear el admin con `Usuario` = el `username` real de PeachEBills (p. ej. `lparedes`) |
| `/retenciones` dice "Módulo no configurado" | Falta `PeachEbills__ConnectionString` | §5 |
| Retenciones: todas las empresas "con error" | Sage 50 multi-empresa inalcanzable / `PeachConnString` mal | Verificar `PeachConnString` en `PeachEBills` y que el server vea las bases Zen |

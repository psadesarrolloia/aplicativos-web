# F5 — Promover el piloto a `psacontabilidad2`

Pasar la app del entorno de desarrollo (PREDATOR) al host de sesión RDS
`psacontabilidad2`, que está unido al dominio. Ahí se activa el Windows Auth
real contra Active Directory y se valida el driver ODBC bajo IIS.

## Qué hace falta en `psacontabilidad2`

- **IIS** con el rol de servidor web.
- El **ASP.NET Core Module v2** (viene con el *ASP.NET Core 9 Hosting Bundle*,
  `dotnet-hosting-9.0.x-win.exe` de Microsoft). Reiniciar IIS después de instalarlo
  (`iisreset`).
- **No hace falta instalar ningún runtime de .NET**: el paquete de publicación es
  *self-contained* y trae su propio runtime x86.
- El driver **Pervasive ODBC Client Interface** (ya está: la máquina ejecuta los `.exe`).
- Ruta de red a la base de Sage 50 (ya la tiene).

## 1. Paquete de publicación (ya generado en PREDATOR)

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish
```

Resultado: carpeta `publish\` (~105 MB) y `PsaWeb.Host-publish.zip` (~46 MB) en
la raíz del repo (ambos ignorados por git). `-r win-x86` + self-contained porque
el driver Pervasive es de 32 bits y así el target no depende de runtimes instalados.

Copiar el contenido del zip a `psacontabilidad2`, por ejemplo a
`C:\inetpub\apps\cierre-de-caja`.

## 2. Application pool en IIS

Nuevo pool, p. ej. `PsaWebCierreDeCaja`:

- **.NET CLR version: Sin código administrado** (el módulo ASP.NET Core hostea el proceso).
- **Habilitar aplicaciones de 32 bits = True**  ← imprescindible: el `web.config`
  usa `hostingModel="inprocess"`, así que el proceso de IIS (`w3wp.exe`) tiene que
  ser de 32 bits para que cargue el ODBC de Pervasive.
- Identidad: una cuenta de dominio de servicio con lectura sobre la carpeta de
  Sage 50 (o `ApplicationPoolIdentity` si esa cuenta ya tiene acceso).

## 3. Sitio / aplicación

- Carpeta física: seguir la convención del server (`C:\inetpub\<NombreApp>`,
  p. ej. `C:\inetpub\CierreDeCaja`) — **no** como aplicación bajo otro sitio
  (la app tiene `<base href="/">` y se rompe bajo un subpath).
- Para la primera prueba, binding **http** en un puerto libre (ojo: `8080` suele
  estar tomado por http.sys). Luego se agrega el binding **https** con certificado.
- El `web.config` ya viene en el paquete (handler `AspNetCoreModuleV2`,
  `hostingModel="inprocess"`). Solo se toca para el paso 5.

```powershell
Import-Module WebAdministration
New-Website -Name "CierreDeCaja" -PhysicalPath "C:\inetpub\CierreDeCaja" -ApplicationPool "CierreDeCaja" -Port 8088
```

## 4. Autenticación de Windows  ← **hacerlo antes de la primera carga**

> Con `hostingModel="inprocess"`, el handler `Negotiate` de la app **exige** que
> la Autenticación de Windows de IIS esté habilitada. Si no lo está, la app tira
> **HTTP 500.30** al arrancar (`InvalidOperationException: The Negotiate
> Authentication handler cannot be used on a server that directly supports
> Windows Authentication`). Por eso este paso va antes de probar.

Si la característica no está instalada: `Install-WindowsFeature Web-Windows-Auth`.

Luego, sobre el sitio → *Autenticación*:

- **Autenticación de Windows: Habilitada**
- **Autenticación anónima: Deshabilitada**

```powershell
Restart-WebAppPool CierreDeCaja
```

No hay que tocar código: fuera de `Development` la app toma `Negotiate` sola.
Confirmar solo que `ASPNETCORE_ENVIRONMENT` **no** esté en `Development`.

## 5. Cadena de conexión a Sage 50

No va en `appsettings.json`. Definir una variable de entorno **del sitio**
(IIS → *Configuration Editor* → `system.webServer/aspNetCore/environmentVariables`,
o agregando `<environmentVariables>` dentro de `<aspNetCore>` en `web.config`):

```xml
<aspNetCore processPath=".\PsaWeb.Host.exe" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="Sage50__ConnectionString"
      value="Driver={Pervasive ODBC Client Interface};ServerName=localhost;DBQ=<BASE>;UID=Peachtree;PWD=<clave>;" />
  </environmentVariables>
</aspNetCore>
```

(`__` = separador de sección). `Restart-WebAppPool CierreDeCaja` y el log debe
decir `Cierre de Caja: repositorio ODBC / Sage 50.`

**El nombre de la base (`DBQ`)** es el nombre de la empresa en Sage 50, en
mayúsculas y sin caracteres no alfanuméricos: `ROLLERDANCE-E-2025-26` →
`ROLLERDANCEE202526`. La lista del registro viejo de PSQL
(`...\PSQL\DBNamesDirectory\dbnames.cfg`) **no** es la del motor Zen — no fiarse
de ella. Si da `Btrieve Error 2301 / Cannot locate the named database`, abrir esa
empresa una vez en Sage 50 (registra la base en Zen) y reintentar.

Para ver el log de arranque: poner `stdoutLogEnabled="true"` en `web.config`,
crear la carpeta `logs\`, reproducir el arranque, y volver a `false`. El error
real también sale en el Visor de eventos (origen `IIS AspNetCore Module V2`).

## 6. Prueba de humo

1. Desde un PC de la oficina en el dominio: abrir `https://<host>/` — debe
   entrar directo por SSO, sin pedir usuario. El nombre de dominio aparece
   arriba a la derecha.
2. Repetir desde una sesión RDS.
3. `/cierre-de-caja` → Consultar un rango con datos → verificar totales.
4. Exportar a Excel y abrir el archivo.
5. Revisar `logs\stdout*.log` o el Visor de eventos por errores de ODBC.

## Problemas típicos

| Síntoma | Causa | Solución |
|---|---|---|
| HTTP 500.19 / no arranca, falta `AspNetCoreModuleV2` | No está el Hosting Bundle | Instalar `dotnet-hosting-9.0.x-win.exe` + `iisreset` |
| HTTP 500.30 + `Negotiate ... cannot be used on a server that directly supports Windows Authentication` | Falta habilitar Windows Auth en IIS | Paso 4 (habilitar Windows Auth, deshabilitar Anónima) + `Restart-WebAppPool` |
| HTTP 500.30 / 500.31 (otro) | El `.exe` no levanta | Ver `logs\stdout` o Visor de eventos; suele ser el paso 5 mal |
| `IM014 architecture mismatch` en el log | Pool no está en 32 bits | App pool → *Habilitar aplicaciones de 32 bits* = True |
| `28000 Invalid user authorization` | Falta `UID`/`PWD` en la cadena | `UID=Peachtree;PWD=<clave>;` |
| `Btrieve Error 2301 / Cannot locate the named database` | `DBQ` no está registrado en el motor Zen | Nombre = empresa en mayúsculas sin símbolos; abrir la empresa una vez en Sage 50 para registrarla |
| El navegador pide usuario/contraseña | El host no está en *Intranet local* del cliente, o SPN | Agregar el sitio a Intranet local; revisar SPN de la cuenta del pool |
| 403 tras autenticar | La identidad del pool no tiene NTFS sobre la carpeta de Sage | Dar lectura a esa cuenta |

## Para la ola de 25 (nota para F6)

Evaluar un **servidor de aplicaciones dedicado** en vez de co-hospedar en el
host RDS: no competir por CPU/RAM con las sesiones RDP y desacoplar reinicios.
El procedimiento es el mismo.

## Notas de base de datos

- `PsaWeb.PeachEbills` consultas: evitar `List.Contains` en LINQ (en SQL Server
  con compat level < 130 se traduce a `OPENJSON` y falla). El backup de
  `PeachEBills` viene en compat 120; conviene subirlo a 150+ en el servidor:
  `ALTER DATABASE [PeachEBills] SET COMPATIBILITY_LEVEL = 150`.

## Módulo Retenciones (Ola 1) — configuración en el servidor

El módulo `/retenciones` se **activa solo si** está `PeachEbills__ConnectionString`.
Sin esa variable, el resto del sitio (piloto Cierre de Caja) funciona igual y la
página muestra "Módulo no configurado".

Variables de entorno del sitio (mismo `<environmentVariables>` del paso 5):

```xml
<environmentVariable name="PeachEbills__ConnectionString"
  value="Server=localhost;Database=PeachEBills;Trusted_Connection=True;TrustServerCertificate=True" />
<!-- DryRun=true: arma y valida cada retención pero NO la envía a Datil ni la guarda.
     Enviar de verdad autoriza el comprobante ante el SRI (irreversible): dejarlo en
     true hasta validar contra Datil EN EL SERVIDOR. -->
<environmentVariable name="Datil__DryRun" value="true" />
<!-- Worker en segundo plano: apagado por defecto. Prender recién cuando DryRun=false
     esté validado y se quiera emisión automática. -->
<environmentVariable name="Retenciones__Worker__Habilitado" value="false" />
<environmentVariable name="Retenciones__Worker__Intervalo" value="01:00:00" />
<environmentVariable name="Retenciones__Worker__RetrasoInicial" value="00:02:00" />
<!-- RUCs a saltar (opcional): índice por variable -->
<!-- <environmentVariable name="Retenciones__OmitirRucs__0" value="1790000000001" /> -->
```

- La cuenta del app pool necesita acceso a SQL Server `PeachEBills` (login +
  `db_datareader`/`db_datawriter`; el alta de retenciones **escribe**
  `TaxWithHoldings` + `THDetails` + `DatilRequests`). Con `Trusted_Connection` el
  login es la identidad del pool (`IIS APPPOOL\<pool>` o la cuenta de servicio).
- Log de arranque esperado: `Retenciones: módulo ACTIVO (PeachEBills configurado).`
  y, si el worker está prendido, `Worker de retenciones ACTIVO. Primera corrida
  en … luego cada …`.
- El botón "Ejecutar ahora" de la página y el worker comparten un candado de un
  solo cupo (`EjecucionRetencionesGate`): nunca corren dos generaciones a la vez;
  la segunda recibe "Ya hay una corrida en curso".
- Cada empresa cuyo Sage 50 multi-empresa no se pueda abrir cuenta como "con
  error" en el resumen y la corrida sigue con las demás (no aborta).
- Orden de puesta en marcha: (1) subir con `DryRun=true` y worker apagado, revisar
  el panorama y correr "Ejecutar ahora" una vez — todo debe quedar en "armadas
  OK"; (2) pasar `Datil__DryRun=false` y volver a correr a mano validando en
  Datil; (3) recién ahí `Retenciones__Worker__Habilitado=true`.

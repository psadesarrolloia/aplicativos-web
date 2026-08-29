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

- Apuntar a `C:\inetpub\apps\cierre-de-caja`, usando el pool anterior.
- Binding **https** con certificado (mismo criterio que ya usan para el RDS).
- El `web.config` ya viene en el paquete (handler `AspNetCoreModuleV2`,
  `processPath=".\PsaWeb.Host.exe"`, `hostingModel="inprocess"`). No editarlo salvo
  el paso 4.

## 4. Autenticación de Windows

En IIS, sobre el sitio → *Autenticación*:

- **Autenticación de Windows: Habilitada**
- **Autenticación anónima: Deshabilitada**

En `Development` la app firma como el usuario local (handler de dev); fuera de
`Development` toma `Negotiate` automáticamente (ver `Program.cs`). No hay que
tocar código: confirmar solo que `ASPNETCORE_ENVIRONMENT` **no** esté en
`Development` (por defecto es `Production`).

## 5. Cadena de conexión a Sage 50

No va en `appsettings.json`. Definir una variable de entorno **del sitio**
(IIS → *Configuration Editor* → `system.webServer/aspNetCore/environmentVariables`,
o agregando `<environmentVariables>` dentro de `<aspNetCore>` en `web.config`):

```
Sage50__ConnectionString = Driver={Pervasive ODBC Client Interface};ServerName=<host-sage>;DBQ=<base>;UID=Peachtree;PWD=<clave>;
```

(`__` = separador de sección). Al arrancar, el log debe decir
`Cierre de Caja: repositorio ODBC / Sage 50.`

Para ver el log de arranque: poner `stdoutLogEnabled="true"` en `web.config`,
crear la carpeta `logs\`, reproducir el arranque, y volver a `false`.

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
| HTTP 500.30 / 500.31 | El `.exe` no levanta | Ver `logs\stdout` (activar `stdoutLogEnabled`); casi siempre es el paso 5 mal |
| `IM014 architecture mismatch` en el log | Pool no está en 32 bits | App pool → *Habilitar aplicaciones de 32 bits* = True |
| `28000 Invalid user authorization` | Falta `UID`/`PWD` en la cadena | `UID=Peachtree;PWD=<clave>;` |
| El navegador pide usuario/contraseña | El host no está en *Intranet local* del cliente, o SPN | Agregar el sitio a Intranet local; revisar SPN de la cuenta del pool |
| 403 tras autenticar | La identidad del pool no tiene NTFS sobre la carpeta de Sage | Dar lectura a esa cuenta |
| Conecta pero 0 filas | `DBQ` equivocado | Confirmar el nombre Pervasive de la empresa en `dbnames.cfg` |

## Para la ola de 25 (nota para F6)

Evaluar un **servidor de aplicaciones dedicado** en vez de co-hospedar en el
host RDS: no competir por CPU/RAM con las sesiones RDP y desacoplar reinicios.
El procedimiento es el mismo.

# F5 — Promover el piloto a `psacontabilidad2`

Pasar la app del entorno de desarrollo (PREDATOR) al host de sesión RDS
`psacontabilidad2`, que está unido al dominio. Ahí se activa el Windows Auth
real contra Active Directory y se valida el driver ODBC bajo IIS.

Requisitos en `psacontabilidad2`:

- **IIS** con el **ASP.NET Core Hosting Bundle 9** instalado
  (`dotnet-hosting-9.0.x-win.exe` de Microsoft — instala el módulo `AspNetCoreModuleV2` y el runtime).
- El driver **Pervasive ODBC Client Interface** (ya está: la máquina ejecuta los `.exe`).
- Ruta de red a la base de Sage 50 (ya la tiene).

## 1. Publicar (en PREDATOR o en un build server)

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained false -o publish
```

`-r win-x86` porque el driver Pervasive es de 32 bits. Copiar la carpeta
`publish\` a `psacontabilidad2`, p. ej. `C:\inetpub\apps\cierre-de-caja`.

## 2. Sitio y pool en IIS

- **Application pool** nuevo, p. ej. `PsaWebCierreDeCaja`:
  - .NET CLR version: **Sin código administrado** (el módulo ASP.NET Core hostea el proceso).
  - **Habilitar aplicaciones de 32 bits = True**  ← imprescindible para el ODBC de Pervasive.
  - Identidad: una cuenta de dominio de servicio con permiso de lectura sobre la
    carpeta de Sage 50 (o `ApplicationPoolIdentity` si esa cuenta ya tiene acceso).
- **Sitio / aplicación** apuntando a `C:\inetpub\apps\cierre-de-caja`, con ese pool.
- Binding: `https` con un certificado (el mismo criterio que ya usan para el RDS).

## 3. Autenticación de Windows

En IIS, sobre el sitio:

- **Autenticación de Windows: Habilitada**
- **Autenticación anónima: Deshabilitada**

En `Development` la app usa un handler que firma como el usuario local; en
`Release` no se registra ese handler y toma `Negotiate` (ver `Program.cs`).
No hay que tocar código: el entorno lo define IIS (`ASPNETCORE_ENVIRONMENT`
= `Production` por defecto). Confirmar que **no** esté seteado a `Development`.

## 4. Cadena de conexión a Sage 50

No va en `appsettings`. Definir una variable de entorno **a nivel del sitio**
(IIS → Configuration Editor → `system.webServer/aspNetCore/environmentVariables`,
o en el `web.config` generado):

```
Sage50__ConnectionString = Driver={Pervasive ODBC Client Interface};ServerName=<host-sage>;DBQ=<base>;UID=Peachtree;PWD=<clave>;
```

(doble guion bajo `__` = separador de sección). Al arrancar, el log debe decir
`Cierre de Caja: repositorio ODBC / Sage 50.`

## 5. Prueba de humo

1. Desde un PC de la oficina en el dominio: abrir `https://<host>/` — debe
   entrar directo (SSO), sin pedir usuario. El nombre de dominio aparece arriba a la derecha.
2. Repetir desde una sesión RDS.
3. `/cierre-de-caja` → Consultar un rango con datos → verificar totales.
4. Exportar a Excel y abrir el archivo.
5. Revisar el log de la app (`publish\logs\` si `stdoutLogEnabled=true` en
   `web.config`, o el Visor de eventos) por errores de ODBC.

## Problemas típicos

| Síntoma | Causa | Solución |
|---|---|---|
| HTTP 500.31 / 500.30 al arrancar | Falta el Hosting Bundle o el runtime | Instalar `dotnet-hosting-9.0.x` y reiniciar IIS (`iisreset`) |
| `IM014 architecture mismatch` en el log | Pool no está en 32 bits | App pool → *Habilitar aplicaciones de 32 bits* = True |
| Pide usuario y contraseña en el navegador | Falta configurar el SPN / el sitio no es de confianza | Agregar el host a *Intranet local* en el cliente, o revisar SPN de la cuenta del pool |
| 403 tras autenticar | La cuenta no tiene permiso NTFS sobre la carpeta de Sage | Dar lectura a la identidad del pool |
| Conecta pero devuelve 0 filas | Base equivocada en `DBQ` | Confirmar el nombre Pervasive de la empresa (ver `dbnames.cfg`) |

## Para la ola de 25 (nota para F6)

Evaluar un **servidor de aplicaciones dedicado** en vez de co-hospedar en el
host RDS: evita competir por CPU/RAM con las sesiones RDP y desacopla los
reinicios. El resto del procedimiento es el mismo.

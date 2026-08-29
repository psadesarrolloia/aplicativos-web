# Validar el Cierre de Caja contra Sage 50 real

Por defecto la app usa **datos de muestra** (la cadena de conexión viene vacía).
Para probar contra una empresa real de Sage 50 y comparar los números con el
ejecutable original `ReceiptsReportRollerD`.

## Empresa: Roller Dance (local, en PREDATOR)

- Carpeta: `C:\Sage\Peachtree\Company\rollerda`
- Nombre de base Pervasive: **`ROLLERDANCEE202526`** (registrado en
  `C:\Program Files (x86)\Pervasive Software\PSQL\DBNamesDirectory\dbnames.cfg`)
- Motor: Actian Zen v15 local · usuario ODBC `Peachtree` · **la clave es la misma
  que usa el `.exe`** (aparece como `pwd=` en el código de `ReceiptsReportRollerD`)
- Conexión **verificada** (32-bit): 97 tablas, `JrnlHdr` con 21.856 filas,
  transacciones del 06/08/2022 al 26/08/2026.

## 1. Abrir una terminal en el proyecto Host

`C:\PROYECTOS IA\Aplicativos web\src\PsaWeb.Host` — en Visual Studio, clic
derecho sobre **PsaWeb.Host** → *Abrir en el terminal*.

## 2. Guardar la cadena de conexión en User Secrets

**No va en git** — queda en `%APPDATA%\Microsoft\UserSecrets\` de tu usuario.
PowerShell (comillas simples para que `{ }` y `;` sean literales):

```powershell
dotnet user-secrets set "Sage50:ConnectionString" 'Driver={Pervasive ODBC Client Interface};ServerName=localhost;DBQ=ROLLERDANCEE202526;UID=Peachtree;PWD=<clave>;'
```

Reemplazá `<clave>` por la contraseña ODBC de Sage 50 (la misma `pwd=` del
`.exe`). Verificar: `dotnet user-secrets list`

> Para la empresa remota (backup) era
> `...;servername=192.168.0.11.1583;DBQ=ptautobackup16;...` — pero esa base no
> tiene movimientos de caja.

## 3. Ejecutar

F5 en Visual Studio, o `dotnet run`. En el log de arranque debe decir:

```
Cierre de Caja: repositorio ODBC / Sage 50.
```

## 4. Comparar

En `/cierre-de-caja`, consultá un rango con datos y compará contra el `.exe`
para el mismo rango: Total ventas, Total cobros, Diferencia, el detalle por
recibo, y el Excel exportado.

**Referencia ya calculada** con las consultas del repositorio,
rango **01/08/2026 – 26/08/2026**:

| | |
|---|---|
| Efectivo | 114,49 |
| Tarjeta de crédito | 524,61 |
| Transferencia | 1.685,44 |
| **Total cobros** | **2.324,54** |
| **Total ventas** | **2.324,62** |
| **Diferencia** | **0,08** |

## 5. Volver a datos de muestra

```powershell
dotnet user-secrets remove "Sage50:ConnectionString"
```

o, sin borrar la cadena: `dotnet user-secrets set "Sage50:UseSampleData" "true"`

## Si falla la consulta

La pantalla muestra `No se pudo consultar Sage 50: <detalle>`. Causas típicas:

| Mensaje | Causa | Qué hacer |
|---|---|---|
| `IM014 ... architecture mismatch` | El proceso no es de 32 bits | Confirmar `<PlatformTarget>x86</PlatformTarget>` en `PsaWeb.Host.csproj` |
| `28000 ... Invalid user authorization` | Falta `UID`/`PWD` o son incorrectos | Deben ir `UID=Peachtree;PWD=<clave>;` en la cadena |
| `Data source name not found` | Nombre del driver mal escrito | Exactamente `Pervasive ODBC Client Interface` |
| Acentos raros en los nombres de recibo | Codepage del driver | Probar `Pervasive ODBC Unicode Interface` en vez de `Client Interface` |
| Timeout / servidor no encontrado | Motor Zen detenido | Servicio `Zen` (o `Actian Zen`) en Windows |

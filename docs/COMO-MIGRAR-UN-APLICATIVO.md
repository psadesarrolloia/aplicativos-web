# Cómo migrar el siguiente aplicativo

Guía basada en el piloto **Cierre de Caja** (`modules/PsaWeb.Modules.CierreDeCaja`).
Cada aplicativo de escritorio que se lleve a web entra como **un módulo** en
`modules/` y reutiliza el Host, el sistema de diseño y la capa de acceso a Sage 50
sin tocarlos.

## Lo que se reutiliza tal cual

| Proyecto | Qué da |
|---|---|
| `src/PsaWeb.Host` | Arranque, DI, autenticación (Windows/AD), layout PSA, ruteo, endpoints |
| `src/PsaWeb.Shared` | Tokens de diseño (`psa-theme.css`), componentes `Psa*` (Button, Card, PageHeader, Stat, DateRange, table) |
| `src/PsaWeb.Sage50` | `ISageConnectionFactory` (conexión ODBC a Sage 50 desde configuración) + `AddSage50()` |

Nunca se ponen credenciales en el código: la cadena de conexión vive en
`appsettings` (vacía en dev), User Secrets o variables de entorno.

## Pasos

### 1. Crear el módulo

```bash
dotnet new razorclasslib -o modules/PsaWeb.Modules.<Nombre> --name PsaWeb.Modules.<Nombre>
dotnet sln add modules/PsaWeb.Modules.<Nombre>
dotnet add modules/PsaWeb.Modules.<Nombre> reference src/PsaWeb.Shared src/PsaWeb.Sage50
dotnet add src/PsaWeb.Host reference modules/PsaWeb.Modules.<Nombre>
```

En el `.csproj` del módulo, borrar `<SupportedPlatform Include="browser" />`
(es Blazor Server y usa ODBC; si no, `CA1416` marca cada llamada a `System.Data.Odbc`).
Borrar los archivos de ejemplo (`Component1.*`, `ExampleJsInterop.cs`, `wwwroot/`).

### 2. Marcador de ensamblado + `_Imports.razor`

- `ModuleInfo.cs`: `public static class ModuleInfo { }` — lo usa el Host para descubrir las páginas.
- `_Imports.razor`: `@using PsaWeb.Shared.Components` (+ `Microsoft.Extensions.Logging` si la página loguea).

### 3. Capa de datos (`Data/`)

- `I<Nombre>Repository` con métodos `async` que devuelven DTOs (`record`s).
- `Odbc<Nombre>Repository` — la(s) consulta(s) real(es) de Sage 50:
  - SQL con parámetros posicionales `?` (nunca interpolar), `OdbcParameter` con `OdbcType`.
  - Conexión vía `ISageConnectionFactory.CreateConnection()`, `await using`, `OpenAsync`.
- `Sample<Nombre>Repository` — datos ficticios para desarrollo sin tocar Sage 50.

### 4. Registro (DI)

Un `<Nombre>Module.Add<Nombre>(IServiceCollection, IConfiguration)` que:
- registra el repo **real** si `Sage50:ConnectionString` está seteada y
  `Sage50:UseSampleData` != `true`; si no, el **de muestra**;
- registra servicios auxiliares (exportadores, etc.).

En `src/PsaWeb.Host/Program.cs`: `builder.Services.Add<Nombre>(builder.Configuration);`

### 5. Página

`Pages/<Nombre>.razor` con `@page "/<ruta>"`. Usar los componentes `Psa*`.
Estados obligatorios: **cargando**, **error**, **vacío / sin datos**, **resultado**.
Validaciones en cliente y servidor. Moneda y fechas en cultura `es-EC`.

Registrar el ensamblado del módulo en el Host (una sola vez por módulo):
- `src/PsaWeb.Host/Components/Routes.razor` → `AdditionalAssemblies="[... typeof(<Nombre>.ModuleInfo).Assembly]"`
- `Program.cs` → `.AddAdditionalAssemblies(typeof(<Nombre>.ModuleInfo).Assembly)` en `MapRazorComponents`

Agregar el enlace en `src/PsaWeb.Host/Components/Layout/NavMenu.razor`.

### 6. Exportaciones / descargas (si aplica)

- Un `<Nombre>Exporter` en `Export/` (ClosedXML para Excel, QuestPDF/otro para PDF).
- Un endpoint en `Program.cs`: `app.MapGet("/<ruta>/export", ...).RequireAuthorization();`
  que **re-consulta** con los mismos parámetros y devuelve `Results.File(bytes, contentType, nombre)`.
- El botón de la página hace `Nav.NavigateTo(url, forceLoad: true)`.
- Nunca `Process.Start` (eso era de escritorio).

### 7. Pruebas

`tests/PsaWeb.<Nombre>.Tests` (xunit). Cubrir el cálculo de negocio, el repo de
muestra y el exportador (reabrir el archivo y verificar celdas). `InternalsVisibleTo`
en el módulo si hay que testear tipos `internal`.

## Convenciones

- **x86**: solo el Host lo necesita (`<PlatformTarget>x86</PlatformTarget>`), por el
  driver Pervasive de 32 bits. Los módulos y libs quedan AnyCPU.
- **Cultura**: `es-EC` para moneda y fechas.
- **Sin credenciales en git**: `appsettings.Development.json` con cadena vacía.
- **Nada de lógica nueva de negocio**: se replica lo que hace el `.exe`, no se rediseña.
- El `.csproj` del Host mata instancias colgadas antes de compilar (`KillStaleHost`);
  si aparece "no se puede copiar .exe: en uso", es una instancia anterior — se resuelve solo.

## Checklist

- [ ] Módulo creado, referencias cruzadas, ejemplos borrados, `SupportedPlatform browser` quitado.
- [ ] `ModuleInfo` + `_Imports.razor`.
- [ ] `Data/`: interfaz + repo ODBC (parametrizado) + repo de muestra.
- [ ] `Add<Nombre>()` y llamada en `Program.cs`.
- [ ] Página con `@page`, componentes `Psa*`, los 4 estados, validaciones, `es-EC`.
- [ ] Ensamblado registrado en `Routes.razor` y `MapRazorComponents`. Enlace en `NavMenu`.
- [ ] Export + endpoint + botón (si aplica).
- [ ] Pruebas.
- [ ] `dotnet build` 0 warnings / 0 errors. `dotnet test` verde.
- [ ] Validado contra la base real y revisado por un usuario del área.

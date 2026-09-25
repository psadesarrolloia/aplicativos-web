# PSA Sage Bridge

Servicio de Windows que **escribe en Sage 50** por el SDK (`Sage.Peachtree.API` 2023, x86). La web nunca carga el SDK:
encola trabajos en `PsaWebPlataforma.TrabajosSage` y el Bridge los ejecuta. Diseño y decisiones:
`docs/PLAN-OLA2-COMPRAS-SAGE.md` (§3 arquitectura, §14 hallazgos de la F0, §15 F1).

## Piezas

| Proyecto | Qué es |
|---|---|
| `PsaWeb.SageBridge` (**anfitrión**, `.exe`) | Servicio `PsaSageBridge` / consola / supervisor. **No se toca**: Sage autoriza este ejecutable; si cambia su hash hay que volver a autorizarlo en cada empresa. Compilación determinista (misma fuente = mismo binario). |
| `PsaWeb.SageBridge.Logica` (DLL) | Todo lo demás: SDK, cola por ADO.NET, lotes, manejadores. Se actualiza sin re-autorizar. |
| `..\src\PsaWeb.SageBridge.Contratos` | Tipos, estados y reglas puras compartidas con la web (netstandard2.0). |

El supervisor lanza **el mismo .exe** con `--trabajador <pid>`: así el proceso que usa el SDK es el autorizado. Si el
trabajador sale con código 3 (error de base del SDK, que deja el proceso inservible) se relanza al instante; si se cae,
espera 5 s … 2 min.

## Compilar

```powershell
dotnet build bridge/PsaWeb.SageBridge.sln -c Release
```

Salida única en `bridge/bin/<Config>/` (exe + DLLs). Requiere Sage 50 2023 instalado (el SDK está en el GAC).

## Configurar (`PsaWeb.SageBridge.config.json`, junto al .exe)

Ver `PsaWeb.SageBridge.config.ejemplo.json`. Cadenas con seguridad integrada (la cuenta del servicio). La clave de
aplicación de Sage se cifra **en el servidor** con:

```powershell
.\PsaWeb.SageBridge.exe --proteger-clave
```

y el resultado va en `ClaveAplicacionProtegida`. Validar sin abrir Sage:

```powershell
.\PsaWeb.SageBridge.exe --probar-config
```

Solo en desarrollo: `ClaveAplicacionArchivo`, `ServidorSageOverride`, `BasesPorRuc` y `SoloBases` (candado: el Bridge
solo abre esas bases; en PREDATOR, la copia de prueba).

## Alta de una empresa

1. `/admin/sage-bridge` → **Probar** la empresa. Si Sage no autorizó al Bridge, queda una solicitud pendiente.
2. En Sage, **abrir esa empresa** (si ya estaba abierta, cerrarla y volver a abrirla) → *Always allow access*.
3. **Probar** otra vez → acceso `Granted` → **Habilitar** y fijar la ventana de mantenimiento (debe cubrir el backup
   automático de Sage: con la compañía abierta por el SDK, el backup falla).

La autorización es por **ejecutable y cuenta de Windows**: el servicio corre siempre con la misma cuenta local dedicada.

## Trabajos

| Tipo | Qué hace |
|---|---|
| `ProbarEmpresa` | Abre y cierra la compañía (solo lectura); deja pendiente la solicitud de acceso si falta. Corre aunque la empresa no esté habilitada. |
| `GuardarOc` | Crea o actualiza **en el lugar** la Purchase Order de una factura de compra, con su proveedor y la numeración (OC y retención) calculada al guardar. Lee Sage por ODBC (cadena de `PeachConnString`, como la web) solo para numerar y buscar la OC existente. |

Un rechazo de negocio (nº ya usado, OC ya convertida en compra, `Validate()` de Sage) deja el trabajo en Error sin reintentos.

## El anfitrión no debe cambiar

`PsaWeb.SageBridge.exe` se compila sin el commit en la versión (`IncludeSourceRevisionInInformationalVersion=false`): el mismo código da
el mismo binario en cualquier commit (SHA-256 `99C8E111…`; tampoco SourceLink ni PDB: el PDB embebido llevaba el commit). Si el hash cambia, Sage vuelve a pedir la autorización en cada empresa.

## Desarrollo

```powershell
cd bridge\bin\Debug
.\PsaWeb.SageBridge.exe --consola      # Ctrl+C detiene; log en logs\anfitrion-yyyyMMdd.log
```

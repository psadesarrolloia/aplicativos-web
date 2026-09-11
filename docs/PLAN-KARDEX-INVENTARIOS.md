# Plan — Reporte de inventarios (Kardex) → módulo web

Primer reporte que se **saca del monolito** `Sage50usIntegration`. No es un `.exe`
standalone: es la pantalla **Reportes → Inventarios → Kardex** (`StockConfig`,
título de ventana "Reporte de Stock") dentro del MDI. Se levanta sola como página
web, **solo lectura**, sin tocar el resto del monolito. Mismo patrón que el piloto
Cierre de Caja: `ISageConnectionFactory` + repo ODBC `SELECT` + ClosedXML + página
acotada a la empresa de sesión. **No usa el SDK de Sage ni el Sage Bridge.**

Esta app abre la línea "reportes del monolito uno por uno", donde está casi todo
el valor que resta de la Ola 1 (el pool de standalone read-only ya casi se agotó).

## 1. Qué hace la pantalla de escritorio

`Sage50usIntegration/Forms/Reports/StockConfig.cs` (+ `.Designer.cs`). Se abre
desde `frmProofInvoice` con `sageContext.Session.SessionActive && Company != null`
(o sea: **contra la empresa abierta en Sage 50**). Sin chequeo de permiso propio.

Menú hermano **"Kardex por Bodegas"** (`kardesxPorBodegasToolStripMenuItem`):
existe en el Designer pero **no tiene handler conectado**; su lógica
(`Services/InventoryCeller/InventoryCellerQuery.cs`, `ExcelReports/KardexCeller.cs`)
está **entera comentada** y apuntaba al SQL propio `PeachEBillsDB`, no a Sage.
→ La migración cubre **solo el Kardex simple, sin bodega**.

### 1.1 Flujo

1. El usuario fija **Desde / Hasta** (fecha) y opcionalmente acota el universo de
   ítems: por **Cuenta** (ID de cuenta GL — tilda solo los ítems cuyo
   `InvAcctRecordNumber` coincide), por **rango de ItemID** (Desde / Hasta
   contiguo), o tildando ítems en la grilla `dgvItems`. Sin ítems tildados →
   **todos** los ítems stock.
2. **"Actualizar Filtros"** (`btnApplyConfig`) valida `Hasta > Desde`, corre las
   consultas y llena la grilla de resultado (`dgvStockHistory`, `ReadOnly`).
3. **"Generar"** (`btnReportToPrint`) exporta el Kardex a `.xlsx` (EPPlus) y abre
   el explorador en el archivo.

**No hay botón de recalcular costos / ajustar / postear.** `grep` de
`ExecuteNonQuery` / `.Save()` / `INSERT` / `UPDATE` / `DELETE` / `OdbcCommand` en
todo el camino (`StockConfig`, `InventroyCostQuery`, `sageItems`, `SageAccounts`,
los dos exporters) → **cero coincidencias**. `SageODBCcnn.viewDataSet()` =
`Open` → `OdbcDataAdapter.Fill` → `Close`. El costeo **se lee** de la tabla
`InventoryCosts` (que Sage mantiene según el método de la empresa —
promedio / PEPS / UEPS); el reporte no lo calcula ni lo devuelve.

### 1.2 Motor de datos — `Services/InventoryCost/InventroyCostQuery.cs`

Tres consultas ODBC contra tablas Zen de Sage 50: `LineItem`, `InventoryCosts`,
`JrnlHdr`. `MajorType`: **1 = compra, 2 = venta, 3 = saldo** (fila de balance que
Sage guarda por transacción).

| # | Qué trae | Filtro |
|---|---|---|
| **Q1 – saldo inicial** | por ítem, la última fila de balance previa al rango | `MajorType = 3` `AND TransDate < desde` |
| **Q2 – movimientos** | compras y ventas del rango | `MajorType <> 3` `AND TransDate BETWEEN desde AND hasta`, `ORDER BY ItemID, TransDate, MajorType` |
| **Q3 – saldo por movimiento** | *(una llamada por cada fila de Q2)* el snapshot de saldo de esa transacción | `MajorType = 3` `AND PostOrderNumber = <n>` `AND ItemID = '<id>'` |

Armado por ítem (`inventoryCostRow` = `{ Cuenta, ItemID, Nombre, Categoría,
Fecha, Referencia, Entradas, Salidas, Saldos }`, cada bloque `kardex` =
`{ Cant, CostoU, CostoT }`):

- **Fila `.INICIAL.`** (fecha = `desde`): de Q1, tomar la de `TransDate` máximo.
  `Cant = Quantity`, `CostoT = TransAmount`, `CostoU = CostoT / Cant` *(nótese:
  ignora `OptAmount`)*. Solo bloque Saldos.
- **Filas de movimiento** (de Q2, en orden `TransDate, MajorType`):
  `Cant = Quantity`, `CostoU = OptAmount`, `CostoT = TransAmount`.
  - compra (`MajorType 1`): `if (CostoU == CostoT) CostoU = CostoT / Cant`
  - venta (`MajorType 2`): `if (CostoU == 0) CostoU = CostoT / Cant`
  - `MajorType 1` → bloque **Entradas** = ese kardex, Salidas vacío.
  - `MajorType 2` → bloque **Salidas** = ese kardex, Entradas vacío.
    (Las cantidades de venta vienen **negativas** en `InventoryCosts` — se muestran
    tal cual, ej. `Cant: -7.446,40` en las capturas de CPTDC.)
  - bloque **Saldos** = de Q3: `Cant = Quantity`, `CostoU = OptAmount`,
    `CostoT = TransAmount` (sin derivar).

### 1.3 Listas auxiliares (también ODBC puro, pese al `using Sage.Peachtree.API`)

- Ítems — `Model/GUI/sageItems.cs`, overload por clasificación:
  `SELECT ItemID, ItemDescription, ItemClass, Category, SaleAcctRecordNumber,
  InvAcctRecordNumber, COGSAcctRecordNumber FROM LineItem
  WHERE (ItemIsInactive = 0) AND ItemClass = 1 ORDER BY ItemID`
  — **`ItemClass = 1`** (el `.exe` filtra por `InventoryItemClassification.StockItem`
  y hace `(int)enum - 1`; `StockItem` = 2 → 1). **Verificado contra CPTDC
  (2026-09-10):** `ItemClass 0` = pseudo-ítems de retención/impuesto (categorías
  `R-IRF` / `R-IVA` / `IMPUESTO`); **`ItemClass 1` = inventario real** (1837 ítems
  activos: casing, cable, wellheads); `2` = descuento/IVA; `3` = pozos/ensamblados;
  `4` = servicios.
- Cuentas — `Model/GUI/SageAccounts.cs`:
  `SELECT GLAcntNumber, AccountID, AccountDescription, AccountType FROM Chart`.
- Mapa "Cuenta" del reporte: `item.InvAcctRecordNumber == account.GLAcntNumber`
  → `account.AccountID`.

### 1.4 Excel — `ExcelReports/InventoryCost.cs` (~230 LOC, EPPlus)

Hoja "Kardex". Un **bloque de encabezado por ítem** (se repite): fila combinada
`Entradas` (F:H) / `Salidas` (I:K) / `Saldos` (L:N), subcolumnas `Cant. / Costo U.
/ Costo T.`; columnas `Cuenta`, `ItemID`, `Item Nombre`, `Fecha`, `Referencia`.
La fila `.INICIAL.` escribe los valores de Saldos **literales**; el resto de las
filas escribe **fórmulas de saldo corrido** en L/M/N
(`L = F + I [+ L fila-1]`, `M = IF(L=0,0,N/L)`, `N = H + K [+ N fila-1]`).
Formato de fecha corto, números `0.00`, encabezados en negrita azul `#010A5F`,
fondos `#BFCEFD`.

> La grilla de la pantalla de escritorio muestra solo `ID / Fecha / Nombre /
> Referencia / Entradas / Salidas` (sin Cuenta ni Saldos). El **Excel** trae todo.
> La página web replicará el layout del Excel (más completo y es lo que el
> usuario efectivamente recibe hoy).

## 2. Lo que ya está y se reutiliza tal cual

- **Shell**: empresa + ambiente por sesión (`EmpresaActualService`,
  `EmpresaSwitcher`, `/seleccionar-empresa`). El usuario confirmó: **solo la
  empresa abierta / de sesión** — sin cross-company, sin worker.
- **`PsaWeb.Sage50`**: `ISageConnectionFactory` (`CreateConnection()` /
  `CreateConnection(string)`), `AddSage50(config)`.
- **`PsaWeb.PeachEbills`**: `PeachConnStringResolver` (RUC → cadena ODBC de Sage
  por empresa, desencriptando la clave) + `DbSecret` + `PeachEbillsOptions.
  SageServerNameOverride` (poner `localhost` en dev; vacío en prod).
- **`PsaWeb.Seguridad`**: `Permisos`, `AppCatalogo` (nav / dashboard dinámico),
  `ISecurityDirectory.EmpresasDelUsuarioAsync` / `TienePermisoAsync`.
- **Patrón de módulo de Cierre de Caja** (`COMO-MIGRAR-UN-APLICATIVO.md`):
  `IResolverEmpresaSage` / `SinShellResolverEmpresaSage` +
  `HostResolverEmpresaSage` (en el Host, RUC de sesión → cadena por
  `PeachConnStringResolver`), repo ODBC + repo de muestra, endpoint `/…/export`
  con `&ruc=` → **403** si el usuario no tiene acceso a esa empresa.
- **`PsaWeb.Shared`**: componentes `Psa*`, `psa-theme.css`, `.psa-input--activo`.
- **ClosedXML 0.105.1** ya en la solución (reemplaza EPPlus).

## 3. Lo que hay que construir

### 3.1 Resolver de empresa compartido (refactor chico)

Hoy `IResolverEmpresaSage` vive en `modules/PsaWeb.Modules.CierreDeCaja/Data/`.
**Extraerlo a `PsaWeb.Sage50`** (interfaz + `SinShellResolverEmpresaSage`) para que
Kardex y Cierre de Caja lo compartan; `HostResolverEmpresaSage` pasa a implementar
la versión compartida y se registra una sola vez. Alternativa de bajo riesgo:
duplicar la interfaz de 4 miembros en el módulo Kardex. **Recomendado: extraer.**

### 3.2 `modules/PsaWeb.Modules.Kardex` (RCL nuevo)

- **DTOs** (`Data/Modelos.cs`): `MovimientoKardex(decimal? Cant, decimal? CostoU,
  decimal? CostoT)`; `FilaKardex(string CuentaGl, string ItemId, string Nombre,
  string Categoria, DateOnly Fecha, string Referencia, MovimientoKardex Entrada,
  MovimientoKardex Salida, MovimientoKardex Saldo, bool EsInicial)`;
  `ItemStock(string Id, string Nombre, string Categoria, string InvAcctRecordNumber)`;
  `CuentaGl(int GlAcntNumber, string Id, string Descripcion)`;
  `ResultadoKardex(IReadOnlyList<FilaKardex> Filas, bool SinMovimientos)`.
- **`Data/IKardexRepository`**: `ItemsStockAsync(filtro)`, `CuentasAsync()`,
  `GenerarAsync(FiltroKardex)` → `ResultadoKardex`. `FiltroKardex(DateOnly Desde,
  DateOnly Hasta, IReadOnlyList<string> ItemIds, string? CuentaGl, string?
  ItemDesde, string? ItemHasta)`. Con shell resuelve por RUC de sesión; expone
  también `GenerarParaRucAsync(ruc, filtro)` para el endpoint de export.
- **`Data/FiltroKardex` — al menos un acotador obligatorio.** CPTDC tiene 1837
  ítems stock activos; "todos los ítems" (lo que hace el `.exe` con la lista
  vacía) es impracticable en web. La página exige **ítems seleccionados, cuenta
  GL, o rango de ItemID**. La cuenta de inventario es el filtro natural
  (`13101 INVENTARIOS CASING`, `13102 INVENT MACHIRY…`, `13103 INVENT WELLHEADS`).
- **`Data/OdbcKardexRepository`** — las 3 consultas de §1.2 **colapsadas** y con
  `OdbcParameter` posicionales `?` (nunca interpolar):
  - **Q1**: `MajorType = 3 AND TransDate < ?` + filtro de ítems; por ítem, la de
    `TransDate` máximo. Se descarta el `JOIN JrnlHdr` (la fila `.INICIAL.` no usa
    `Reference`).
  - **Q2**: `MajorType <> 3 AND TransDate BETWEEN ? AND ?` + filtro de ítems;
    `ORDER BY ItemID, TransDate, MajorType`. Trae `Reference` de `JrnlHdr`.
  - **Q3 (colapsada, era N+1)**: una sola consulta
    `MajorType = 3 AND PostOrderNumber IN (…)` con los `PostOrderNumber` de Q2
    (troceado en lotes si la lista es larga); indexar por `(ItemID,
    PostOrderNumber)` y tomar la primera fila.
  - **Filtro de ítems**: `ItemID IN (?, ?, …)` con la selección; si viene vacía,
    se resuelve el universo (cuenta / rango / todos los stock) **en C#** contra la
    lista ya cargada y se pasa como `IN` acotado — nunca OR-chain interpolada.
  - Ítems / cuentas: los `SELECT` de §1.3.
- **`Data/ArmadorKardex`** — lógica pura (port fiel del cuerpo de
  `InventroyCostQuery`), testeable, con los **bugs conservados y documentados**:
  - **B1**: la fila `.INICIAL.` usa `TransAmount / Quantity` como costo unitario,
    ignorando `OptAmount`.
  - **B2**: si un ítem tenía stock antes de `Desde` pero **sin** fila
    `MajorType = 3` previa, no hay `.INICIAL.` y el saldo corrido del Excel arranca
    de cero (solo el primer movimiento).
  - **B3**: en el Excel la columna **Saldos** de las filas no-iniciales son
    **fórmulas acumuladas** (F/I/L y H/K/N), no el snapshot `MajorType = 3` de Q3.
    La página web replica el acumulado para que **página y archivo coincidan**; el
    snapshot de Q3 se guarda igual en el DTO para control de calidad.
  - **B4**: derivación de `CostoU` en compra (`if CostoU == CostoT`) y venta
    (`if CostoU == 0`) tal cual el `.exe`.
  - **B5**: las consultas del Kardex no filtran `ItemIsInactive` (solo el
    selector de ítems lo hace).
- **`Data/SampleKardexRepository`** — 2–3 ítems con movimientos ficticios para
  dev sin Sage.
- **`Export/KardexExcelExporter`** (ClosedXML) — port de `ExcelReports/
  InventoryCost.cs`: bloque de encabezado por ítem, merges Entradas/Salidas/
  Saldos, **fórmulas de saldo corrido**, `.INICIAL.` con valores literales,
  formatos y colores PSA. Modernización visual mínima (paleta PSA, pie de firma)
  como se hizo en Cierre de Caja.
- **`KardexModule.AddKardex(services, config)`**: repo real si
  `Sage50:ConnectionString` seteada y `Sage50:UseSampleData != true`; si no, el de
  muestra. `TryAddScoped<IResolverEmpresaSage, SinShellResolverEmpresaSage>()`.
  `AddSingleton<KardexExcelExporter>()`.
- **`Pages/Kardex.razor`** (`@page "/kardex"`): filtros (Desde/Hasta con
  validación `Hasta > Desde`; Cuenta GL; rango ItemID; selección múltiple de ítems
  con búsqueda) — **al menos uno de {ítems, cuenta, rango} obligatorio**; los
  **4 estados** (cargando / error / vacío / resultado), tabla estilo Excel
  (Cuenta · Item · Fecha · Ref + Entradas/
  Salidas/Saldos con 3 subcolumnas c/u), moneda y fechas `es-EC`, botón
  **"Exportar a Excel"** (`Nav.NavigateTo(url, forceLoad: true)`), subtítulo con la
  empresa de sesión, reacción a `EmpresaActual.Cambio` (re-scope + limpiar
  resultado).

### 3.3 Host

- `Program.cs`: `builder.Services.AddKardex(builder.Configuration);` +
  `HostResolverEmpresaSage` registrado para la interfaz compartida (cubre Cierre
  de Caja y Kardex).
- Endpoint `GET /kardex/export` — re-consulta con los mismos parámetros, devuelve
  el `.xlsx` como `attachment`; `RequireAuthorization`; con `ruc` valida acceso
  (`EmpresasDelUsuarioAsync`) → **403**; sin `ruc` = standalone.
- `Components/Routes.razor` → `AdditionalAssemblies` + `MapRazorComponents(...)
  .AddAdditionalAssemblies(typeof(Kardex.ModuleInfo).Assembly)`.
- `NavMenu.razor`: se arma solo desde `AppCatalogo` (nada que tocar a mano).

### 3.4 Permiso

- `Permisos.VerKardex = "quKardex"` (solo consulta; no hay "mk").
- `AppCatalogo`: entrada `("kardex", "Kardex", "Kardex de inventarios de Sage 50.",
  "📦", "/kardex", …)`. Mientras el área no cargue `quKardex` en `allowAction` de
  PeachEBills, se usa el patrón **`GateProvisional`** (lista de permisos vacía →
  visible para cualquier empresa), igual que `fe-liquidaciones`. Revertir a
  `new[] { Permisos.VerKardex }` en una línea cuando esté el código.

### 3.5 Pruebas — `tests/PsaWeb.Kardex.Tests` (xunit)

- `ArmadorKardex`: casos golden — solo inicial, inicial + compras + ventas,
  ítem sin inicial (B2), costo unitario derivado en compra/venta (B4), cantidades
  negativas de venta, multi-ítem, sin movimientos.
- Mapa ítem → cuenta GL.
- `SampleKardexRepository`.
- `KardexExcelExporter`: reabrir el `.xlsx` y verificar encabezados combinados,
  fórmulas de L/M/N, valores literales de la fila `.INICIAL.`, formatos numéricos.

## 4. Fases

| Fase | Contenido | Sale |
|---|---|---|
| **F1** | Refactor §3.1 (extraer `IResolverEmpresaSage` a `PsaWeb.Sage50`). Módulo `PsaWeb.Modules.Kardex` scaffold (patrón COMO-MIGRAR): `ModuleInfo`, `_Imports`, DTOs, `IKardexRepository` + `SampleKardexRepository`, `AddKardex()`. Página `/kardex` solo con filtros (sin datos). Registro en Host + `AppCatalogo` + nav. `dotnet build` 0/0. | Módulo visible en el nav, página con filtros y datos de muestra. |
| **F2** | `OdbcKardexRepository` (las 3 consultas colapsadas + ítems + `Chart`, `OdbcParameter ?`). `ArmadorKardex` (port fiel, bugs B1–B5 documentados). Página cableada: 4 estados, validación, `es-EC`, selección de ítems / rango / cuenta, reacción a `EmpresaActual.Cambio`. Tests de `ArmadorKardex` + sample. Verificado en dev con el sample repo. | Kardex funcionando en pantalla con datos de muestra y (si hay User Secret) contra Sage local. |
| **F3** | `KardexExcelExporter` (ClosedXML, port de `InventoryCost.cs` con fórmulas de saldo corrido). Endpoint `/kardex/export` (`&ruc=` + 403). Botón en la página. Tests del exporter. | `.xlsx` descargable idéntico en layout al del `.exe`. |
| **F4** | **Validación contra CPTDC real en PREDATOR** (§6). Correr `/kardex` acotado por cuenta `13101` (INVENTARIOS CASING) o por ítems `CS-*`, mismo rango, y comparar fila a fila + el `.xlsx` contra el `.exe`. Ajustar hasta paridad. Revisión de un usuario del área. | Paridad confirmada. |
| **F5** | Deploy a `SERWEBPSA01`: redeploy del Host con el módulo (junto con el fix de ícono pendiente `f4fd679` si sigue sin desplegar). **Ninguna env var nueva** (usa las del shell). Smoke test: `/kardex` carga, consulta CPTDC devuelve datos, export OK. | Kardex en producción para usuarios de la LAN. |

**Estimación: ~4–6 días de dev.** Es un clon del piloto Cierre de Caja + un
exporter con fórmulas. Riesgo bajo: el camino ODBC por empresa de sesión (vía
`PeachConnString`) ya está validado en el server con Cierre de Caja y FE.

## 5. Decisiones

1. **Alcance = empresa de sesión** (confirmado por el usuario). Sin cross-company,
   sin "Ver todas las empresas", sin worker.
2. **Solo lectura, sin bodega** (el "Kardex por Bodegas" del `.exe` está muerto).
   **Al menos un filtro obligatorio** (ítems / cuenta / rango) — sin "todos"
   implícito (CPTDC = 1837 ítems stock).
3. Página `/kardex`, un enlace en el nav, gateada por `quKardex` con
   `GateProvisional` hasta que el área cargue el código en `allowAction`.
4. **"Saldos" de la página = acumulado, como el Excel** (no el snapshot
   `MajorType = 3`), para que página y archivo den lo mismo. El snapshot de Q3 se
   conserva en el DTO para QA.
5. **Port fiel**: se conservan los bugs B1–B5 del `.exe`, documentados en el
   código.
6. **Q3 colapsada** de N+1 a una sola consulta (única desviación de rendimiento
   respecto del `.exe`; resultado idéntico).
7. Fixture: **CPTDC**, ya cargada en PREDATOR con inventario.

## 6. Fixture de prueba — CPTDC en PREDATOR (VERIFICADO 2026-09-10)

Empresa **CPTDC CHINA PETROLEUM TECHNOLOGY & DEVELOPMENT COR**, RUC
**`1792051800001`**, ejercicio 2025-2026, ya abierta en el `.exe` sobre PREDATOR.
Todo lo necesario **ya está en PREDATOR**; el único ajuste es de config de dev
(`servername`).

### 6.1 `PeachConnString` de PeachEBills (`PREDATOR\SQLEXPRESS`) — id 4

| Campo | Valor |
|---|---|
| `RUC` | `1792051800001` |
| `Driver` | `{Pervasive ODBC Client Interface}` (`DSN` vacío) |
| `uid` | `Peachtree` |
| `pwd` | `4gOp/yqYXe4=` → `JCV1234` (`DbSecret`) |
| `servername` | **`SERWEBPSA01`** (prod; inalcanzable desde PREDATOR — ver 6.3) |
| `dbq` | `cptdcecuadorsa202520` |
| `checkToLoadByRUC` | `1` |

`Transmitter`: `HaveToDoAccounting = 1`, `ToSendToDatil = 1`.
`UserTransmitter`: **`lparedes` ya está** para este RUC (+ `administrador`,
`peachb2019`, etc.) → CPTDC aparece en `/seleccionar-empresa` sin tocar nada.

### 6.2 Sage 50 local — OK (smoke ODBC 32-bit ejecutado en PREDATOR)

- DBQ Zen `CPTDCECUADORSA202520` **registrado en el legacy `dbnames.cfg`** de
  PREDATOR (`C:\Program Files (x86)\Pervasive Software\PSQL\DBNamesDirectory\`).
- Cadena que **funciona desde 32 bits** (probada — corren `LineItem`,
  `InventoryCosts`, `Chart` y las 3 consultas del Kardex):
  ```
  Driver={Pervasive ODBC Client Interface};servername=localhost;uid=Peachtree;dbq=CPTDCECUADORSA202520;pwd=JCV1234;
  ```
- `InventoryCosts`: `MajorType 1` 9.381 filas (hasta 25/08/2026), `MajorType 2`
  7.141 (hasta 01/09/2026), `MajorType 3` 11.438. Datos reales de casing:
  compras `Reference` `LIQ IMPORT nnn-aaaa` (`Quantity` +, `OptAmount = TransAmount`
  → dispara la derivación de costo B4), ventas `Reference` `001-001-nnnnnnnnn`
  (`Quantity` −, `OptAmount = 0` → dispara la otra derivación), y ajustes
  `AJT-INVT-aaaa-nnn`. `MajorType 3` trae el saldo corrido con `OptAmount` = costo
  unitario (~29,91 en CS-012).
- Cuentas de inventario en `Chart`: `13101 INVENTARIOS CASING`,
  `13102 INVENT MACHIRY AND PETR EQUIP`, `13103 INVENT WELLHEADS`.
- **`ItemClass`**: `1` = inventario real (1.837 ítems activos); `0` = pseudo-ítems
  de retención/impuesto; `2` = descuento/IVA; `3` = pozos/ensamblados; `4` =
  servicios. → el filtro del selector de ítems es `WHERE (ItemIsInactive = 0)
  AND ItemClass = 1`.

### 6.3 Único ajuste para dev

`PeachConnString.servername = SERWEBPSA01` (inalcanzable desde PREDATOR). **No
mutar la fila compartida.** En dev: `PeachEbills:SageServerNameOverride = localhost`
(User Secret del Host — mismo mecanismo que FE) **o** `Sage50:ConnectionString`
directa a CPTDC y correr sin shell.

### 6.4 Pendiente antes de F5

**Área**: cargar `quKardex` en `allowAction` de `PeachEBills` y asignarlo a los
roles que deban ver el reporte. (Hasta entonces, `GateProvisional`.)

### 6.5 Validación F4 — HECHA (2026-09-10)

En vez de correr el `.exe` WinForms (MDI, difícil de automatizar), se hizo una
**re-implementación independiente** de `InventroyCostQuery.cs` como script ODBC
(`scratchpad/kardex-reference.ps1`): las **3 consultas verbatim** del monolito
(incluida la Q3 N+1 por `PostOrder`) + el armado C# (B1/B4, `.INICIAL.` =
`MajorType 3` más reciente previo a `Desde`).

**Resultado: `/kardex` de CPTDC, cuenta `13101` (INVENTARIOS CASING, 179 ítems),
rango 01/07/2026–31/08/2026 → 175 filas, IDÉNTICAS fila a fila y valor a valor
(redondeo 2 dec.) contra la referencia.** Cubre `.INICIAL.` con y sin saldo
previo, compras (`LIQ IMPORT`, derivación B4 `OptAmount==TransAmount`), ventas
(`001-001-*`, `OptAmount==0`), ajustes (`AJT-INVT-*`), cantidades de venta
negativas, y movimientos cuyo `PostOrder` no tiene snapshot `MajorType 3`
(saldo → 0).

**Desviación deliberada del `.exe`:** las 3 consultas y el armador agregan
`InventoryCosts.PostOrderNumber` como último criterio de orden. El `.exe` ordena
sólo por `(ItemID, TransDate, MajorType)`; cuando hay varios movimientos del
mismo tipo el mismo día (p. ej. 3 ventas de CS-008 el 01/07), su orden depende
del orden físico que devuelva Pervasive (no determinista). Con el desempate por
`PostOrder` el reporte web es reproducible corrida a corrida; los valores no
cambian, sólo se fija la secuencia de esas filas empatadas.

**Excel:** el endpoint `/kardex/export` devuelve OOXML válido para el mismo
filtro; los 8 tests del exporter verifican encabezado por ítem, `.INICIAL.`
literal vs fórmula de movimiento, refs `F+I(+L fila-1)` / `H+K(+N fila-1)`,
formatos y encabezado repetido.

**Pendiente de F4:** revisión por un usuario del área (comparar contra una
corrida real del `.exe` en el server, donde sí lo tienen a mano).

> Las capturas que pasó el usuario (filtro "9/1/2026".."9/10/2026", CS-012 con
> salidas "9/8/2026") eran de otra corrida/datos; referencia visual del layout,
> no golden numérico.

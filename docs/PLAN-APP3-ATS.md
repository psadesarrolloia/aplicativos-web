# Plan — App #3: ATSfromPeach → módulo web

Ola 1, aplicativo #3. Piloto (Cierre de Caja), app #1 (`AutomaticTwhSender` →
`/retenciones`) y app #2 (`Sage50FacturacionElectronica` → `/fe/*`) ya
desplegados en `SERWEBPSA01`. En paralelo, en otro chat, se está portando el
reporte **Kardex de inventarios** sobre `main` (rama `kardex-web`). Este
documento planifica el port de **`ATSfromPeach`** ("Generar ATS"). La
implementación arranca **cuando el Kardex esté cerrado**, para no pisar el wiring
compartido (`Program.cs`, `Routes.razor`, `Permisos.cs`, `AppCatalogo.cs`,
`NavMenu`, `psa-theme.css`, `_Imports`).

Fuente de escritorio:
`Código fuente Aplicativos/sage50Apps-master/ATSfromPeach/` (+ proyectos
`ATSinScheme`, `PeachEBillsDB`, `Sage50MetaData`, `ErrorSage50Proyect`).

---

## 1. Qué hace el aplicativo de escritorio

MDI (.NET Framework 4.6.1, 32 bits, EF6 + EPPlus 5.1.2 + Newtonsoft). `Program.cs`
→ `Form1` (shell MDI) con menú:

| Menú | Abre | Rol |
|---|---|---|
| Seleccionar Empresa | `FrmEmpresaSeleccionar` | grilla de `Transmitter`; al elegir, escribe `Properties.Settings.TransmitterRUC` (RUC = "empresa activa" de todo el `.exe`) |
| API DATIL | `DatilConfig` | config (no lo usa el ATS; vestigial) |
| **Consultar** | **`ATSform`** | **la ventana real: genera el ATS** |
| Administrar Empresas | `frmListaEmpresas` | ABM de `Transmitter` / `PeachConnString` |
| Configuración Json Forms | `SRIFormsConfig` | ABM de `ConfigJsonForms` (mapeo casilleros → JSON) |
| Configuración balance inicial | `FrmInitialBalanceConfig` | config del Formulario 101 |
| Generar 101 | (inline en `Form1`) | lee un Excel del Formulario 101 y emite un JSON |

> El aplicativo **no toca el SDK de Sage 50**. `ATSfromPeach.csproj` referencia
> `Sage50usIntegration` pero ningún `.cs` lo usa; lo único que se importa de esa
> familia es `Sage50MetaData.Model` (enums de journal + `ExternalVendorInfo`).
> Encaja 100 % en la categoría **solo lectura (ODBC)** de
> `APPS-SDK-VS-SOLO-LECTURA.md` — mismo patrón que Cierre de Caja / FE.

### 1.1 `ATSform` — el flujo

Entradas: `txtPeriodo` (año), `cboMonth` (mes `01`–`12`), y la **empresa =
`Settings.TransmitterRUC`**. Validación: `año <= año actual`.

1. **Botón "Cargar"** (`btnLoadPeach_Click`) → `LoadAtsMethod()` →
   `new LoadATS(RUC, año, mes)` → `LoadToATSobject(ref ivaType ivaATSformat)`.
   Arma **en memoria** un objeto `ivaType` (el esquema ATS del SRI). Muestra
   `MessageBox "Correctamente cargado a Memoria."`.
2. Todas las pestañas son **grillas / resúmenes de solo lectura** sobre ese
   objeto en memoria (proyectado a `DataTable` por reflexión en
   `Resources/ToDataTable.cs`; filtros/orden client-side con `ADGV`).
3. **Botón "Generar ATS - XML"** (`btnSaveXML_Click`) → `SaveXmlFile(path)` →
   `new XmlSerializer(typeof(ivaType)).Serialize(writer, ivaATSformat)`.
   **Sin plantilla, sin string-building: serialización directa del objeto.**
4. **Botón "Seleccionar Empresa"** (`btnSelectEmitter_Click`) → reabre
   `FrmEmpresaSeleccionar`; si cambió el RUC, limpia el formulario.
5. Botón "Ver errores." (`btnViewErrors`) — presente en el diseñador pero **sin
   handler** (muerto).

### 1.2 Pestañas de `ATSform`

| Pestaña | Contenido | Fuente en `ivaType` |
|---|---|---|
| **General** | cabecera (`IdInformante`, `numEstabRuc`, período, informante, total ventas) + grilla "ventas por establecimiento" (`adgvEstabSales`) | `ventasEstablecimiento` (`ventaEstType[]`) |
| **Compras** | grilla `adgvPurchases` (facturas 01 / NV 02 / liq 03) + botón "Retenciones" → `FrmAtsAirList` (detalle `detalleAir` de la fila) | `compras.Where(tipoComprobante != "04")` |
| **NC Compras** | grilla `adgvNcPurchases` | mismo filtro que Compras (bug del `.exe`: filtra `!= "04"`, debería ser `== "04"`) |
| **Ventas** | grilla `adgvSales` | `ventas.Where(tipoComprobante == "18")` |
| **NC Ventas** | grilla `adgvSalesNc` | `ventas.Where(tipoComprobante == "04")` |
| **Anulados Detalle** | grilla `adgvAnulados` | `anulados` (`detalleAnuladosType[]`) |
| **Resumen ATS** | 6 grillas de agregados: compras por `tipoComprobante`; ventas por `tipoComprobante`; retenciones en la fuente en compras (por `codRetAir`); retenciones de IVA en compras (buckets 10/20/30/50/70/100 %); retenciones recibidas en ventas (IVA/renta); conteo de anulados | agrupaciones LINQ en memoria |
| **Formulario 103** | grilla `adgvF103Config` + "Generar Form 103" (rellena xlsx) + "Generar JSON" + "Guardar Configuración" | `ReportForm103` |
| **Formulario 104** | grillas de cálculo (ventas/compras/retenciones) + "Generar Form 104" (rellena xlsx) + "Generar JSON" | `ReportForm104` + `Form104Params` |

### 1.3 Dónde se arma el XML

- **Esquema**: `ATSinScheme/Model/Esquema_at.cs` — clases generadas por
  `xsd.exe 4.6.1055` desde `Esquema_at.xsd` (el XSD oficial del ATS del SRI).
  Raíz `[XmlRoot("iva", Namespace="")]` `ivaType`. Tipos usados por el port:
  `detalleComprasType`, `detalleVentasType`, `ventaEstType`,
  `detalleAnuladosType`, `detalleAirComprasType : detalleAirType`,
  `pagoExteriorType` (+ enums `parteRelType`, `tipoEmisionType`,
  `pagoLocExtType`, `aplicConvDobTribType`, `tipoRegiType`). Tipos del esquema
  que el `.exe` **nunca llena**: `exportaciones`, `recap`, `fideicomisos`,
  `rendFinancieros`, `codigoOperativo`, `TipoIDInformante` (quedan default/null).
- **Orquestador**: `ATSModel/LoadATS.cs` (228 LOC).
  - ctor `LoadATS(RUC, periodo, mes)`: `new PeachCnn(RUC)` (→ cadena ODBC a Sage
    de esa empresa, desde `PeachConnString` + desencriptado de `pwd`); lee
    `Transmitter` (EF6) para `razonSocial`.
  - `LoadToATSobject(ref ivaType)`:
    - `IdInformante = RUC`; `razonSocial` = `Name` con `&`→`y`, se quitan `.` y
      `-`; `Anio`; `Mes` (`"00"`).
    - `new LoadSales(periodo, mes, cnn)` → `ivaATSobj.ventas`,
      `ivaATSobj.totalVentas`, `ivaATSobj.ventasEstablecimiento`.
    - `numEstabRuc`: `count(distinct establecimientos con venta).ToString("000")`,
      o `"001"` si no hay ninguno **(quirk: no es el nº real de establecimientos
      del RUC, sino los que facturaron ese mes)**.
    - `new LoadPurchases(periodo, mes, cnn)` → `ivaATSobj.compras`.
    - `new LoadCanceled(RUC, periodo, mes, cnn)` → `ivaATSobj.anulados`.
- **Lectores** (todo ODBC `SELECT` a Sage 50, SQL **interpolado** con
  `mes`/`periodo`/ids — hay que parametrizar con `?` al portar):

  | Clase | LOC | Qué hace |
  |---|---|---|
  | `ATSModel/LoadSales.cs` | 472 | Agrupa ventas por `CustVendId`+`JournalEx` (8 → factura tipo `18`, 9 → NC tipo `04`). Por cliente: `LoadCustomer` (id/tipo), `LoadBaseNoGraIva` (`SalesTaxType` 5/6), `LoadBaseImponible` (`<>0,5,6`), `LoadBaseImpGrav` (`=0`), `LoadMontoIVA` (`RowType=5`), retenciones recibidas (`Jobs.JobID` IRF/IVA con `PurchOrder='4R'`). `ventasEstablecimiento` = `SUM(Amount)` por `LEFT(Reference,3)`. `formasDePago` fijo `"20"`. Fusiona filas con mismo `(idCliente, tipoComprobante)`. |
  | `ATSModel/LoadPurchases.cs` | 828 | `JrnlKey_Journal=4`, `Reference LIKE '___-___-%'`, por mes/año. Por comprobante: `tipoComprobante` (01 factura / 02 NV / 03 liq / 04 NC) desde `JournalEx` (11 vs 12) + `ShipVia`; `codSustento` de `ShipVia` / `ShipToCity`; `LoadVendor` (id/tipo/`pagoExterior`); `LoadImponibles` (parsea `LineItem.Category` + `CustomField1..5` + `JrnlRow.Amount` → `baseImpGrav`/`baseImponible`/`baseImpExe`/`baseNoGraIva`/`montoIva` + 6 buckets `valRet*` de retención de IVA); `LoadRetencionesRF` (`detalleAir`: líneas `R-%RF` con `%` y valor + líneas de retención 0 % `332`/`332G` desde `CustomField5`); `SriAuthorization` (de `ShipToAddress1`, o item `AUT-SRI` en `JrnlRow`, o de la OC vinculada). NC → bloque `docModificado`. `formasDePago = ["20"]` si total ≥ 500 (`CommonConst.BankPaymentNonDeductubleLimitAmount`). `estabRetencion1`/`ptoEmiRetencion1`/`secRetencion1`/`autRetencion1` de `ShipToAddress2` + `ShipToCity`. |
  | `ATSModel/LoadCanceled.cs` | 321 | Vía `Services/SageODBCcnn.viewDataSet` (`DataSet`/`DataTable`, no reader). `Description LIKE 'ANULAD%' OR 'RETENCION ANULAD%'`, `Reference LIKE '___-___-%'`, por mes/año. Clasifica con los enums de `Sage50MetaData.SageJournalMetaData` (`JrnlKeyJournalType` / `JournalExType`) → `tipoComprobante` 18/04/03/07. Maneja liquidaciones y retenciones vinculadas por OC. `autorizacion` de item `AUT-SRI` o `"9999999999"`. |
  | `ATSModel/LoadVendor.cs` | 201 | ODBC `Vendors`+`Address`. Tipo de id vía `dicIdentityTypeATS` (`TransType=2`, mapea `OurAccountWithThem` → `IdAts`). Id tributario de `Address.Country` / `CustomField3` / `CustomField4` (pasaporte). Proveedor del exterior → arma `pagoExteriorType` desde `CustomField0..2` + `ExternalVendorInfo` (de `Sage50MetaData`). |
  | `ATSModel/LoadCustomer.cs` | 109 | ODBC `Customers`+`Address`. Id de `CustomField5` (pasaporte) / `CustomField4` (RUC) / `Country`. Tipo: `04` RUC / `05` CC / `06` exterior / `07` consumidor final (`9999999999999`). Nombre = `Customer_Bill_Name` + `CustomField1`. |
  | `ATSModel/QueryPersonType.cs` | 38 | 10 díg → CC, 13 díg → RUC, resto → exterior. **Ya portado** como `TiposIdentificacion.Deducir`. |

- **Helpers de conexión**:
  - `PeachModel/PeachCnn.cs` — RUC → cadena ODBC desde `PeachConnString`
    (arma `Driver;servername;uid;dbq` o `Dsn;Driver;uid`, desencripta `pwd`).
    **= `PsaWeb.PeachEbills/PeachConnStringResolver` (ya portado).**
  - `HashSecurity/PasswordSecurity.cs` — TripleDES/ECB, key `MD5(PwdKey)` con
    `PwdKey = "1oo2435681"`. **= `PsaWeb.PeachEbills/DbSecret` (ya portado).**
  - `Services/SageODBCcnn.cs` — `OdbcDataAdapter.Fill(DataSet)`. **→ reemplazar
    por `ISageConnectionFactory.CreateConnection(str)` + reader/adapter.**
- **Formularios 103 / 104 / 101** (EPPlus): abren un **xlsx en blanco que
  provee el usuario** (plantilla DIMM del SRI), recorren filas, hacen match de
  un código en una celda y escriben `Math.Abs(valor)` en la celda siguiente,
  `SaveAs`. `Form104Params` tiene el mapa de casilleros del 104 + config de
  totales. Los botones "Generar JSON" arman `{ "detallesDeclaracion": { … } }`
  usando filas de mapeo `ConfigJsonForms` de una **BD SQL Server aparte
  `PSContexts`** (LINQ-to-SQL `DataContext`; cadena en
  `Settings.ContextManagerSqlConnectionData`). El Formulario 101 es un flujo
  independiente (Excel → JSON) colgado del menú de `Form1`.

---

## 2. Lo que ya está y se reutiliza tal cual

| Pieza | Qué aporta al ATS |
|---|---|
| **Shell** (`/seleccionar-empresa`, `EmpresaSwitcher`, `EmpresaActualService`) | Reemplaza `FrmEmpresaSeleccionar` + `Settings.TransmitterRUC`. Empresa + ambiente por sesión. |
| **`PsaWeb.Sage50`** | `ISageConnectionFactory.CreateConnection(string)`; `IResolverEmpresaSage` / `HostResolverEmpresaSage` (RUC de sesión → cadena ODBC por empresa vía `PeachConnStringResolver`). Ya lo usan Cierre de Caja / Kardex. |
| **`PsaWeb.PeachEbills`** | `PeachEbillsContext` con `Transmitter`, `PeachConnString`, `Establishments`, `CurrentAmbient`. `DbSecret` (TripleDES). `PeachConnStringResolver`. `PeachEbillsOptions.SageServerNameOverride` (para dev en PREDATOR: `servername → localhost` sin mutar la fila). |
| **`PsaWeb.Comprobantes/Sri`** | `TiposIdentificacion` (= `QueryPersonType`), `NumeroDocumentoSri` (formato `est-pto-sec`), `EmailSri`. |
| **`PsaWeb.Comprobantes/Proveedores/LectorProveedor`** | Solapa parcialmente con `LoadVendor` (mismos quirks de `Vendors`/`Address`: id en `Address.Country`, tipo en `OurAccountWithThem`, nombre `Name`+`CustomField0`). **Extender, no bifurcar** (ver §3.3). |
| **Seguridad** | `Permisos.VerAts = "quats"` **ya está definido** en `PsaWeb.Seguridad/Permisos.cs`. `ISecurityDirectory.TienePermisoAsync`. `AppCatalogo` para dashboard/nav. |
| **Patrón de export + endpoint** | `app.MapGet("/…/export", …).RequireAuthorization()` que **re-consulta** y devuelve `Results.File(bytes, contentType, nombre)`. Acá el contentType es `application/xml` y el cuerpo sale de `XmlSerializer`, no de ClosedXML. |
| **Convenciones de página** | 4 estados (cargando / error / vacío / resultado), `es-EC`, `PsaModal`, `PsaPageHeader`, tabla con `overflow-x:auto`, reacción a `EmpresaActual.Cambio`. |

---

## 3. Lo que hay que construir

### 3.1 `PsaWeb.Ats` — esquema + escritor XML  *(proyecto nuevo)*

- `Esquema/` — **port verbatim** de `ATSinScheme/Model/Esquema_at.cs`. Es código
  generado por `xsd.exe` y **es el contrato con el SRI**: se copia tal cual
  (namespace `PsaWeb.Ats.Esquema`), sin rediseñar, conservando `[XmlRoot("iva")]`,
  los `[XmlElement(DataType="integer")]`, los `*Specified`, etc. `System.Xml.Serialization`
  existe en .NET 9 sin paquete extra.
- `EscritorXmlAts` — envuelve `XmlSerializer(typeof(ivaType))`:
  `StreamWriter` UTF-8, `XmlWriterSettings` que reproduzca **byte a byte** el
  output del `.exe` (indentación, `<?xml version="1.0"?>`, sin `xmlns:xsi`/`xsd`
  → usar `XmlSerializerNamespaces` vacío). Devuelve `byte[]`.
- Tests: serializar un `ivaType` de muestra y **diff contra un XML golden**
  producido por el `.exe` (F1 depende de tener ese golden — ver §6).

### 3.2 `PsaWeb.PeachEbills` — entidad nueva

- Scaffold EF Core de **`dicIdentityTypeATS`** (`Id`, `IdAts`, `ProofTypeId`,
  `TransType`) — la usan `LoadPurchases`/`LoadVendor` (`TransType == 2`).
  `DbSet` en un partial (`PeachEbillsContext.Ats.cs`), sin fluent.
  Todo lo demás que el ATS necesita ya está scaffoldeado.

### 3.3 `PsaWeb.Comprobantes` (o `PsaWeb.Ats`) — lectores

Cada lector: **interfaz + impl ODBC** (parámetros `?`, nunca interpolar) + impl
de **muestra** (PREDATOR no llega a la Sage multi-empresa de `SERWEBPSA01`; y hay
empresas locales para el camino real — §6). Port **fiel**: se replica lo que hace
el `.exe`, incluidos sus quirks conocidos (se documentan como `// Bug Bn` igual
que en el Kardex y FE).

- `Ventas/LectorVentasAts` — port de `LoadSales` + `LoadCustomer`. Parte pura
  `ArmarVentas` (fusión por `(idCliente, tipoComprobante)`, buckets por
  `SalesTaxType`, `formasDePago` fijo) testeable. `LectorVentasEstablecimientoAts`
  (el `SUM` por `LEFT(Reference,3)`).
- `Compras/LectorComprasAts` — port de `LoadPurchases` (con `LoadImponibles`,
  `LoadRetencionesRF`, `SriAuthorization`, `AirBaseImp`) + `LoadVendor`. **El
  bloque más grande (~1.000 LOC).** `LoadVendor` se resuelve **extendiendo
  `LectorProveedor`** con: (a) el mapeo por `dicIdentityTypeATS`, (b) el árbol
  `pagoExteriorType` (`ExternalVendorInfo`). Si extender complica, un
  `LectorProveedorAts` que reusa las queries de `LectorProveedor` y agrega lo del
  ATS.
- `Anulados/LectorAnuladosAts` — port de `LoadCanceled`. Requiere portar el
  mínimo de `Sage50MetaData.SageJournalMetaData` (los enums `JrnlKeyJournalType` /
  `JournalExType` + `IntTo…` y `ExternalVendorInfo`) a un helper chico en
  `PsaWeb.Comprobantes/Sri/` (son constantes de esquema de Sage, no SDK).
- `Ats/ArmadorAts` — port de `LoadATS.LoadToATSobject`: orquesta ventas +
  ventasEstablecimiento + compras + anulados + cabecera + `numEstabRuc` (con su
  quirk) → `ivaType`. `EmpresaEmisora` desde `Transmitter` (EF).

### 3.4 `modules/PsaWeb.Modules.Ats` — módulo + página  *(RCL nuevo)*

Patrón de `COMO-MIGRAR-UN-APLICATIVO.md`. **Una sola página `/ats`**, acotada a
la **empresa + año fiscal de la sesión**:

- Entradas: **año** (`txtPeriodo`) + **mes** (`01`–`12`). Validación cliente y
  servidor: `mes` elegido, `año <= año actual` (igual que el `.exe`).
- "Cargar" → `ArmadorAts` arma el `ivaType` **en memoria del circuito**
  (server-side; puede tardar → estado "cargando", quizás `IProgress`/spinner como
  el `frmWaitForm`).
- Pestañas de **solo lectura** replicando §1.2: General, Compras, NC Compras,
  Ventas, NC Ventas, Anulados Detalle, Resumen ATS. Grillas con filtros/orden
  client-side + `overflow-x:auto`; detalle de retenciones de una compra en
  `PsaModal` (reemplaza `FrmAtsAirList`).
- **"Generar ATS (XML)"** → `Nav.NavigateTo("/ats/export?anio=&mes=&ruc=", forceLoad:true)`.
  Endpoint en el Host: valida acceso a `ruc` (`EmpresasDelUsuarioAsync` → 403),
  re-arma el `ivaType`, `EscritorXmlAts` → `Results.File(xml, "application/xml",
  $"ATS_{ruc}_{anio}{mes}.xml")`. `RequireAuthorization`.
- **Formularios 103 / 104** → **fase posterior** (§4, F7). Si se incluyen: pestañas
  con subida del xlsx en blanco + descarga del relleno (ClosedXML, conservando el
  `Math.Abs`), y "Generar JSON". El Formulario 101 queda **fuera de alcance**.
- Gate: `Permisos.VerAts` (`"quats"`). **Confirmar que `quats` existe en
  `allowAction` de `PeachEBills`**; si no, `GateProvisional` (lista de permisos
  vacía en `AppCatalogo`) hasta que el área lo cargue — mismo patrón que
  `qupurchliq` / `quKardex`.
- Wiring (lo hace la implementación, no ahora): `AddAts` en `Program.cs` solo si
  están las cadenas; ensamblado en `Routes.razor` + `AddAdditionalAssemblies`;
  enlace en `NavMenu`; 1 `AppWeb` nuevo en `AppCatalogo` (`"ats"`, icono p. ej.
  `📑`, ruta `/ats`).

---

## 4. Fases

Mismo molde que app #1 / app #2 (`F1…Fn`, `F3` es el bloque pesado).

- **F1 — Esquema + escritor XML.** `PsaWeb.Ats` nuevo: `Esquema/` (port verbatim
  de `Esquema_at.cs`), `EscritorXmlAts` (`XmlSerializer`, ajustar `XmlWriterSettings`
  / `XmlSerializerNamespaces` para clonar el output del `.exe`). Scaffold de
  `dicIdentityTypeATS` en `PsaWeb.PeachEbills`. Tests: serializar un `ivaType`
  armado a mano y **diff contra XML golden** del `.exe` (§6). ~1–2 días.
- **F2 — Lector de ventas.** Port de `LoadSales` + `LoadCustomer` →
  `LectorVentasAts` (ODBC `?`) + `ArmarVentas` puro + `LectorVentasEstablecimientoAts`
  + impl de muestra. Tests de `ArmarVentas` (buckets por `SalesTaxType`, fusión
  por cliente, NC negativa, retenciones recibidas) y del golden de ventas.
  ~2 días.
- **F3 — Lector de compras.** Port de `LoadPurchases` (+ `LoadImponibles`,
  `LoadRetencionesRF`, `SriAuthorization`, `AirBaseImp`) + `LoadVendor`
  (extendiendo `LectorProveedor`: `dicIdentityTypeATS` + `pagoExteriorType`).
  El bloque grande. Impl de muestra. Tests de la parte pura (clasificación
  01/02/03/04, `codSustento`, buckets `valRet*`, `detalleAir` incl. líneas
  `332`/`332G`, `formasDePago` por límite 500, bloque `docModificado` de NC).
  ~3–4 días.
- **F4 — Anulados + armador.** Portar el mínimo de `Sage50MetaData`
  (`SageJournalMetaData` enums + `ExternalVendorInfo`) a `PsaWeb.Comprobantes/Sri`.
  Port de `LoadCanceled` → `LectorAnuladosAts`. `ArmadorAts` (= `LoadToATSobject`:
  cabecera + `numEstabRuc` con quirk + ventas + compras + anulados). **Golden
  completo** de una empresa/período real: `ivaType` web serializado ==
  XML del `.exe`. ~2–3 días.
- **F5 — Módulo + página + endpoint.** `modules/PsaWeb.Modules.Ats`, página
  `/ats` con las 7 pestañas de solo lectura + `PsaModal` de retenciones,
  "Generar ATS (XML)" → endpoint `/ats/export`, acotado a empresa + año de
  sesión, gate `quats` (`GateProvisional` si hace falta), `AppCatalogo` + nav.
  `Data/` con repos EF/ODBC + interruptor real/muestra por `Sage50:ConnectionString`.
  Smoke en navegador (login → empresa → `/ats` → Cargar → pestañas → descarga XML).
  ~2–3 días.
- **F6 — Deploy a `SERWEBPSA01` + validación.** Redeploy del Host con el módulo
  (`publish win-x86 self-contained`), env vars ya existentes. **Validación de
  paridad**: para 2–3 empresas × período cerrado, comparar el XML del web contra
  el del `.exe` y **cargar ambos en el DIMM del SRI** (valida contra el XSD).
  Revisión por un usuario del área. Sin nada "real" que enviar (el ATS es un
  archivo que sube el contador). ~1–2 días.
- **F7 — (posterior / decisión) Formularios 103 y 104.** Pestañas con subida del
  xlsx en blanco (plantilla DIMM) + descarga del relleno con **ClosedXML**
  (port de `ReportForm103`/`ReportForm104` + `Form104Params`, conservando
  `Math.Abs` y la config de totales) + botones "Generar JSON". Requiere resolver
  `ConfigJsonForms` / BD `PSContexts` (§5, decisión 3). **Formulario 101 fuera de
  alcance.** ~3–5 días si se hace.

**Estimación núcleo (F1–F6): ~11–16 días de dev**, concentrados en F3 (port fiel
de ~1.000 LOC de esquema Sage) y F4 (anulados + golden). F7 aparte.

---

## 5. Decisiones a confirmar

1. **Alcance del corte.** ¿Entra solo el **ATS (XML)** en este corte (F1–F6) y
   los **Formularios 103/104** quedan para F7 (u otra ola)? Propuesta: sí,
   separarlos — el 103/104 son "rellenar una plantilla Excel del DIMM", ortogonal
   al ATS y con su propia dependencia (`ConfigJsonForms`).
2. **Formulario 101** — flujo Excel→JSON del menú de `Form1`, no es una pestaña
   del ATS. Propuesta: **fuera de alcance** de la migración web (uso marginal).
   Confirmar.
3. **`ConfigJsonForms` / BD `PSContexts`** (solo si entra F7). El `.exe` lee el
   mapeo casillero→JSON de una BD SQL Server aparte (`PSContexts`, tablas
   `ConfigJsonForms` y `Context`). Opciones: (a) mover esa tabla a
   `PeachEBills` o a `PsaWebPlataforma` y sembrarla; (b) llevar el mapeo a un
   `appsettings`/JSON versionado; (c) dejar `PSContexts` como BD externa
   configurable. Propuesta: (b) si el mapeo es chico y estable.
4. **Permiso `quats`.** ¿Está la fila `quats` en `allowAction` de `PeachEBills`
   y asignada a los roles? Si no, se arranca con `GateProvisional` y el área la
   carga (como `qupurchliq` / `quKardex`).
5. **Quirks del `.exe` que se portan fieles** (confirmar que se replican, no se
   "arreglan"): pestaña **NC Compras** muestra el mismo filtro que Compras
   (`!= "04"`); `numEstabRuc` = nº de establecimientos **que facturaron** ese mes,
   no los del RUC; `razonSocial` con `&`→`y` y sin `.`/`-`; SQL con `"YEAR"()` /
   `"MONTH"()` de Pervasive; `TipoIDInformante` / `codigoOperativo` quedan
   vacíos. Si alguno **debe** corregirse por normativa del SRI, decirlo ahora.
6. **Multi-establecimiento.** El `.exe` scopea todo por RUC (un `Transmitter`).
   ¿El módulo web queda **solo empresa de sesión**, sin cross-company ni worker
   (igual que Kardex / Cierre de Caja)? Propuesta: sí.
7. **Botón "Ver errores"** del `.exe` está muerto (sin handler). ¿Se omite?
   Propuesta: sí; los `Mistake`/errores de lectura se muestran inline por fila
   como en FE.
8. **Fixture** (§6): qué empresa + período usar como golden, y quién corre el
   `.exe` para producir el XML de referencia (PREDATOR no está en la red
   `192.168.0.x`).

---

## 6. Fixture de prueba

El ATS necesita una empresa con, en un **mes cerrado**: compras (factura + NV +
liquidación + NC), ventas (factura + NC), retenciones en la fuente y de IVA en
compras, retenciones recibidas en ventas, y algún comprobante **anulado**. Cuanto
más completo, mejor cubre el golden.

- RUC por defecto del `.exe`: **`1791741951001`** (en `Settings.TransmitterRUC`).
- Empresas ya verificadas en PREDATOR con Sage local + fila `PeachConnString`
  (de los planes de Kardex y FE): **CPTDC `1792051800001`**
  (`dbq=cptdcecuadorsa202520`), **SANCEV `1791313747001`**
  (`dbq=sancevcialtda2202520`), **Roller Dance `1793198281001`**
  (`dbq=rollerdancee202526`). Todas requieren
  `PeachEbills:SageServerNameOverride=localhost` en dev (la fila apunta a
  `SERWEBPSA01`).
- **Pendiente del área antes de F1**: elegir empresa + período con datos ATS
  completos y **generar el XML de referencia con el `.exe`** (para el diff de
  F1/F4). Si ninguna empresa local tiene un mes ATS completo, cargar comprobantes
  ficticios (como se hizo para FE en `sancialu`).
- `lparedes` (login web de dev) debe estar en `UserTransmitter` para el RUC
  elegido; si no, script SQL `docs/sql/lparedes-empresas.sql` (mismo patrón que
  FE / Kardex). Correrlo también en el server.
- Probes ODBC 32-bit: reutilizar el patrón de
  `scratchpad/kardex-cptdc-probe*.ps1` (correr con
  `C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`).

**F1 depende del golden XML. F2/F3 dependen de datos Sage locales de la empresa
elegida.**

---

## 7. Notas de arquitectura

- **x86**: solo el Host (ya lo es), por el driver Pervasive de 32 bits. `PsaWeb.Ats`
  y el módulo quedan AnyCPU.
- **Sin lógica nueva**: se replica lo que hace el `.exe`. El XSD del SRI es el
  contrato — el esquema se copia generado, no se re-modela.
- **Sin credenciales en git**: cadenas vacías en `appsettings.Development.json`;
  reales en User Secrets / env vars del sitio.
- El `.exe` no tiene build ni CI; su "verdad" es el código fuente y el XML que
  produce. Toda validación es diff contra su salida + el validador del DIMM.

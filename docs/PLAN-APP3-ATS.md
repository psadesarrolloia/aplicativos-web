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
      `-`; `Anio`; `Mes` (`"00"`). **Esto no es cosmético: el XSD oficial
      (`razonSocialType`) solo permite `[a-zA-Z0-9\s]` — sin el reemplazo el XML
      no valida.** Se porta tal cual.
    - `new LoadSales(periodo, mes, cnn)` → `ivaATSobj.ventas`,
      `ivaATSobj.totalVentas`, `ivaATSobj.ventasEstablecimiento`.
    - `numEstabRuc`: `count(distinct establecimientos con venta).ToString("000")`,
      o `"001"` si no hay ninguno. **Confirmado como bug real contra la
      normativa del SRI, no un quirk a preservar — ver "Hallazgos contra la
      normativa" más abajo y decisión 5.**
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

### 1.4 Hallazgos contra la normativa real del SRI (investigado 2026-09-12)

`C:\SRI-DIMM\` (el DIMM oficial, instalado en esta máquina) trae en
`Documentacion\` el XSD oficial (`ats.xsd`, idéntico al `at_jun_2020_en_adelante.xsd`
que el propio DIMM usa internamente — el esquema del `.exe` sigue vigente) más la
**"Ficha Técnica Transaccional Simplificado ATS"** (especificación campo por
campo + reglas de negocio) y el manual del DIMM. Además, el propio validador del
DIMM (`Dimm\plugins\ec.gob.sri.dimm.ats.validacion_1.2.0.jar`, que envuelve
`rig-ats-validacion`) tiene clases Java con los nombres y mensajes reales de cada
regla. Cruzando eso contra el código del `.exe` aparecen **2 desvíos reales que
conviene corregir en el port, no replicar**, y 1 bug cosmético:

1. **`numEstabRuc` / `ventasEstablecimiento` — bug real.** La Ficha Técnica es
   explícita: *"Número de establecimientos del sujeto pasivo inscritos en el
   RUC... campo obligatorio y debe ser mayor a 000"* y *"[ventasEstablecimiento]
   debe generarse igual número de registros que el valor informado en
   `numEstabRuc`... la sumatoria del total de ventas por los establecimientos no
   puede ser mayor al valor registrado en el campo total ventas"*. El propio
   validador del DIMM (`VentaYVentaEstablecimientoValidador.class`) hace
   exactamente ese cruce y rechaza si no cuadra. El `.exe` en cambio calcula
   `numEstabRuc` = **cantidad de establecimientos que facturaron ese mes** (no
   los activos del RUC), y solo emite un `ventaEstType` por cada uno de esos.
   Si un establecimiento activo no facturó ese mes, **falta su registro en
   `ventasEstablecimiento`** y `numEstabRuc` queda subestimado. **Fix
   propuesto**: usar `Establishments` (ya scaffoldeada en `PsaWeb.PeachEbills`,
   filtro `IsFromPeach`/activo) como fuente de verdad de `numEstabRuc`, y emitir
   un `ventaEstType` por cada establecimiento activo (con `ventasEstab = 0` si
   no facturó).
2. **"Forma de pago" en ventas — bug real.** La Ficha Técnica: el campo es
   condicional — *"a partir del 20 de diciembre de 2023 se genera cuando la
   sumatoria de bases imponibles y montos de impuestos es mayor a USD 500,00...
   para períodos anteriores... mayor a USD 1000,00"* y **no aplica a notas de
   crédito** (confirmado también por el mensaje literal del validador:
   `"Las formas de pago no aplican para notas de crédito"`). El `.exe`:
   - en **Compras** (`LoadPurchases`) sí evalúa el umbral, pero lo tiene fijo en
     500 (`CommonConst.BankPaymentNonDeductubleLimitAmount`) sin importar la
     fecha — correcto solo para períodos ≥ dic-2023.
   - en **Ventas** (`LoadSales.LoadValuesInvoice`) **no evalúa ningún umbral**:
     fija `formasDePago = ["20"]` en toda factura, sin importar el monto (por
     suerte sí lo omite en NC, que es lo correcto). **Fix propuesto**: aplicar
     el mismo umbral por fecha (500 desde 20-dic-2023, 1000 antes) en ventas y
     compras.
   - Nota menor: `valRetBien10`/`valRetServ20` (retención IVA bienes 10 %/20 %)
     la normativa los admite recién desde 06/2015 — irrelevante si solo se
     declaran períodos corrientes; solo importa si se generan ATS de meses muy
     atrasados.
3. **Pestaña "NC Compras" — bug cosmético, no afecta el XML.** `ATSform.cs`
   carga esa grilla con el mismo filtro que "Compras"
   (`.Where(tipoComprobante != "04")`) en vez de `== "04"` — es un error de
   copy-paste en la UI de escritorio. El array `compras` del XML (que sí
   incluye las NC con `tipoComprobante="04"`) no se ve afectado. **Fix
   trivial** al portar la pestaña: usar el filtro correcto.

Estos 3 hallazgos están reflejados en la decisión 5 (§5) y en las fases F3/F4/F5
(§4).

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
- **Formularios 103 / 104** → **descartados de este corte** (decisión 1/3, §5).
  El Formulario 101 queda **fuera de alcance** (decisión 2).
- Gate: `Permisos.VerAts` (`"quats"`). **Verificado en la copia local de
  `PeachEBills` (2026-09-12): la fila `quats` (aid `6`, "Ver ATS") ya existe en
  `allowAction`, pero tiene 0 filas en `adrAllowRol` — no está asignada a
  ningún rol.** Arranca con `GateProvisional` (lista de permisos vacía en
  `AppCatalogo`, mismo patrón que `qupurchliq` / `quKardex`) hasta que el área
  la asigne a los roles pertinentes (p. ej. el rol `6` "Hacer Comprobantes
  Electrónicos", que ya usa CPTDC — ver decisión 4 y fixture §6).
- Wiring (lo hace la implementación, no ahora): `AddAts` en `Program.cs` solo si
  están las cadenas; ensamblado en `Routes.razor` + `AddAdditionalAssemblies`;
  enlace en `NavMenu`; 1 `AppWeb` nuevo en `AppCatalogo` (`"ats"`, icono p. ej.
  `📑`, ruta `/ats`).

### 3.5 "Revisar ATS" — reemplazo del botón muerto "Ver errores"

El usuario confirmó la intención original: una revisión **tipo DIMM oficial**
antes de generar el XML. El DIMM real (`ec.gob.sri.dimm.ats.validacion` →
`rig-ats-validacion`) trae validadores dedicados por bloque —
`ValidacionCompras`, `ValidacionVentas`, `ValidacionVentasEstablecimiento` +
`VentaYVentaEstablecimientoValidador` (cruce ventas↔establecimientos),
`ValidacionAnulados`, `ValidacionRetencionesCompras`,
`ValidadorMontosDeRetencionIVA`, `FormaPagoValidador`,
`TipoIdentificacionProveedorValidador`, `PagoExteriorValidador` /
`CompraExteriorValidador`, `ValidacionDuplicadosAts` (comprobantes duplicados),
`RangosNumericos` (tolerancias de redondeo). Con eso como base, `PsaWeb.Ats`
suma un `ValidadorAts` (parte pura, testeable) que corre **sobre el `ivaType` ya
armado**, antes de habilitar "Generar ATS (XML)", y devuelve una lista de
hallazgos (bloqueantes vs. advertencias):

- **Estructurales** (derivados del propio `ats.xsd`): longitudes/patrones por
  campo (p. ej. `razonSocialType`, RUC 13 dígitos, fechas `dd/mm/aaaa`), campos
  obligatorios sin valor.
- **Cruce ventas ↔ establecimientos** (el hallazgo 1 de §1.4): nº de
  `ventasEstablecimiento` == `numEstabRuc`, `sum(ventasEstab) <= totalVentas`.
- **Forma de pago** (hallazgo 2 de §1.4): presente cuando corresponde por
  umbral/fecha, ausente en notas de crédito.
- **Compras**: al menos una de `baseImpExe`/`baseImponible`/`baseImpGrav` > 0
  por línea (regla literal de la Ficha Técnica); suma de retenciones de IVA por
  compra ≤ `montoIva` (mensaje real del validador: *"La sumatoria de los
  valores por concepto de retenciones... es mayor al valor de MONTO IVA"*).
  fechas: registro ≥ emisión, dentro del período, posteriores a 01/01/2002.
- **Duplicados**: mismo `(establecimiento, puntoEmision, secuencial, idProv)`
  repetido en compras/ventas.

Se muestra como panel de hallazgos en la página (no bloquea la exploración de
pestañas, pero si hay bloqueantes se advierte antes de exportar). Se desarrolla
junto con F5 (o F5.5 si el volumen de reglas lo amerita) — ver §4.

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
  El bloque grande. Impl de muestra. **Aplica el fix 2 de §1.4**: forma de pago
  por umbral y fecha (500 desde 20-dic-2023, 1000 antes), no un límite fijo.
  Tests de la parte pura (clasificación 01/02/03/04, `codSustento`, buckets
  `valRet*`, `detalleAir` incl. líneas `332`/`332G`, forma de pago por
  umbral/fecha, bloque `docModificado` de NC). ~3–4 días.
- **F4 — Anulados + armador.** Portar el mínimo de `Sage50MetaData`
  (`SageJournalMetaData` enums + `ExternalVendorInfo`) a `PsaWeb.Comprobantes/Sri`.
  Port de `LoadCanceled` → `LectorAnuladosAts`. `ArmadorAts` (= `LoadToATSobject`:
  cabecera + ventas + compras + anulados). **Aplica el fix 1 de §1.4**:
  `numEstabRuc` y `ventasEstablecimiento` desde `Establishments` (activos del
  RUC), no desde "los que facturaron". **Golden completo** de una empresa/
  período real: `ivaType` web serializado == XML del `.exe` **salvo en los 2
  puntos corregidos** (documentar el diff esperado). ~2–3 días.
- **F5 — Módulo + página + endpoint.** `modules/PsaWeb.Modules.Ats`, página
  `/ats` con las 7 pestañas de solo lectura (con el fix cosmético 3 de §1.4 en
  "NC Compras") + `PsaModal` de retenciones, "Generar ATS (XML)" → endpoint
  `/ats/export`, acotado a empresa + año de sesión, gate `quats`
  (`GateProvisional`), `AppCatalogo` + nav. `Data/` con repos EF/ODBC +
  interruptor real/muestra por `Sage50:ConnectionString`. Smoke en navegador
  (login → empresa → `/ats` → Cargar → pestañas → descarga XML). ~2–3 días.
- **F5.5 — "Revisar ATS" (§3.5).** `ValidadorAts` (puro, testeable) + panel de
  hallazgos en la página, reemplazando el botón muerto "Ver errores". Tests por
  regla (estructurales, cruce ventas↔establecimientos, forma de pago,
  retenciones de IVA vs. `montoIva`, duplicados). ~2–3 días.
- **F6 — Deploy a `SERWEBPSA01` + validación.** Redeploy del Host con el módulo
  (`publish win-x86 self-contained`), env vars ya existentes. **Validación de
  paridad**: para 2–3 empresas × período cerrado, comparar el XML del web contra
  el del `.exe` (esperando el diff de los 2 fixes) y **cargar ambos en el DIMM
  del SRI** (valida contra el XSD + su propio validador de negocio — buena
  doble confirmación de que los fixes son correctos). Revisión por un usuario
  del área. Sin nada "real" que enviar (el ATS es un archivo que sube el
  contador). ~1–2 días.
- **F7 — descartado de este corte** (decisión 1/3, §5). Formularios 103 y 104
  (relleno de plantilla xlsx + "Generar JSON" vía `ConfigJsonForms`/`PSContexts`)
  quedan fuera por ahora; se retoman si se decide más adelante.

**Estimación núcleo (F1–F6): ~13–19 días de dev**, concentrados en F3 (port fiel
de ~1.000 LOC de esquema Sage + fix de forma de pago) y F4 (fix de
establecimientos + golden).

---

## 5. Decisiones (confirmadas con el usuario 2026-09-12)

1. ✅ **Alcance del corte**: solo el **ATS (XML)**, F1–F6. Formularios 103/104
   quedan fuera por ahora (ver 3).
2. ✅ **Formulario 101** fuera de alcance de la migración web.
3. ✅ **F7 (Formularios 103/104) se deja fuera por el momento** — no se planifica
   más allá de dejarlo anotado como posible trabajo futuro (§4, F7).
4. ✅ **Permiso `quats`** — investigado en la copia local de `PeachEBills`
   (`PREDATOR\SQLEXPRESS`, restaurada de `SERWEBPSA01`): la fila **`quats` (aid
   `6`, "Ver ATS") ya existe en `allowAction`**, pero **no tiene ninguna fila en
   `adrAllowRol`** (0 roles con ese permiso asignado) — mismo estado en el que
   empezaron `qupurchliq`/`quKardex`. Se arranca con `GateProvisional`; el área
   deberá asignar `quats` a los roles pertinentes cuando corresponda (candidato
   natural: el rol `6` "Hacer Comprobantes Electrónicos", que ya usa la empresa
   fixture CPTDC — ver 8).
5. ✅ **Investigado contra la normativa real** (Ficha Técnica ATS del SRI +
   validador del DIMM instalado — detalle en §1.4). Resultado: **hay 2
   correcciones a aplicar** (no son quirks a preservar):
   - `numEstabRuc` / `ventasEstablecimiento` deben reflejar los establecimientos
     **activos del RUC** (vía `Establishments`), no los que facturaron ese mes.
   - "Forma de pago" en **ventas** debe respetar el mismo umbral por fecha que
     ya aplica (parcialmente) en compras (500 desde 20-dic-2023, 1000 antes),
     en vez de fijarse siempre en "20".
   Además 1 bug cosmético sin impacto en el XML: la pestaña "NC Compras" del
   `.exe` filtra igual que "Compras" — se corrige al portar la grilla.
   `razonSocial` con `&`→`y` y sin `.`/`-` **no es un bug**: el XSD lo exige.
   Se documentan los 3 puntos en F3/F4/F5 (§4).
6. ✅ **Multi-establecimiento**: el módulo web queda **solo empresa de sesión**,
   sin cross-company ni worker.
7. ✅ **Botón "Ver errores"**: no se omite — se reemplaza por una función real
   **"Revisar ATS"** inspirada en el validador oficial del DIMM (que es lo que
   el botón buscaba emular). Diseño en §3.5, fase F5.5.
8. ✅ **Fixture**: **CPTDC (RUC `1792051800001`)**. Ya verificada localmente en
   PREDATOR (Sage local + `PeachConnString` + `Establishments`, del plan de
   Kardex) y **`lparedes` ya tiene acceso** (rol `6` "Hacer Comprobantes
   Electrónicos" vía `UserTransmitter`/`udrUserRolesTr`, confirmado por
   consulta directa) — no hace falta script SQL de acceso. Falta: confirmar que
   CPTDC tenga un mes con datos ATS completos (compras+ventas+retenciones+
   anulados) o cargar comprobantes ficticios, y **generar con el `.exe` el XML
   de referencia** de ese mismo período para el golden de F1/F4 (§6).

---

## 6. Fixture de prueba — CPTDC (RUC `1792051800001`)

El ATS necesita una empresa con, en un **mes cerrado**: compras (factura + NV +
liquidación + NC), ventas (factura + NC), retenciones en la fuente y de IVA en
compras, retenciones recibidas en ventas, y algún comprobante **anulado**. Cuanto
más completo, mejor cubre el golden.

- **Elegida: CPTDC CHINA PETROLEUM TECHNOLOGY & DEVELOPMENT COR., RUC
  `1792051800001`** — ya usada como fixture del Kardex, con Sage local en
  PREDATOR (`dbq=cptdcecuadorsa202520`, DBQ Zen `CPTDCECUADORSA202520`,
  `servername` local vía `PeachEbills:SageServerNameOverride=localhost`) y
  `PeachConnString`/`Establishments` verificados (`001-001/002/003`).
- **Acceso ya resuelto**: `lparedes` (login web de dev) **ya está** en
  `UserTransmitter` + `udrUserRolesTr` para este RUC (rol `6`, "Hacer
  Comprobantes Electrónicos") — confirmado por consulta directa 2026-09-12, no
  hace falta script SQL de acceso (a diferencia de SANCEV/Roller Dance/DGRV en
  el plan de FE).
- **Pendiente del área antes de F1**: confirmar que CPTDC tenga, en algún mes
  cerrado, datos ATS completos (compras + ventas + retenciones + anulados); si
  no, cargar comprobantes ficticios en la Sage local `cptdc...` (como se hizo
  para FE en `sancialu`). Luego **generar con el `.exe` el XML de referencia**
  de ese mismo RUC+período — es el golden de F1/F4.
- Probes ODBC 32-bit: reutilizar el patrón de
  `scratchpad/kardex-cptdc-probe*.ps1` (correr con
  `C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`).

**F1 depende del golden XML. F2/F3 dependen de datos Sage locales de CPTDC.**

---

## 7. Notas de arquitectura

- **x86**: solo el Host (ya lo es), por el driver Pervasive de 32 bits. `PsaWeb.Ats`
  y el módulo quedan AnyCPU.
- **Salvo los 2 fixes de §1.4, sin lógica nueva**: se replica lo que hace el
  `.exe`. El XSD del SRI es el contrato — el esquema se copia generado, no se
  re-modela.
- **Sin credenciales en git**: cadenas vacías en `appsettings.Development.json`;
  reales en User Secrets / env vars del sitio.
- El `.exe` no tiene build ni CI; su "verdad" es el código fuente y el XML que
  produce, contrastado contra la normativa oficial disponible en esta máquina:
  `C:\SRI-DIMM\Documentacion\ats.xsd` (esquema vigente, idéntico al que usa el
  DIMM instalado) + `Ficha Tecnica Transaccional Simplificado ATS.pdf` (reglas
  de negocio) + el propio validador del DIMM
  (`Dimm\plugins\ec.gob.sri.dimm.ats.validacion_1.2.0.jar`). Toda validación de
  F6 es diff contra la salida del `.exe` (esperando el diff de los 2 fixes) +
  carga en el DIMM real.

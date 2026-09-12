# Plan — App #3: ATSfromPeach → módulo web

Ola 1, aplicativo #3. Piloto (Cierre de Caja), app #1 (`AutomaticTwhSender` →
`/retenciones`), app #2 (`Sage50FacturacionElectronica` → `/fe/*`) y el
**Kardex de inventarios** (`/kardex`) ya están en `main`. Este documento
planifica el port de **`ATSfromPeach`** ("Generar ATS"), listo para arrancar
la implementación (el Kardex, que compartía el wiring, ya cerró).

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
  `rendFinancieros` (no aplican a este `.exe`). `codigoOperativo` y
  `TipoIDInformante` tampoco se asignan explícitamente, pero **no quedan
  vacíos**: ambos enums (`codigoOperativoType`, `ivaTypeTipoIDInformante`)
  tienen un único valor posible (`IVA` y `R` respectivamente) en la posición 0,
  que es el default de C# — coincide exacto con el XML real de CPTDC (§6). No
  es un bug, solo una asignación implícita.
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
regla. Cruzando eso contra el código del `.exe` aparecen 4 hallazgos —
detallados abajo, y resumidos en la decisión 5 (§5): 1 bug real a corregir
(`numEstabRuc`), 1 desvío real que se decide **no** corregir (forma de pago),
1 bug cosmético sin impacto en el XML (pestaña "NC Compras"), y 1 gap de
esquema (`valorRetencionNc`):

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
2. **"Forma de pago" en ventas — desvío real, pero se deja como está (decisión
   del usuario 2026-09-12).** La Ficha Técnica dice que el campo es condicional
   por fecha/umbral y que no aplica a notas de crédito. El `.exe` en **Compras**
   sí evalúa un umbral (fijo en 500, sin variar por fecha) y en **Ventas**
   (`LoadSales.LoadValuesInvoice`) **no evalúa ninguno**: fija
   `formasDePago = ["20"]` en toda factura sin importar el monto (y sí lo omite
   correctamente en NC). Evaluado con el usuario: **se mantiene el código `"20"`
   fijo en ventas, sin condicionarlo a fecha/monto** — volverlo condicional
   introduce una regla más para mantener y un riesgo de error humano/de cálculo
   mayor al beneficio de precisión marginal que da. Por consistencia, **en
   compras tampoco se le agrega la variante por fecha** propuesta originalmente:
   se porta el umbral fijo del `.exe` (500) tal cual. Si en el futuro el SRI
   objeta esto en una revisión real, se revisita.
   - Nota menor (no accionable ahora): `valRetBien10`/`valRetServ20` (retención
     IVA bienes 10 %/20 %) la normativa los admite recién desde 06/2015 —
     irrelevante si solo se declaran períodos corrientes.
3. **Pestaña "NC Compras" — bug cosmético, no afecta el XML.** `ATSform.cs`
   carga esa grilla con el mismo filtro que "Compras"
   (`.Where(tipoComprobante != "04")`) en vez de `== "04"` — es un error de
   copy-paste en la UI de escritorio. El array `compras` del XML (que sí
   incluye las NC con `tipoComprobante="04"`) no se ve afectado. **Fix
   trivial** al portar la pestaña: usar el filtro correcto.
4. **El esquema `Esquema_at.cs` está desactualizado — no copiar tal cual.**
   Diffeando los nodos del XML real de CPTDC julio/2026 (§6) contra las
   propiedades de `detalleComprasType` aparece **`valorRetencionNc`** (`tipo
   moneda`, `minOccurs="0"`), documentado en `ats.xsd` como *"Nuevo campo 2020
   de valor de retención en notas de crédito 100%"* — **no existe en
   `Esquema_at.cs`** (el `.exe` nunca lo generó porque su copia del esquema es
   de antes de esa fecha). Es opcional en el XSD, así que el XML del `.exe`
   sigue validando sin él, pero el golden real SÍ lo trae (con `0.00`) en cada
   `detalleCompras`. **F1 no debe copiar `Esquema_at.cs` a ciegas**: hay que
   regenerarlo/auditarlo contra `C:\SRI-DIMM\Documentacion\ats.xsd` (el vigente)
   y al menos agregar `valorRetencionNc` (aunque el port no lo calcule todavía
   — emitirlo en `0.00`, que es lo que hace el propio DIMM).

Estos 4 hallazgos están reflejados en la decisión 5 (§5) y en las fases F1/F3/F4/F5
(§4). Los ejemplos reales para contrastar todo esto ya están disponibles — ver
§6 (fixture).

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

- `Esquema/` — **regenerar con `xsd.exe` (o equivalente) contra el XSD vigente
  `C:\SRI-DIMM\Documentacion\ats.xsd`**, no copiar `ATSinScheme/Model/Esquema_at.cs`
  a ciegas (hallazgo 4 de §1.4: le falta `valorRetencionNc`). El resultado sigue
  siendo código generado — **es el contrato con el SRI**, no se rediseña a mano
  (namespace `PsaWeb.Ats.Esquema`, conservando `[XmlRoot("iva")]`, los
  `[XmlElement(DataType="integer")]`, los `*Specified`, etc.). Cotejar además
  contra los nodos reales del golden de CPTDC (§6) por si hay algún otro campo
  nuevo que el diff no haya capturado. `System.Xml.Serialization` existe en
  .NET 9 sin paquete extra.
- `EscritorXmlAts` — envuelve `XmlSerializer(typeof(ivaType))`:
  `StreamWriter` UTF-8, `XmlWriterSettings` que reproduzca el output real
  (`<?xml version="1.0" encoding="UTF-8" standalone="no"?>`, sin `xmlns:xsi`/`xsd`
  → usar `XmlSerializerNamespaces` vacío). Devuelve `byte[]`.
- Tests: serializar un `ivaType` de muestra y **comparar contra el XML golden**
  real de CPTDC julio/2026 (§6) — comparación **estructural y numérica**, no
  byte-a-byte: el golden puede venir ya renormalizado por el propio DIMM (su
  cabecera `standalone="no"` no es la que produce por defecto un
  `XmlSerializer` crudo), así que el objetivo es que cargue igual en el DIMM,
  no un diff textual exacto.

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
- **"Revisar ATS"** (§3.5) corre las validaciones sobre el `ivaType` en memoria.
- **Dos salidas finales** (habilitadas tras revisar, ver §3.5): **"Generar ATS
  (XML)"** → `Nav.NavigateTo("/ats/export?anio=&mes=&ruc=", forceLoad:true)`.
  Endpoint en el Host: valida acceso a `ruc` (`EmpresasDelUsuarioAsync` → 403),
  re-arma el `ivaType`, `EscritorXmlAts` → `Results.File(xml, "application/xml",
  $"ATS_{ruc}_{anio}{mes}.xml")`. Es el archivo que luego se sube al DIMM real
  y, ya validado ahí, a la declaración del SRI. Y **"Descargar Talón Resumen
  (PDF)"** → mismo patrón de endpoint, `Results.File(pdf, "application/pdf",
  $"TRSMN-ATS-{mes}-{anio}-{ruc}.pdf")`. `RequireAuthorization` en ambos.
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

El usuario confirmó la intención original: una revisión **tipo DIMM oficial**,
y que el flujo termine en **dos entregables**: el **Talón Resumen en PDF** que
el DIMM emite, y el **XML** (que después se sube al DIMM real y, ya validado
ahí, a la declaración). Ambos ya se pueden diseñar con precisión porque el
usuario dejó en `C:\SRI-DIMM\Documentacion\` los dos ejemplos reales de la
declaración de CPTDC de julio/2026 (misma información que hoy está en el Sage
de PREDATOR — ver §6): `ATS CPTDC JULIO  2026.xml` y
`TRSMN-ATS-07-2026-CPTDC.pdf`.

#### a) Validaciones ("Revisar ATS")

El DIMM real (`ec.gob.sri.dimm.ats.validacion` → `rig-ats-validacion`) trae
validadores dedicados por bloque — `ValidacionCompras`, `ValidacionVentas`,
`ValidacionVentasEstablecimiento` + `VentaYVentaEstablecimientoValidador`
(cruce ventas↔establecimientos), `ValidacionAnulados`,
`ValidacionRetencionesCompras`, `ValidadorMontosDeRetencionIVA`,
`FormaPagoValidador`, `TipoIdentificacionProveedorValidador`,
`PagoExteriorValidador`/`CompraExteriorValidador`, `ValidacionDuplicadosAts`
(comprobantes duplicados), `RangosNumericos` (tolerancias de redondeo). Con eso
como base, `PsaWeb.Ats` suma un `ValidadorAts` (parte pura, testeable) que
corre **sobre el `ivaType` ya armado**, antes de habilitar las dos descargas, y
devuelve una lista de hallazgos (bloqueantes vs. advertencias):

- **Estructurales** (derivados del propio `ats.xsd`): longitudes/patrones por
  campo (p. ej. `razonSocialType`, RUC 13 dígitos, fechas `dd/mm/aaaa`), campos
  obligatorios sin valor.
- **Cruce ventas ↔ establecimientos** (hallazgo 1 de §1.4): nº de
  `ventasEstablecimiento` == `numEstabRuc`, `sum(ventasEstab) <= totalVentas`.
- **Compras**: al menos una de `baseImpExe`/`baseImponible`/`baseImpGrav` > 0
  por línea (regla literal de la Ficha Técnica); suma de retenciones de IVA por
  compra ≤ `montoIva` (mensaje real del validador: *"La sumatoria de los
  valores por concepto de retenciones... es mayor al valor de MONTO IVA"*).
  Fechas: registro ≥ emisión, dentro del período, posteriores a 01/01/2002.
- **Duplicados**: mismo `(establecimiento, puntoEmision, secuencial, idProv)`
  repetido en compras/ventas.
- No se valida "forma de pago por umbral/fecha" (decisión 2026-09-12, hallazgo
  2 de §1.4: queda fija en `"20"`, no es una regla a chequear).

Se muestra como panel de hallazgos en la página (no bloquea la exploración de
pestañas, pero si hay bloqueantes se advierte antes de exportar).

#### b) Talón Resumen (PDF)

El propio jar del validador del DIMM trae, en `resources/`, la plantilla HTML +
CSS + logo con la que arma este PDF: `templateATS2.html` / `talonAts.css` /
`sriTalonLogo.jpg` (más `IVA_talonResumen.properties` con los mismos bloques
parametrizados por marcadores `#CODIGO#`/`#TRANSACCION#`/etc., usados por la
variante RTF/HTML del generador `GeneraTalonAtsDimm`). Contrastado contra
`TRSMN-ATS-07-2026-CPTDC.pdf` (el ejemplo real), el talón tiene:

1. Cabecera: logo, "TALÓN RESUMEN / SERVICIO DE RENTAS INTERNAS / ANEXO
   TRANSACCIONAL", razón social, RUC, período (`MM-AAAA`), fecha de generación.
2. Párrafo de certificación ("Certifico que la información... es fiel reflejo
   del siguiente reporte:").
3. Tabla **COMPRAS**: Cod. | Transacción | Nº Registros | BI tarifa 0% | BI
   tarifa diferente 0% | BI No Objeto IVA | Valor IVA — una fila por
   `tipoComprobante` (01 Factura, 02 Nota de Venta, 03 Liquidación de compra,
   04 Notas de crédito) + fila TOTAL.
4. Tabla **VENTAS**: mismas columnas, fila "18 DOCUMENTOS AUTORIZADOS EN VENTAS
   EXCEPTO ND Y NC" + TOTAL.
5. **COMPROBANTES ANULADOS**: conteo total del período.
6. **RESUMEN DE RETENCIONES - AGENTE DE RETENCIÓN**:
   - Tabla "RETENCIÓN EN LA FUENTE DE IMPUESTO A LA RENTA": Cod. | Concepto |
     Nº Registros | Base Imponible | Valor Retenido, una fila por `codRetAir`
     (303A, 304B, 310, 312, 320, 332, 332G, 332I, 343, 3440, 501A, …) + TOTAL.
   - Tabla "RETENCIÓN EN LA FUENTE DE IVA": Operación=COMPRA + los 6 conceptos
     (10/20/30/50/70/100 %) + TOTAL.
7. "RESUMEN DE RETENCIONES QUE LE EFECTUARON EN EL PERIODO": Operación=VENTA +
   "Valor de IVA que le han retenido" / "Valor de Renta que le han retenido" +
   TOTAL.
8. Texto legal (Art. 101 de la Ley de Régimen Tributario Interno) + líneas de
   firma "Firma del Contador" / "Firma del Representante Legal".

**Todo esto ya está calculado** por las agregaciones de la pestaña "Resumen
ATS" (§1.2) que el port ya planifica en F2–F4 (`purchasesSummary`/
`salesSummary`/`purchasesTwhRFSummary`/`purchaseTwhIvaSummary`/
`salesTwhSummary`/conteo de anulados) — el Talón es, en esencia, ese resumen
con el layout oficial del SRI. `TalonResumenAts` (nuevo, en `PsaWeb.Ats`) arma
el HTML reusando esa misma plantilla/CSS (adaptada a Razor/UTF-8, texto sin
firmas reales — las líneas quedan en blanco para imprimir), y un renderer
HTML→PDF la convierte a bytes. **Decisión pendiente de tecnología de
renderizado** (§7): se necesita algo que corra en el servidor IIS de
`SERWEBPSA01` sin licencia paga (mismo criterio que llevó a ClosedXML en vez de
EPPlus). Candidatos: **PuppeteerSharp** (Chromium headless, MIT, reproduce el
HTML/CSS con fidelidad pero descarga un binario de Chromium ~300 MB) o una
librería de PDF nativa .NET tipo **QuestPDF** (más liviana, pero hay que
re-maquetar el diseño en su API en vez de reusar el HTML/CSS tal cual). A
confirmar con el usuario en F5.6.

Se desarrolla en las fases F5.5 (validaciones) y F5.6 (Talón Resumen) — ver §4.

---

## 4. Fases

Mismo molde que app #1 / app #2 (`F1…Fn`, `F3` es el bloque pesado).

- **F1 — Esquema + escritor XML.** `PsaWeb.Ats` nuevo: `Esquema/` **regenerado**
  desde `C:\SRI-DIMM\Documentacion\ats.xsd` vigente (no copiado de
  `Esquema_at.cs` — hallazgo 4 de §1.4: falta `valorRetencionNc`).
  `EscritorXmlAts` (`XmlSerializer`, `XmlWriterSettings`/`XmlSerializerNamespaces`
  para reproducir el formato real). Scaffold de `dicIdentityTypeATS` en
  `PsaWeb.PeachEbills`. Tests: serializar un `ivaType` armado a mano y comparar
  estructural/numéricamente contra **el golden real ya disponible**
  (`C:\SRI-DIMM\Documentacion\ATS CPTDC JULIO  2026.xml`, §6). **Ya no bloquea
  con "conseguir un golden" — el archivo ya existe.** ~1–2 días.
- **F2 — Lector de ventas.** Port de `LoadSales` + `LoadCustomer` →
  `LectorVentasAts` (ODBC `?`) + `ArmarVentas` puro (`formasDePago` fijo `"20"`,
  decisión 2026-09-12) + `LectorVentasEstablecimientoAts` + impl de muestra.
  Tests de `ArmarVentas` (buckets por `SalesTaxType`, fusión por cliente, NC
  negativa, retenciones recibidas) y contra el bloque `<ventas>`/`<compensaciones>`
  del golden. ~2 días.
- **F3 — Lector de compras.** Port de `LoadPurchases` (+ `LoadImponibles`,
  `LoadRetencionesRF`, `SriAuthorization`, `AirBaseImp`) + `LoadVendor`
  (extendiendo `LectorProveedor`: `dicIdentityTypeATS` + `pagoExteriorType`).
  El bloque grande. Impl de muestra. Umbral de forma de pago **fijo en 500,
  tal cual el `.exe`** (sin variante por fecha — decisión 2026-09-12). Tests de
  la parte pura (clasificación 01/02/03/04, `codSustento`, buckets `valRet*`,
  `detalleAir` incl. líneas `332`/`332G`, bloque `docModificado` de NC) y
  contra el bloque `<compras>` del golden real. ~3–4 días.
- **F4 — Anulados + armador.** Portar el mínimo de `Sage50MetaData`
  (`SageJournalMetaData` enums + `ExternalVendorInfo`) a `PsaWeb.Comprobantes/Sri`.
  Port de `LoadCanceled` → `LectorAnuladosAts`. `ArmadorAts` (= `LoadToATSobject`:
  cabecera + ventas + compras + anulados). **Aplica el fix 1 de §1.4**:
  `numEstabRuc` y `ventasEstablecimiento` desde `Establishments` (activos del
  RUC), no desde "los que facturaron" — es el único punto donde se espera
  diferencia real contra el golden; documentar el diff. **Validación completa**
  contra `ATS CPTDC JULIO  2026.xml` (real). ~2–3 días.
- **F5 — Módulo + página + endpoint.** `modules/PsaWeb.Modules.Ats`, página
  `/ats` con las 7 pestañas de solo lectura (con el fix cosmético 3 de §1.4 en
  "NC Compras") + `PsaModal` de retenciones, endpoint `/ats/export` (XML),
  acotado a empresa + año de sesión, gate `quats` (`GateProvisional`),
  `AppCatalogo` + nav. `Data/` con repos EF/ODBC + interruptor real/muestra por
  `Sage50:ConnectionString`. Smoke en navegador (login → empresa → `/ats` →
  Cargar → pestañas → descarga XML). ~2–3 días.
- **F5.5 — "Revisar ATS" (§3.5a).** `ValidadorAts` (puro, testeable) + panel de
  hallazgos en la página, reemplazando el botón muerto "Ver errores". Tests por
  regla (estructurales, cruce ventas↔establecimientos, retenciones de IVA vs.
  `montoIva`, duplicados). ~2–3 días.
- **F5.6 — Talón Resumen (PDF) (§3.5b).** `TalonResumenAts` (arma el HTML a
  partir de las mismas agregaciones de "Resumen ATS") + renderer HTML→PDF
  (tecnología a confirmar, §7) + endpoint `/ats/talon-resumen`. Validar layout
  visual contra `TRSMN-ATS-07-2026-CPTDC.pdf` (real). ~2–3 días (más si el
  renderer HTML→PDF elegido requiere ajuste de infraestructura en el server).
- **F6 — Deploy a `SERWEBPSA01` + validación.** Redeploy del Host con el módulo
  (`publish win-x86 self-contained`), env vars ya existentes. **Validación de
  paridad**: contrastar el XML y el Talón del web contra los reales de CPTDC
  julio/2026 (§6) y **cargar el XML del web en el DIMM real** (valida contra el
  XSD + su propio validador de negocio). Revisión por un usuario del área. Sin
  nada "real" que enviar (el ATS es un archivo que sube el contador). ~1–2 días.
- **F7 — descartado de este corte** (decisión 1/3, §5). Formularios 103 y 104
  (relleno de plantilla xlsx + "Generar JSON" vía `ConfigJsonForms`/`PSContexts`)
  quedan fuera por ahora; se retoman si se decide más adelante.

**Estimación núcleo (F1–F6): ~14–20 días de dev**, concentrados en F3 (port fiel
de ~1.000 LOC de esquema Sage), F4 (fix de establecimientos + validación contra
el golden real) y F5.6 (Talón Resumen, si el renderer HTML→PDF elegido pide
trabajo extra de infraestructura).

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
   validador del DIMM instalado — detalle en §1.4). Resultado ajustado tras
   feedback del usuario (2026-09-12, punto 5.2): **1 corrección real a
   aplicar**, **1 se descarta deliberadamente**, y se sumó **1 hallazgo nuevo**:
   - `numEstabRuc` / `ventasEstablecimiento` **sí se corrige**: deben reflejar
     los establecimientos **activos del RUC** (vía `Establishments`), no los
     que facturaron ese mes.
   - "Forma de pago" en **ventas**: **se descarta el fix** — queda fija en
     `"20"` (y en compras el umbral queda fijo en 500, sin variante por fecha),
     por decisión explícita del usuario: condicionarla a fecha/monto agrega una
     regla más para mantener con riesgo de error, a cambio de una precisión
     normativa marginal. Documentado como decisión de negocio, no como
     descuido.
   - **Nuevo (hallazgo 4 de §1.4)**: el esquema `Esquema_at.cs` del `.exe` está
     desactualizado — le falta `valorRetencionNc` (campo SRI de 2020), detectado
     al comparar contra el XML real de CPTDC. F1 regenera el esquema desde el
     XSD vigente en vez de copiar el archivo viejo.
   - Bug cosmético sin impacto en el XML: la pestaña "NC Compras" del `.exe`
     filtra igual que "Compras" — se corrige al portar la grilla.
     `razonSocial` con `&`→`y` y sin `.`/`-` **no es un bug**: el XSD lo exige.
     `TipoIDInformante`/`codigoOperativo` tampoco son un bug: su único valor de
     enum posible es el default de C#.
6. ✅ **Multi-establecimiento**: el módulo web queda **solo empresa de sesión**,
   sin cross-company ni worker.
7. ✅ **Botón "Ver errores"**: no se omite — se reemplaza por **"Revisar ATS"**
   (validaciones tipo DIMM, F5.5) que además, confirmado por el usuario, debe
   terminar en **dos entregables**: el **Talón Resumen en PDF** (igual al que
   emite el DIMM real) y el **XML** para subir al DIMM y luego a la
   declaración. Diseño completo en §3.5 (fases F5.5 + F5.6), con la plantilla
   HTML/CSS real del DIMM ya localizada dentro de su propio instalador.
8. ✅ **Fixture**: **CPTDC (RUC `1792051800001`)**. Ya verificada localmente en
   PREDATOR (Sage local + `PeachConnString` + `Establishments`, del plan de
   Kardex) y **`lparedes` ya tiene acceso** (rol `6` "Hacer Comprobantes
   Electrónicos" vía `UserTransmitter`/`udrUserRolesTr`, confirmado por
   consulta directa) — no hace falta script SQL de acceso. **El golden ya no
   está pendiente**: el usuario dejó en `C:\SRI-DIMM\Documentacion\` el XML y
   el Talón Resumen reales de la declaración de CPTDC de julio/2026 — misma
   información que hoy está en el Sage de PREDATOR (§6).

---

## 6. Fixture de prueba — CPTDC (RUC `1792051800001`), julio/2026

**El golden ya existe — no hay que generarlo.** El usuario puso en
`C:\SRI-DIMM\Documentacion\` los dos entregables **reales** de la declaración
de CPTDC de julio/2026 (misma información que hoy está en la Sage de CPTDC en
PREDATOR):

- **`ATS CPTDC JULIO  2026.xml`** (536 KB) — el XML real presentado al SRI.
  Confirmado con un vistazo: cabecera `TipoIDInformante=R`,
  `IdInformante=1792051800001`, `razonSocial="CPTDC CHINA PETROLEUM
  TECHNOLOGY y DEVELOPMENT CORPORATION ECUADOR S A"`, `Anio=2026`, `Mes=07`,
  `numEstabRuc=001`, `totalVentas=7380817.15`, `codigoOperativo=IVA`, con
  bloques `compras`/`ventas`/`anulados` completos (retenciones de renta e IVA,
  `pagoExterior`, `formasDePago`, `air` incluidos). Es la fuente del hallazgo 4
  de §1.4 (`valorRetencionNc`).
- **`TRSMN-ATS-07-2026-CPTDC.pdf`** (114 KB) — el Talón Resumen real que emitió
  el DIMM para esa misma declaración. Es la referencia visual/numérica de F5.6
  (§3.5b): las tablas de compras/ventas/retenciones de este PDF deben cuadrar
  con las que arma el port a partir del mismo período.

Con esto, **CPTDC julio/2026 es el golden de punta a punta**: F1 (esquema) y F4
(armador completo) comparan contra el XML; F5.6 (Talón) compara contra el PDF;
y como la información real está en el Sage de PREDATOR, **F2/F3 (lectores)
pueden validarse consulta por consulta contra los mismos datos** en vez de
depender de comprobantes ficticios.

- Empresa: **CPTDC CHINA PETROLEUM TECHNOLOGY & DEVELOPMENT COR.**, ya usada
  como fixture del Kardex — Sage local en PREDATOR
  (`dbq=cptdcecuadorsa202520`, DBQ Zen `CPTDCECUADORSA202520`, `servername`
  local vía `PeachEbills:SageServerNameOverride=localhost`) y
  `PeachConnString`/`Establishments` verificados (`001-001/002/003`).
- **Acceso ya resuelto**: `lparedes` (login web de dev) **ya está** en
  `UserTransmitter` + `udrUserRolesTr` para este RUC (rol `6`, "Hacer
  Comprobantes Electrónicos") — confirmado por consulta directa 2026-09-12, no
  hace falta script SQL de acceso (a diferencia de SANCEV/Roller Dance/DGRV en
  el plan de FE).
- Probes ODBC 32-bit: reutilizar el patrón de
  `scratchpad/kardex-cptdc-probe*.ps1` (correr con
  `C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`).

**Ya no hay nada pendiente del área para arrancar F1.**

---

## 7. Notas de arquitectura

- **x86**: solo el Host (ya lo es), por el driver Pervasive de 32 bits. `PsaWeb.Ats`
  y el módulo quedan AnyCPU.
- **Salvo el fix de establecimientos (hallazgo 1, §1.4) y el esquema
  regenerado (hallazgo 4), sin lógica nueva**: se replica lo que hace el
  `.exe`, incluida su decisión de dejar "forma de pago" fija (hallazgo 2). El
  XSD del SRI es el contrato — el esquema se genera, no se re-modela a mano.
- **Sin credenciales en git**: cadenas vacías en `appsettings.Development.json`;
  reales en User Secrets / env vars del sitio.
- **Renderizado del Talón Resumen (F5.6) — decisión de tecnología pendiente.**
  El diseño exacto (HTML + CSS) ya está disponible sin reconstruirlo a ojo
  (`Dimm\plugins\ec.gob.sri.dimm.ats.validacion_1.2.0.jar\resources\
  templateATS2.html`/`talonAts.css`/`sriTalonLogo.jpg`). Falta elegir cómo
  convertir ese HTML a PDF en el servidor **sin licencia paga** (mismo criterio
  que llevó a ClosedXML en vez de EPPlus): candidatos **PuppeteerSharp**
  (Chromium headless, MIT, fidelidad total al HTML/CSS pero suma un binario
  pesado al deploy) vs. **QuestPDF** (liviano, pero exige rearmar el layout en
  su API en vez de reusar el HTML/CSS). A decidir en F5.6.
- El `.exe` no tiene build ni CI; su "verdad" es el código fuente contrastado
  contra la normativa oficial disponible en esta máquina:
  `C:\SRI-DIMM\Documentacion\ats.xsd` (esquema vigente) + la Ficha Técnica del
  ATS + el validador real del DIMM +, ahora, **los dos entregables reales de
  CPTDC julio/2026** (XML + Talón Resumen, §6) — el golden ya no depende de
  correr el `.exe` a mano.

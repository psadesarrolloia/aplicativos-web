# Plan — App #2: Sage50FacturacionElectronica → módulo web

Ola 1, aplicativo #2. Piloto (Cierre de Caja) y app #1 (`AutomaticTwhSender` →
`/retenciones`) ya desplegados en `SERWEBPSA01`. Este documento planifica el port
de `Sage50FacturacionElectronica`.

## 1. Qué hace el aplicativo de escritorio

MDI interactivo (~8.200 LOC, no headless). El usuario elige empresa + ambiente
(`FrmEmpresaSeleccionar`) y opera **cuatro tipos de comprobante**, cada uno en su
formulario:

| Tipo | Flujo desktop | Lector | Constructor Datil |
|---|---|---|---|
| **Facturas de venta** | `FrmSalesInvoices` → `LoadSaleInvoices`/`LoadSaleInvoice` | ~720 LOC (descuentos, tasas de IVA por `Tax_Authority`, detalles de `JrnlRow`, info adicional configurable) | `DatilSend(SaleInvoice)` |
| **Notas de crédito de venta** | `FrmSalesNCs` → `LoadSaleNCs`/`LoadSaleNC` | ~363 LOC (+ referencia al documento modificado: `NCdetail`) | `DatilSend(SaleNC)` |
| **Liquidaciones de compra** | `FrmPurchaseLIQ` → `LoadPurchaseLiqs`/`LoadPurchaseInvoice` | ~332 LOC | `DatilSend(PurchaseLiqInvoice)` |
| **Retenciones de compra** | `FrmPurcahsesTwhs` → `LoadPurchaseTwhs`/`LoadPurchaseTwh` | ~334 LOC | `DatilSend(PurchaseTwh)` — **ya portado en app #1** |

Flujo común por tipo:

1. Elegir rango de fechas → **listar pendientes** (consulta ODBC a Sage 50) →
   se cachean en memoria.
2. Clic en una fila → **panel de detalle**: persona (cliente/proveedor), líneas,
   errores detectados (`Mistake`), info adicional.
3. Diálogo de confirmación → `DatilSend`:
   - POST `…/issue` con el comprobante armado.
   - Si Datil devuelve `id` → persiste el comprobante en **`PeachEBills` SQL**
     (`CRUDsql.BillIdCreate` / `NCIdCreate` / `TaxWithHCreate`) y registra la
     respuesta cruda en `DatilRequests`.
   - Si devuelve `errors` → los muestra, no persiste.
4. Re-consulta el estado en el SRI vía `QueryToDatil` (GET `…/{id}`) para pintar
   la columna "estado".

Botón **"Procesar lote"** (`mksinBatch` / `mkTwhBatch`): lo mismo para todos los
pendientes sin errores. Botón **"Solicitar anulación"** (solo retenciones,
`auCanceTwh`): manda un email al supervisor de la empresa (no anula nada por sí
mismo).

## 2. Lo que ya está y se reutiliza tal cual

- **Shell**: `/seleccionar-empresa` + `EmpresaSwitcher` reemplazan
  `FrmEmpresaSeleccionar`. Empresa + ambiente viven por sesión en
  `EmpresaActualService`.
- **`PsaWeb.Seguridad/Permisos`**: ya tiene los códigos de `allowAction` de FE
  (`qusaleinv`, `mksaleinv`, `mksinBatch`, `qusalenc`, `mksalenc`, `qupurchtwh`,
  `mkpurchtwh`, `mkTwhBatch`, `auCanceTwh`). `FEAllowed.UserHadAcess` →
  `ISecurityDirectory.TienePermisoAsync`. `AppCatalogo` para el dashboard/nav.
- **`PsaWeb.Comprobantes`**: `LectorProveedor`, todo `Retenciones/*`
  (`ConstructorRetencion`, `LectorCompraRetencion`, `RetencionBuilder`),
  `Sri/NumeroDocumentoSri` (= `NumberFormatValidate`), `TiposIdentificacion`
  (= `QueryPersonType`), `EmailSri` (= `EmailValidator`), `CodigosDocumento`,
  `DiarioSage`.
- **`PsaWeb.Datil`**: cliente sobre `HttpClient`, `DatilOptions.DryRun` (default
  `true`), modelo `Retencion`, `EmitirRetencionAsync` / `ConsultarEstadoAsync`,
  `DatilCredentials`, parseo de `id` / `errors` / `estado`.
- **`PsaWeb.PeachEbills`**: contexto EF con `Transmitter`, `CurrentAmbient`,
  `Establishments`, `Persons`, `Thdetails`, `TaxWithHoldings`, `DatilRequests`,
  `DatilApi`, `PeachConnString`, `EPoofGeneralAditionalInfo` + tablas de
  seguridad. `DbSecret` (TripleDES), `PeachConnStringResolver`.
- **`PsaWeb.Modules.Retenciones`**: `ProcesadorRetenciones.ProcesarUnaAsync` /
  `ProcesarTodasAsync`, `EjecucionRetencionesGate` (single-flight),
  `TableroRetenciones`, componente `PsaModal`, worker opcional.
- **Módulo `/retenciones`** ya cubre el 4º tipo → **el módulo FE NO lo duplica**
  (decisión 2026-09-07). App #2 = Facturas + Notas de crédito + Liquidaciones.

## 3. Lo que hay que construir

### 3.1 `PsaWeb.Datil` — modelos y llamadas nuevas

- Modelos (port de `DatilClientLibrary`, serializados `snake_case` sin nulos con
  `DatilJson`):
  - `Factura` + `TotalesFactura` + `MetodoPago` + `CreditoFactura`.
  - `NotaDeCredito` + `TotalesNotaDeCredito` (+ `NumeroDocumentoModificado`,
    `TipoDocumentoModificado`, `FechaEmisionDocumentoModificado`, `Motivo`).
  - `LiquidacionCompra` + `TotalesLiquidacion` + `FormaPagoLiquidacionC` +
    `Proveedor`.
  - Compartidos: `Item`, `ImpuestoItem`, `Impuesto`, `InfoAdicional` (lista
    ordenada), reutilizar `Emisor` / `Establecimiento` / `Comprador` existentes.
- `DatilCredentials`: hoy tiene una sola `BaseUrl`. Cambiar a URLs por tipo de
  doc (`FacturaUrl`, `NotaCreditoUrl`, `LiquidacionUrl`, `RetencionUrl`) leídas de
  `DatilAPI` (`ApiFacturaUrl`, `ApiNCUrl`, `ApiRetencionUrl`, + Liquidación). El
  `IssueUrl`/`StatusUrl` de retenciones que ya usa app #1 se mantiene por
  compatibilidad.
- `IDatilClient`: agregar `EmitirFacturaAsync`, `EmitirNotaCreditoAsync`,
  `EmitirLiquidacionAsync` (misma forma que `EmitirRetencionAsync`, respetan
  `DryRun`) y `ConsultarComprobanteAsync(id, tipo, cred)` → devuelve
  `estado` + primer mensaje de error (port de `QueryToDatil`).
- Tests: serializar cada doc contra un JSON golden extraído de una corrida real
  del `.exe`.

### 3.2 `PsaWeb.PeachEbills` — entidades y capa de escritura

- Scaffold (EF Core, `dotnet ef dbcontext scaffold` acotado a estas tablas) de:
  `Facturas`, `Details`, `NCdetail`, `Payments`, `PaymentTypes`,
  `FacturaPropiedadExterna`, `InvoiceConfigAditionalInfo`.
- `RepositorioComprobantesVenta` — port de `CRUDsql`:
  - `GuardarFacturaAsync` (= `BillIdCreate`): upsert `Persons`, transacción con
    `Facturas` + `Details` + `Payments` (solo codDoc `01`) + `FacturaPropiedadExterna`
    (FAX). Devuelve `FacturaId`.
  - `GuardarNotaCreditoAsync` (= `NCIdCreate`): `BillIdCreate` + fila `NCdetail`.
  - Log a `DatilRequests` (ya existe; `IsTaxWithH = false`).
  - **En `DryRun` no persiste nada** (misma disciplina que `RepositorioRetenciones`).
- `ISecurityDirectory`: agregar `EmailUsuarioAsync(user)` y
  `EmailsPorRolAsync(ruc, rol = "Supervisor")` (port de `FEAllowed.userEmail` /
  `emailsByRole`, leen `users` / `roles` / `udrUserRolesTr`).

### 3.3 `PsaWeb.Comprobantes` — lectores y constructores nuevos

- `Clientes/LectorCliente` — port de `LoadCustomerPeach` (lee `Customers` +
  `Address` por `CustomerRecordNumber`; quirks: RUC en `CustomField4` o
  `Address.Country`, pasaporte en `CustomField5`, nombre `Customer_Bill_Name` +
  `CustomField1`; valida emails con `EmailSri`, deduce tipo con
  `TiposIdentificacion`).
- `Sri/NumeroDocumentoConEstablecimiento` — port de `CommonSriNumberValidate` /
  `SalesInvoiceNumberValidate`: valida formato con `NumeroDocumentoSri` y resuelve
  el `EstablishmentId` en `Establishments` por `(RUC, Code, IssuePoint)`.
  Interfaz `IEstablecimientoLookup` ya existe en el módulo de retenciones — mover
  a `PsaWeb.Comprobantes` o duplicar la firma.
- `InfoAdicional/LectorInfoAdicional` — port de `LoadAditionalInfoOnInvoice`
  (config por RUC en `InvoiceConfigAditionalInfo`, fuentes `JrnlHdr` / `JrnlRow` /
  `Customers` / valor fijo, orden por `OrderNum`) para **facturas**. Para NC y
  liquidaciones se usa `GeneralAdtionalInfo(ruc, codDoc)` → ya cubierto por
  `IInfoAdicionalLookup` + `EPoofGeneralAditionalInfo`. **Se hace en F3a** (sólo
  lo consumen las facturas).
- **Facturas** (`Venta/`):
  - `LectorFacturaVenta` — port de `LoadSaleInvoice` + `LoadSaleInvoices` (lista).
    Es el bloque más grande y delicado: cálculo de descuentos con/sin IVA,
    tasa de IVA desde `Tax_Code`/`Tax_Authority`, detalles desde `JrnlRow`,
    `Mistake`s. Port **fiel**, sin rediseñar.
  - `ConstructorFactura` — port del ctor `DatilSend(ref SaleInvoice)`: comprador,
    establecimiento, emisor, items + impuestos agrupados, totales, pagos vs
    crédito según `DateDue`, `FechaEmision` con offset local fijo UTC-5, email de
    pruebas según ambiente.
- **Notas de crédito** (`Venta/`): `LectorNotaCredito` + `ConstructorNotaCredito`
  (port de `LoadSaleNC` + `DatilSend(ref SaleNC)`).
- **Liquidaciones** (`Compra/`): `LectorLiquidacionCompra` + `ConstructorLiquidacion`
  (port de `LoadPurchaseInvoice`/`LoadPurchaseLiqs` + `DatilSend(ref PurchaseLiqInvoice)`).
- Cada lector: interfaz + impl ODBC (parámetros `?`, nunca interpolar) + impl de
  **muestra** para dev (PREDATOR no llega a la Sage multi-empresa de `SERWEBPSA01`).
- Tests por lector/constructor contra datos golden.

### 3.4 Correo (para "Solicitar anulación")

La plataforma web hoy no tiene SMTP. Agregar:

- `PsaWeb.Notificaciones` (o dentro de `PsaWeb.Identidad`): `IServicioCorreo` +
  `SmtpServicioCorreo` (`System.Net.Mail`), opciones `Correo:{Servidor, Puerto,
  Usuario, Clave, Ssl}` (env vars `Correo__*`). Port de `ConfigSettings`.
- Remitente **fijo `anulaciones@paredes.com.ec`** por ahora (decisión 2026-09-07;
  el `.exe` usaba `info@paredes.com.ec`). No se parametriza todavía.
- Registrado solo si `Correo:Servidor` está presente; si no, la acción de
  anulación queda deshabilitada con aviso.

### 3.5 Módulo `PsaWeb.Modules.FacturacionElectronica`

RCL nuevo (patrón de `COMO-MIGRAR-UN-APLICATIVO.md`). **Páginas separadas**
(decisión 2026-09-07), cada una acotada a la empresa + ambiente de la sesión:

| Ruta | Página | Permisos que la habilitan |
|---|---|---|
| `/fe/facturas` | Facturas de venta | `qusaleinv`, `mksaleinv`, `mksinBatch` |
| `/fe/notas-credito` | Notas de crédito | `qusalenc`, `mksalenc` |
| `/fe/liquidaciones` | Liquidaciones de compra | `qupurchliq`, `mkpurchliq` **(códigos nuevos)** |

Por página: rango de fechas → tabla de pendientes → los 4 estados obligatorios
(cargando / error / vacío / lista); clic → detalle (persona, líneas, errores,
info adicional en `PsaModal` o panel); **"Generar"** por fila con banner
DRY-RUN / EMISIÓN REAL según `Datil:DryRun`; **"Procesar lote"** gateado por
permiso; enlaces **PDF/XML** a `https://app.datil.co/ver/{id}/pdf|xml`
(target `_blank`, como el `.exe`). Retención → botón "Solicitar anulación"
vive en `/retenciones`, no acá.

- `IResolverEmpresaSage` / `HostResolverEmpresaSage`: reutilizar el de Cierre de
  Caja (RUC de sesión → cadena ODBC vía `PeachConnStringResolver`) o extraerlo a
  un servicio compartido.
- Orquestación: `ProcesadorComprobantesVenta` con `ProcesarUnoAsync(postOrder)` /
  `ProcesarLoteAsync()` por tipo, degradando por ítem, con `EjecucionGate`
  single-flight compartido (mismo patrón que retenciones). Sin worker (FE es
  100% a demanda).
- Registro en el Host: `Add<...>()` en `Program.cs` solo si están las cadenas;
  ensamblado en `Routes.razor` + `MapRazorComponents(...).AddAdditionalAssemblies`;
  3 enlaces en `NavMenu`; 3 `AppWeb` nuevos en `AppCatalogo`.
- `Permisos`: agregar `VerLiquidaciones = "qupurchliq"` y
  `HacerLiquidacion = "mkpurchliq"`. Requiere **2 filas nuevas en `allowAction`**
  de `PeachEBills` y asignarlas a los roles (tarea del área, no del código).

## 4. Fases

Mismo molde que app #1 (`F1→F5`, con `F3` dividido).

- **F1 — Datil (modelos + llamadas). HECHO** (rama `app2-fe-f1-datil-modelos`).
  `Model/Comprobantes.cs` (`ItemComprobante`, `Impuesto`, `InfoAdicionalItem`,
  `RetencionEnFactura`), `Model/Factura.cs`, `Model/NotaCredito.cs`,
  `Model/Liquidacion.cs` (reutilizan `Emisor`/`Comprador`/`Establecimiento`).
  `IDatilClient` + `DatilClient`: `EmitirFacturaAsync` / `EmitirNotaCreditoAsync`
  / `EmitirLiquidacionAsync` (core común `EnviarAsync<T>`, respetan `DryRun`) +
  `ConsultarComprobanteAsync` → `DatilConsultaResult` (port de `QueryToDatil`).
  `DatilCredentials` sin cambios: cada tipo de doc arma sus credenciales con su
  `BaseUrl` (`invoices/`, `credit-notes/`, `purchase-settlements/`). 15 tests
  nuevos (serialización snake_case / omisión de nulos / info adicional lista vs
  diccionario / crédito vs pagos / consulta). Solución **128 tests** en verde.
- **F2 — Escritura PeachEbills + lectores compartidos. HECHO**
  (rama `app2-fe-f2-escritura-lectores`).
  - `PsaWeb.PeachEbills`: 7 entidades EF nuevas (`Facturas`, `Details`, `NcDetail`
    [`NCdetail`], `Payments`, `PaymentTypes`, `FacturaPropiedadExterna` [PK
    compuesta], `InvoiceConfigAditionalInfo`) + `DbSet`s en un partial.
    `RepositorioComprobantesVenta` — port de `CRUDsql.BillIdCreate` / `NCIdCreate`:
    `GuardarFacturaAsync` / `GuardarNotaCreditoAsync` (upsert `Persons` + tx con
    `Facturas`+`Details`+`Payments`[codDoc 01]+`NCdetail`+`FacturaPropiedadExterna`
    +`DatilRequests`). Registrado en `AddPeachEbills`.
  - `PsaWeb.Comprobantes`: `Clientes/LectorCliente` (+`ClienteSri`) — port de
    `LoadCustomerPeach` (2 queries ODBC, quirks de identificación/pasaporte/
    multi-dirección, validación de emails). `Sri/ValidadorNumeroEstablecimiento`
    — port de `CommonSriNumberValidate`/`SalesInvoiceNumberValidate` (formato
    tolerante `AnalizarFactura` + lookup de `Establishments` vía
    `IEstablecimientoLookup`).
  - 16 tests nuevos (3 de mapeo EF contra la copia local + 13 de lógica pura).
    Solución **144 tests** en verde.
  - **`LectorInfoAdicional` se mueve a F3a** (sólo lo consumen las facturas; NC y
    liquidaciones usan el `IInfoAdicionalLookup` existente).
- **F3a — Facturas de venta.** §3.3 (bloque grande). Dev con lector de muestra.
  Dividido:
  - **F3a-1 HECHO** (rama `app2-fe-f3a-facturas`): `Venta/LectorFacturaVenta`
    (port de `LoadSaleInvoice.ForceLoadFromPeach`) + DTOs
    (`FacturaVentaCabecera`/`FacturaVentaLinea`/`FacturaVentaLeida`). 5 queries
    ODBC (cabecera `JrnlHdr`+`Tax_Code`; descuento `JrnlRow`; monto IVA `JrnlRow`;
    tasa IVA `Tax_Authority`; líneas `JrnlRow`+`LineItem`) + `LectorCliente`. La
    lógica pura de descuentos/IVA/mapeo de códigos vive en `ArmarDesde` (port
    fiel, incluye 2 bugs conocidos del `.exe` documentados). 14 tests de
    `ArmarDesde` (con/sin IVA, descuento con/sin IVA, número corregible, cantidad
    <1, multilínea, sin líneas, cliente nulo). **158 tests solución.**
  - **F3a-2 HECHO**: `Venta/InfoAdicional/LectorInfoAdicional` (port de
    `LoadAditionalInfoOnInvoice`) + `IConfigInfoAdicionalFactura` /
    `ConfigInfoAdicional` (la impl EF va en F4). `ArmarAsync` recorre la config
    ordenada, resuelve valores fijos / `JrnlHdr` / `JrnlRow` (con la lógica de
    "Comisión" y numeración de filas) / `Customers`; valida `SourceValue` como
    identificador SQL antes de interpolar. 4 tests de la parte de ensamblado.
  - **F3a-3 (constructor) HECHO**: `Venta/ConstructorFactura` (port de
    `DatilSend(ref SaleInvoice)`) → `ResultadoFactura { Factura (Datil),
    FacturaParaGuardar (DTOs planos, sin EF), Errores }`. Mapea emisor/comprador/
    establecimiento (punto de emisión del número), items con impuesto+tarifa
    0–100 (`CheckTaxPercentValue`), totales con impuestos agrupados, secuencial
    sin ceros, `FechaEmision` UTC-5 fijo, pago al contado vs crédito según
    vencimiento, redirección de email en pruebas. `ValidadorNumeroEstablecimiento`
    ahora expone el `EstablecimientoInfo` completo. 10 tests. **172 tests solución.**
  - **F3a-4 HECHO**: `Venta/FacturaBuilder` (resuelve el establecimiento con
    `ValidadorNumeroEstablecimiento` y llama a `ConstructorFactura`),
    `Venta/LectorFacturasPendientes` (+`FacturaPendiente`) = port de la consulta
    de `LoadSaleInvoices` (`JrnlKey_Journal=3`, `JournalEx=8`, `JrnlTypeEx=0`, no
    `ANULAD%`, rango de fechas parametrizado), y `Venta/Muestra/FacturasVentaMuestra`
    (3 facturas de muestra para dev: simple con IVA / a exterior / con descuento y
    crédito). 6 tests. **178 tests solución. F3a COMPLETA.**
- **F3b — Notas de crédito. HECHO** (rama `app2-fe-f3b-notas-credito`).
  `Venta/LectorNotaCredito` (port de `LoadSaleNC.ForceLoadFromPeach`): 5 queries
  ODBC (cabecera `JrnlTypeEx=2`/`JournalEx=9`; factura relacionada por
  `INV_POSOOrderNumber` → `LectorFacturaVenta.LeerAsync` para `NCdetail`; monto
  IVA; tasa IVA; líneas con `LinkToOtherTrxIndex>0`) + `LectorCliente`. `ArmarDesde`
  pura: número **estricto** (`AnalizarEstricto`, no tolerante), causa de
  `ReturnAuthorization` (default "Devolución"), IVA sin redondear el %, mapeo de
  Tax con **error** en el caso default (más estricto que factura), sin descuentos.
  `Venta/ConstructorNotaCredito` (port de `DatilSend(SaleNC)`) → `ResultadoNotaCredito`
  { `NotaCredito` (Datil), `NotaCreditoParaGuardar` con `DocumentoModificadoParaGuardar`,
  `Errores` }. Usa el punto de emisión **del establecimiento** (no del número),
  `FechaEmisionDocumentoModificado` UTC-5, `InformacionAdicional` del tipo 04.
  `Venta/NotaCreditoBuilder` (resuelve establecimiento + `IInfoAdicionalLookup(04)`).
  `Venta/HelpersVenta` (helpers compartidos factura/NC). 20 tests. **188 tests
  solución.**
- **F3c — Liquidaciones de compra. HECHO** (rama `app2-fe-f3cd`).
  `Venta/LectorLiquidacionCompra` (port de `LoadPurchaseInvoice.ForceLoadFromPeach`):
  3 queries ODBC (cabecera `JrnlKey_Journal=4`; ítem `Category='IMPUESTO'` para
  el IVA; detalles excluyendo `R-*RF`/`R-IVA`/`IMPUESTO`/`AUT-SRI`) + `LectorProveedor`.
  El % de IVA se resuelve del texto `"15%"` (CustomField3/2) contra `dicTaxRate`
  vía `ITasaIvaLookup` (entidad EF `DicTaxRate` nueva). `ArmarDesde` pura: número
  tolerante, error si hay >1 ítem de IVA, líneas "NO IVA" mapeadas por
  CustomField4 (IMPEX→7 / NOGRA→6) con la mutación del código "actual" del `.exe`,
  sólo líneas con `Amount>0`. `Venta/ConstructorLiquidacion` (port de
  `DatilSend(PurchaseLiqInvoice)`) → `ResultadoLiquidacion`/`LiquidacionParaGuardar`
  (codDoc 03, TransType 2); punto de emisión del número, `FormaPago` fija "20".
  `Venta/LiquidacionBuilder` (+`IInfoAdicionalLookup(03)`), `Venta/LectorLiquidacionesPendientes`.
  11 tests. **128 tests Comprobantes.**
- **F3d — Correo + "Solicitar anulación". HECHO** (rama `app2-fe-f3cd`).
  Proyecto nuevo **`src/PsaWeb.Notificaciones`**: `IServicioCorreo` +
  `SmtpServicioCorreo` (`System.Net.Mail`) + `CorreoOptions` (`Correo:*`, remitente
  fijo `anulaciones@paredes.com.ec`) + `AddNotificaciones` (impl inerte si no hay
  `Correo:Servidor`; `Disponible=false`). `ISecurityDirectory` +
  `EmailUsuarioAsync` / `EmailsPorRolAsync` (port de `FEAllowed.userEmail` /
  `emailsByRole`, sobre `user`/`roles`/`udrUserRolesTr`). `SolicitudAnulacionRetencion`
  en el módulo Retenciones (port de `FrmPurcahsesTwhs.btnCancel_Click`): arma el
  correo al supervisor + al usuario con el enlace a Datil; falla con gracia si no
  hay supervisor o SMTP. Registrado en `AddRetenciones`; `AddNotificaciones` en el
  Host. La acción se cablea a la página `/retenciones` en F4. 5 tests. **203 tests
  solución.**
- **F4 — Módulo + páginas + wiring. HECHO** (rama `app2-fe-f4`).
  Módulo nuevo **`modules/PsaWeb.Modules.FacturacionElectronica`**:
  - `Data/LookupsEf` — impls EF de `IEstablecimientoLookup`/`IInfoAdicionalLookup`
    (compartidos, `TryAdd`), `IConfigInfoAdicionalFactura` (`InvoiceConfigAditionalInfo`),
    `ITasaIvaLookup` (`dicTaxRate`). `Data/EmisorLookup` (RUC → `EmpresaEmisora` +
    `DatilEmpresaFe` con URLs por tipo de doc; liquidación = constante).
  - `Data/MapeadorEntidades` — `FacturaParaGuardar`/`NotaCreditoParaGuardar`/
    `LiquidacionParaGuardar` → entidades `Facturas`/`Details`/`Persons`/`NcDetail`
    (mantiene `PsaWeb.Comprobantes` sin EF).
  - `ProcesadorComprobantesVenta` — orquestador: por tipo, `Procesar…Async` (uno)
    y `ProcesarLote…Async` (rango de fechas, 1 conexión), + `Listar…PendientesAsync`.
    Lee → `*Builder.ArmarAsync` → `IDatilClient.Emitir…Async`; en DryRun no
    persiste; en emisión real mapea + `RepositorioComprobantesVenta`. Degrada por
    ítem (`ResultadoComprobante`/`ResumenLote`).
  - `src/PsaWeb.Comprobantes/Venta/LectorNotasCreditoPendientes` (port de la
    consulta de `LoadSaleNCs`).
  - Páginas: `Pages/Comprobantes.razor` (componente compartido, acotado a
    empresa+ambiente de sesión vía `EmpresaActualService`, permisos vía
    `ISecurityDirectory`, banner DRY-RUN, tabla de pendientes con «Generar» por
    fila + enlace PDF, «Procesar lote» solo facturas) + 3 wrappers
    `/fe/facturas`, `/fe/notas-credito`, `/fe/liquidaciones`.
  - `Permisos`: `VerLiquidaciones`/`HacerLiquidacion` (`qupurchliq`/`mkpurchliq`).
    `AppCatalogo`: 3 entradas nuevas (🧾 / ↩️ / 📥). Nav dinámico → aparecen solas.
  - Host: `AddFacturacionElectronica` en `Program.cs`, ensamblado en
    `Routes.razor` + `MapRazorComponents(...).AddAdditionalAssemblies`.
  - `/retenciones`: botón **«Solicitar anulación»** en el popup de detalle →
    `SolicitudAnulacionRetencion`.
  - Tests: 3 de `MapeadorEntidades`. Smoke: el Host arranca con el módulo
    registrado, todas las rutas resuelven. **206 tests solución.**
  - **F4b — paridad de UX con Retenciones (2026-09-10):** `Data/TableroComprobantes`
    (consultas de solo lectura sobre `Facturas`+`Details`+`Persons`+`NCdetail`):
    `RecientesAsync(ruc, codDoc, desde, hasta, top)` + `DetalleAsync(facturaId)`.
    `Comprobantes.razor` reescrita: lista de **guardados** (no solo pendientes),
    columna **PDF** (`app.datil.co/ver/{id}/pdf`), **popup** de detalle (`PsaModal`:
    cabecera + persona + líneas + totales + Ver PDF / Ver XML; NC muestra el doc.
    modificado), **filtros** (número/persona/ambiente/datil) + **orden** por
    columna + contador «X de Y» + «Limpiar filtros», checkbox **«Incluir
    pendientes de emitir»**, **«Procesar lote»** para los 3 tipos con resumen.
    2 tests de `TableroComprobantes`. **208 tests solución.**
- **F5 — Deploy a `SERWEBPSA01`.** Redeploy del Host con el módulo nuevo **+ el
  fix de ícono pendiente `f4fd679`**; env vars (`Correo__*` si se usa);
  smoke test en **DRY-RUN**. Sin emisión real (decisión 2026-09-07: nada real
  hasta migrar los 26 aplicativos).

Estimación: ~8–12 días de dev, concentrados en F2 y F3a (port fiel de ~720 LOC
de lógica de esquema Sage con descuentos e IVA).

## 5. Decisiones tomadas (2026-09-07)

1. **UI**: páginas separadas (`/fe/facturas`, `/fe/notas-credito`,
   `/fe/liquidaciones`), un enlace por página en el nav, gateadas por permiso.
2. **Retenciones**: el módulo FE **no** las incluye; siguen en `/retenciones`.
3. **Lote + "solicitar anulación"**: ambos entran en este corte. Anulación
   necesita SMTP de plataforma (§3.4), remitente fijo `anulaciones@paredes.com.ec`.
4. **Permiso de liquidaciones**: códigos nuevos `qupurchliq` / `mkpurchliq`.
5. **Fixture de prueba**: empresa **SANCEV, RUC `1791313747001`** — verificado
   completo en PREDATOR (§6).

## 6. Fixture de prueba en PREDATOR — VERIFICADO 2026-09-07

Fixture: **SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.**, RUC `1791313747001`.
Todo lo necesario **ya está en PREDATOR**; el único ajuste es de config de dev
(servername, §6.3).

### 6.1 Sage 50 local — OK (smoke ODBC 32-bit ejecutado)

- Compañía: `C:\Sage\Peachtree\Company\sancialu`, motor **Actian Zen 15.11**.
- DBQ (nombre Zen, registrado en el `dbnames.cfg` local → carpeta `SANCIALU`):
  **`SANCEVCIALTDA2202520`**.
- Cadena ODBC que **funciona desde 32 bits** (probada, `OPEN OK`):
  ```
  Driver={Pervasive ODBC Client Interface};ServerName=localhost;DBQ=SANCEVCIALTDA2202520;UID=Peachtree;PWD=JCV1234;
  ```
- Datos: `JrnlHdr` 45.416 filas; hay comprobantes de venta recientes
  (`Reference` `001-003-0000135xx`, `JrnlKey_Journal=3`, `JournalEx=8`,
  fechas hasta 09/04/2026).

### 6.2 `PeachEBills` local (`PREDATOR\SQLEXPRESS`) — filas para el RUC presentes

| Tabla | Estado | Notas |
|---|---|---|
| `Transmitter` | OK | "SANCEV ELECTRICA INDUSTRIAL CIA. LTDA.", `HaveToDoAccounting=1`, `Number_Resolution_CE = NULL` (no es contribuyente especial). |
| `DatilAPI` | OK | URLs `https://link.datil.co/{invoices,credit-notes,retentions}/`; `myApiKey` 32 ch; `mySignaturePassword` 24 ch (cifrado `DbSecret`). **Confirmar con el área que la clave es de PRUEBAS / se puede usar.** |
| `CurrentAmbient` | OK | `AmbientDefault=1` (PRUEBAS), `Active=1`, `emailForTest = luis.ps@paredes.com.ec` → los comprobantes de prueba NO llegan al cliente. |
| `Establishments` | OK | `001-001`, `001-002`, `001-003` (todos `IsFromPeach=1`). |
| `PeachConnString` | OK | id 21, `dbq=sancevcialtda2202520`, `uid=Peachtree`, `pwd=4gOp/yqYXe4=` (→ `JCV1234`), **`servername=SERWEBPSA01`** (prod, ver §6.3). |

URL de liquidación: no está en `DatilAPI`; el `.exe` usa la constante
`https://link.datil.co/purchase-settlements/`. Base para PDF/XML:
`https://app.datil.co/ver/{id}/pdf|xml`.

### 6.3 Único ajuste para dev

`PeachConnString.servername` apunta a `SERWEBPSA01` (inalcanzable desde PREDATOR).
**No mutar la fila compartida.** En dev, el `PeachConnStringResolver` /
`HostResolverEmpresaSage` debe poder sobreescribir `servername → localhost` para
este RUC vía User Secret o `appsettings.Development.json` (mismo patrón que el
`Sage50:ConnectionString` del piloto). A definir en F3a.

### 6.4 Pendiente del área antes de F3a

1. Confirmar que la `myApiKey` de SANCEV en `DatilAPI` es de pruebas / se puede
   usar para emisión real contra el sandbox de Datil.
2. **Crear comprobantes ficticios en la Sage local `sancialu`**:
   - Facturas de venta: 1 simple, 1 con descuento (con y sin IVA), 1 a cliente
     con pasaporte / identificación del exterior.
   - Notas de crédito: 2, cada una referenciando una factura previa.
   - Liquidaciones de compra: 2 a un proveedor.
3. (Opcional) filas en `InvoiceConfigAditionalInfo` para probar el lector de
   info adicional de facturas.

### 6.5 Validación en F3

Correr el módulo web local apuntando a `1791313747001` con `Datil:DryRun = false`
+ `ambiente = pruebas` → comparar (a) aceptación / rechazo de Datil pruebas y
(b) filas escritas en `Facturas` / `Details` / `NCdetail` contra un trazado
manual. En producción el default sigue en `DryRun = true`.

**F1 y F2 no dependen del fixture.** F3a depende de §6.4.

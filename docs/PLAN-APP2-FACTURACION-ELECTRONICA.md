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
- **F3b — Notas de crédito.**
- **F3c — Liquidaciones de compra.**
- **F3d — Correo + "Solicitar anulación".** §3.4 (la acción se agrega a la página
  `/retenciones` existente, no al módulo FE).
- **F4 — Módulo + páginas + wiring.** §3.5.
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

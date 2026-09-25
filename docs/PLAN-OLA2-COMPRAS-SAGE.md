# Plan — Ola 2 · Compras y retenciones en venta recibidas en Sage 50 (+ Sage Bridge)

Primeros módulos de la **Ola 2** (escritura en Sage). Reemplazan cuatro piezas de escritorio:

| Pieza actual | Qué hace | Destino |
|---|---|---|
| `Sage50usIntegration` → Tareas › Compra › **Nueva** (`FrmPrintedPurchases` + `Bill`) | Factura / nota de venta / liquidación de compra manual → **Purchase Order** en Sage (SDK) | Módulo **Compras** (categoría Compras) |
| `Sage50usIntegration` → Tareas › Compra › **Desde Reporte SRI** (`FrmBillBatch` + `SRIwebReadAccessKeys` + `Bill`) | Facturas recibidas del SRI → OC en Sage | Módulo **Compras**, bandeja alimentada por **Conciliación con SRI - Docs Recibidos** |
| `Sage50usIntegration` → Tareas › **Retenciones en Venta** (`FrmTwhAtSalesBatch` + `TwhAtSale`) | Retención recibida de un cliente → **NC de cliente `4R`** aplicada a la factura de venta (SDK) | Módulo **Retenciones en venta recibidas** (categoría nueva **Ventas**) |
| `Sage.Sage50\PSComInvoiceGenerate` («Cargar compras automáticamente», uno por empresa en sesión RDP) | Convierte OC → compra por **COM** y llena `PeachEBills.PurchaseOrderSync` | **Sage Bridge** (servicio de Windows, SDK) |

Nuevo (no existe en el exe): **notas de crédito de compra**, dentro del módulo Compras (vía archivo de importación de Sage, §7).

**Estructura (decisión E, 2026-09-25)**: cada módulo contiene solo lo suyo — **Compras** = compras y sus relacionadas (NC de compra);
**Retenciones en venta recibidas** = módulo propio en la categoría **Ventas**. **Conciliación** abre el formulario de registro que corresponde
a cada uno de los 3 tipos recibidos (§4.2).

La revisión completa del legado (fase 1, 2026-09-23/25) está resumida en §1; el detalle vive en la memoria
`ola2-compras-sage-hallazgos` y en el código citado.

---

## 1. Hechos que condicionan el diseño (verificados)

1. **El exe no asienta la compra: crea una OC** (`JrnlKey_Journal=10`, `JournalEx=18`) con `PurchaseOrderFactory` del SDK
   `Sage.Peachtree.API` 2023.0.0.222 (x86). El asiento nace cuando el worker la convierte en compra (`JrnlKey 4`/`JournalEx 11`,
   `INV_POSOOrderNumber` = referencia de la OC). CPTDC: ~450–540 OC/mes; 2–15 compras/mes sin OC.
2. **Convenciones de la OC** (verificadas en CPTDC, OC-8764/OC-8768):
   `ReferenceNumber` = `OC-####`; `TermsDescription` = `ShipToAddress1` = nº factura `000-000-000000000`;
   `ShipToAddress2` = nº de retención; `ShipToState` 01 crédito / 02 costo; `ShipToZIP` `Manual` / `Externo` / vacío;
   `ShipVia` FACTURA / NOTA DE VENTA / LIQUIDACION; `GoodThruDate` = fecha de registro; cuenta AP `20000`.
   Líneas en orden: detalle con ítem «C» (`CustomField1='COMPRA'`, C1–C15) · IVA (ítem `IMPUESTO`, qty = base, unit = tarifa) ·
   propina/otros · `AUT-SRI` (descripción = clave) · R-IRF / R-IVA (qty = base, unit = −%) · 2 líneas «.».
3. **SDK 2023** (reflexión sobre la DLL del GAC_32 de PREDATOR): `PurchaseOrder` escribible; `PurchaseInvoice.AddOrderLine(PurchaseOrderLine)`
   existe (**OC → compra sin COM**); **`VendorCreditMemo` es de solo lectura** (sin `Save`/`AddLine`).
4. **COM** (`PeachwServer`, usado por el worker): importa el diario de compras (incluye `IsCreditMemo`, `ApplyToInvoiceNumber`),
   pero se engancha al Sage **abierto en el escritorio** y escribe en la empresa abierta **sin verificar el RUC**. No sirve para un servicio.
5. **WS `AutorizacionComprobantesOffline`** entrega el XML solo por **~15 días** desde la emisión (medido 2026-09-25: del 11/09 en adelante sí,
   del 09/09 hacia atrás `numeroComprobantes=0`); fallas TLS transitorias. «Abrir Reporte del SRI» del exe está roto (el portal pasó a 12
   columnas); hoy se usa «Desde Texto».
6. **`PurchaseOrderSync`** (PO→PI por RUC, 14 empresas) lo llena el worker y **lo lee el módulo web de Retenciones** para las pendientes.
7. **Numeración**: el exe calcula `MAX(Reference)` como texto con 4 dígitos y exige 7 caracteres. CPTDC iba en `OC-8768` el 12/09 a ~480 OC/mes
   → **`OC-9999` hacia el 25-nov-2026**. La web usará máximo numérico (§6.3); ver D5 para el exe.
8. NC de compra: ~1–3/mes en CPTDC; hoy se registran a mano en Sage (`JournalEx 12`).

---

## 2. Decisiones ya tomadas (usuario, 2026-09-23/25)

| # | Decisión |
|---|---|
| 1 | Alcance: Nueva (factura, nota de venta, liquidación) + Desde SRI (facturas) + **NC de compra**. |
| 2 | Se sigue escribiendo **OC**; la conversión a compra la hace el Bridge (reemplaza al worker COM). |
| 3 | El Bridge corre en **SERWEBPSA01** (mismo server que Sage, SQL e IIS). |
| 4 | Desarrollo y pruebas **solo en PREDATOR**, contra copias locales. Ningún Sage real hasta el corte. |
| 5 | «Desde SRI» se alimenta de Conciliación (extensión existente), cuidando el flujo del usuario. |
| 6 | Bugs: **se corrige lo que pierde o falsea datos; la lógica contable se porta fiel** (§8). |
| 7 | Permisos nuevos Ver/Registrar compras (7a) · auditoría de quién registra (7b) · la web **propone** el ID del proveedor nuevo (7c). |
| 8 | Documento ANULADO o de ambiente de pruebas: **advertir, no bloquear**. |
| 9 | Sin convivencia exe + web: se prueba todo, se corta y se da de baja el exe con runbook (§10 F8). |

---

## 3. Arquitectura

```
Navegador ─► PsaWeb.Host (.NET 9, x86, IIS)
               │  lectura: ODBC directo a Sage (como hoy)
               │  escritura: INSERT en cola ──► PsaWebPlataforma.TrabajosSage
               │                                      ▲   │
               │  consulta estado del trabajo ────────┘   ▼
               │                              PsaWeb.SageBridge (Windows Service, .NET FW 4.8 x86, STA)
               │                                  │  SDK Sage.Peachtree.API  ──► Sage 50 (empresa del trabajo)
               │                                  │  ODBC (idempotencia, lecturas)
               │                                  └► PeachEBills.PurchaseOrderSync
               └► WS SRI (descarga XML por clave)
```

- **Cola en SQL, no HTTP**: durable, el Bridge puede estar caído sin perder nada, sin puertos entrantes. La web solo encola y
  consulta; **nunca referencia el SDK**.
- **Contratos compartidos** en `src/PsaWeb.SageBridge.Contratos` (**netstandard2.0**): tipos de trabajo, payloads y resultados JSON.
  Los usan el Host (.NET 9) y el Bridge (.NET FW 4.8).
- **El Bridge** (`bridge/PsaWeb.SageBridge`, fuera de la solución .NET 9 o en un `.sln` propio): un hilo STA, una sesión SDK, **una empresa a
  la vez**; toma trabajos con *lease* (`TomadoPor`/`TomadoHasta`) para que un reinicio no deje trabajos colgados.
  Abre la empresa por `LookupCompanyIdentifier(servername, dbq)` de `PeachConnString` (mismo resolvedor que la web) y la **cierra al
  terminar cada lote** — si la dejara abierta, el backup nocturno de Sage fallaría.
- **Ventana de mantenimiento** configurable (hoy 22–06 h, igual que el worker): el Bridge no toca Sage; los trabajos quedan
  «En cola — se procesa a las 06:00».
- **Modo consola** del mismo ejecutable para desarrollo en PREDATOR (`PsaWeb.SageBridge.exe --consola`).
- **Secretos**: `thirdPartyApplicationKey` y cadenas de conexión solo en la config del servidor (protegida con DPAPI / variables de
  entorno), nunca en git. Se reutiliza la clave de aplicación del exe (ver F0-e: si la autorización «Allow Access» es por clave de
  aplicación, las empresas ya autorizadas para el exe no requieren repetirla).

### 3.1 Tabla `TrabajosSage` (PsaWebPlataforma, migración EF desde el Host)

`Id` · `Ruc` · `Tipo` (`CrearOc`, `ActualizarOc`, `ConvertirOcEnCompra`, `CrearRetencionRecibida`, `ProbarEmpresa`) · `Estado` (`EnCola`, `EnProceso`, `Hecho`,
`Error`, `Cancelado`) · `ClaveIdempotencia` (única por `Ruc`+`Tipo`) · `PayloadJson` · `ResultadoJson` · `Error` · `Intentos` ·
`CreadoPor` · `CreadoUtc` · `TomadoPor` · `TomadoHastaUtc` · `TerminadoUtc`.

- **Idempotencia**: `CrearOc` usa `Ruc|VendorRecordNumber|nº factura`. Antes de escribir, el Bridge vuelve a buscar por ODBC la OC de
  ese proveedor y número (`WasPreviewCreated`) y, si ya existe, marca `Hecho` con su referencia en vez de duplicar.
- **Numeración** (`OC-####`, nº de retención): se asigna **dentro del Bridge**, en el mismo trabajo que guarda, serializado por empresa
  — nunca en la web (la web muestra «se asigna al guardar»).

### 3.2 Página de estado del Bridge

`/admin/sage-bridge` (gate `Plataforma:Admins`): latido del servicio, cola por empresa, errores recientes, última conversión OC→compra por
empresa, botón «Probar empresa» (abre y cierra la compañía, sin escribir).

---

## 4. Módulos web

Dos categorías nuevas en `Categorias.Orden`: **Compras** y **Ventas** (ubicación en la barra, ver E3). Los dos módulos comparten
`src/PsaWeb.Recibidos` (XML del SRI: descarga por WS, subida, almacenamiento §13.1, parsers de factura / NC / retención, advertencias)
y el cliente de la cola del Bridge.

### 4.0 Módulo **Compras** (categoría Compras)

`modules/PsaWeb.Modules.Compras` (RCL) + `src/PsaWeb.Compras` (lógica pura, sin SDK: armado y validación de la OC y de la NC).

| Ruta | Port de | Qué hace |
|---|---|---|
| `/compras` | `FrmPrintedPurchases` | OC por rango de fechas y tipo (FACTURA / NOTA DE VENTA / LIQUIDACION); estado Guardado / Contabilizado; autorización; Nueva, Abrir, Copiar (no se copia un electrónico). |
| `/compras/nueva?tipo=01\|02\|03` | `Bill.LoadNewBill` | Captura manual (físicas, notas de venta, liquidaciones). |
| `/compras/{postOrder}` | `Bill.LoadInfoBill(DataRow…)` | Ver / actualizar una OC guardada; solo lectura si ya es compra. |
| `/compras/recibidos` | `FrmBillBatch` | Bandeja de documentos recibidos **Solo en SRI** (staging de Conciliación), pestañas **Facturas** y **NC de compra**; estado Falta XML / Pendiente / Guardado / Registrado. |
| `/compras/recibidos/factura/{clave}` | `Bill.LoadInfoBill(Factura)` | Formulario de compra precargado desde el XML; el digitador personaliza y guarda. |
| `/compras/recibidos/nc/{clave}` | nuevo | NC de compra desde su XML → archivo de importación (§7). |

**Formulario de compra** (el corazón del port de `Bill.cs`, ~3.000 LOC): emisor/proveedor (buscar por RUC, crear/editar, ID propuesto),
sustento crédito/costo, forma de pago «Aplica / No aplica retención» (`AboutApplyTwh`), grilla de detalle (código, cantidad, ítem, cuenta,
precio, IVA, RetRF, RetIVA, job), «Resumir detalles», impuestos, pestaña Retención (serie y número, líneas, cuenta asumida), totales y NETO,
panel de advertencias. Guardar = encolar `CrearOc`/`ActualizarOc` y mostrar el avance del trabajo.

El mismo componente de formulario sirve a `/compras/nueva`, `/compras/{postOrder}` y `/compras/recibidos/factura/{clave}` (como `Bill`
en el exe); cambia solo el origen de los datos.

### 4.2 Registrar desde Conciliación (los 3 tipos)

En las filas **«Solo en SRI»** de Conciliación aparece **«Registrar en Sage»**, que abre el **mismo formulario** del módulo dueño del tipo
(no una ventana aparte ni una copia):

| Tipo recibido | Abre | Módulo / permiso |
|---|---|---|
| Factura | `/compras/recibidos/factura/{clave}` | Compras · `mkpurchinv` |
| NC de compra | `/compras/recibidos/nc/{clave}` | Compras · `mkpurchinv` |
| Retención en venta | `/ventas/retenciones-recibidas/{clave}` | Retenciones en venta recibidas · `mktwhrec` |

- El botón aparece solo si el usuario tiene el permiso de registrar del módulo de destino.
- El enlace lleva `?volver=conciliacion`: al guardar, el formulario ofrece «Volver a Conciliación» con los filtros como estaban.
- Cuando Sage ya tiene el documento, la fila sale sola de «Solo en SRI» en la siguiente conciliación (Conciliación ya lee `AUT-SRI` de la
  OC vinculada, las NC de compra por su propio `AUT-SRI` y las retenciones por `4R`). No se cambia la lógica de conciliación.

### 4.3 Módulo **Retenciones en venta recibidas** (categoría Ventas)

`modules/PsaWeb.Modules.RetencionesRecibidas` + `src/PsaWeb.RetencionesRecibidas` (lógica pura).

| Ruta | Port de | Qué hace |
|---|---|---|
| `/ventas/retenciones-recibidas` | `FrmTwhAtSalesBatch` | Bandeja de retenciones recibidas **Solo en SRI** con estado (cliente no encontrado / duplicado / pendiente / guardado), como el exe. |
| `/ventas/retenciones-recibidas/{clave}` | `TwhAtSale` | Formulario precargado desde el XML de la retención: cliente, factura de venta que retiene, líneas IR / IVA, cuentas. |

Escritura: trabajo `CrearRetencionRecibida` del Bridge → **`CustomerCreditMemo`** por SDK (escribible, a diferencia de la NC de proveedor)
con `CustomerPurchaseOrderNumber = "4R"`, aplicada a las líneas de la factura de venta, como hace hoy `TwhAtSale.cs`.

- **Pendiente de revisión fiel** (`TwhAtSale.cs` 652 LOC + `FrmTwhAtSalesBatch.cs` 374 LOC): se hace al inicio de F7b, con el mismo método
  que compras (lectura + verificación contra datos de CPTDC). Su carga por archivo usa el lector viejo del TXT, probablemente roto por el
  cambio de formato del portal (§1.5).
- **Mejora que entra aquí** (backlog del ATS, `ESTADO-MIGRACION-WEB.md` §9): alertar cuando la fecha de la retención no coincide con el
  período de la factura de venta, con botón «Pasar a período correcto».

### 4.4 Obtención del XML (los 3 tipos, `src/PsaWeb.Recibidos`)

1. Si la clave está dentro de la ventana del WS (~15 días), el **servidor** descarga el XML (`AutorizacionComprobantesOffline`, 3 reintentos
   como `ConsultaComprobanteClient`) y lo guarda en `XmlComprobantesRecibidos` (§13.1) para no depender de la ventana otra vez. Lo hace
   apenas llega el reporte de la extensión, no cuando el digitador abre la fila.
2. Si está fuera de la ventana: el formulario pide **subir el XML** descargado del portal (arrastrar archivo). Descarga automática por la
   extensión = D2 (§13).
3. Advertencias (no bloquean): estado ANULADO (vía `IVerificadorEstadoSri`), ambiente de pruebas, comprador ≠ RUC de la empresa
   (confirmación, como el exe), total del XML ≠ suma de líneas.

Parser: port a .NET 9 de lo que usa `ImportXmlLib.FacturaE` (infoTributaria, infoFactura, detalles/impuestos, totalConImpuestos, pagos,
propina + otrosRubrosTerceros), con `InvariantCulture`. NC: parser nuevo sobre el mismo patrón (`infoNotaCredito`, `numDocModificado`).
Retención: port de lo que usa `ImportXmlLib.RetencionE` (versiones 1.0 y 2.0 del esquema; se confirma en F7b).

---

## 5. Lógica a portar fiel (`src/PsaWeb.Compras`)

Funciones puras, testeables sin Sage, alimentadas por catálogos leídos por ODBC:

- **Catálogos** (lectores ODBC nuevos, patrón de `LectorProveedor`): ítems de detalle (Stock/NonStock/Serialized, excluye IMPUESTO/COMPRA/
  R-IRF/R-IVA y `AUT-SRI`), ítems C, IMPUESTO, R-IRF, R-IVA (con `CustomField1`=código, `CustomField2`=%), cuentas, jobs, proveedor por RUC
  (`Address.Country`/`CustomField3`/`CustomField4`) y por ID; de `PeachEBills`: `AboutApplyTwh`, `dicTaxRate`, `dicImpuestosTipo`,
  `PaymentTypes`, `IdentityType`, `Establishments`, `VendorConfiguration`.
- **Ítem C** (`CorrectToLoad`): tabla IVA sí/no × tipo no-IVA (Imponible 0 % / NoGraIVA / ImpExe) × RF sí/no × 332/332G/332I según forma de
  pago (1 tarjeta → 332G, 2 débito → 332I, resto → 332); regla 332 con retención 332*.
- **IVA** (`UpdateTaxLines` / XML): una línea por tarifa; ítem IMPUESTO por tarifa; cuenta = cuenta de inventario del ítem.
- **Retenciones** (`CheckTwhApply`): RF agrupada por ítem sobre la base; RIVA sobre el IVA de la línea; **322 seguros** = 10 % de la base +
  línea de gasto por el 90 %; **retención asumida** (forma de pago 7) = línea espejo a la cuenta de gasto; «no aplica» = línea 0 % 332*.
- **Resumir detalles**: agrupar por (IVA, RetIVA, RetRF).
- **Validaciones de proveedor y tipo** (RUC ≠ liquidación; 06/08 solo liquidación; 05 solo liquidación; nota de venta sin retención;
  email si hay retención; cuenta de gasto; datos ATS del exterior en `CustomField1` JSON de 7 valores).
- **Proveedor** (`LoadVendor`): crear/actualizar con las convenciones (`OurAccountWithThem` tipo, `Address.Country` identificación, nombre
  partido `Name`(30)+`CustomField0`, dirección 30+30, `PhoneNumber2` «OC-/SC-» + «CE#n#», reactivar si estaba inactivo).
- **Mapeo aprendido** código de proveedor → ítem de Sage (`VendorConfiguration`), con el diálogo de confirmación cuando cambia.

### 5.1 ID propuesto del proveedor nuevo (decisión 7c)

Razón social en mayúsculas, sin tildes ni caracteres que Sage rechaza (`* ? + ½`), recortada al largo de `VendorID` (20); si ya existe
(case-insensitive), sufijo `-2`, `-3`… El digitador puede editarlo; la validación de «ID ya usado» se mantiene. Se revalida en el Bridge.

---

## 6. Escritura en Sage (Bridge)

### 6.1 `CrearOc` / `ActualizarOc`
Replica `LoadPurchaseOrder` (cabecera, orden de líneas y campos de §1.2) con estas diferencias (§8):
- **Actualizar es atómico**: se arma y `Validate()` la nueva OC **antes** de borrar la anterior; si algo falla, la anterior queda intacta.
  El exe borra primero.
- Si `Validate()` falla, el error vuelve a la web (el exe lo perdía en silencio).
- Proveedor (crear/actualizar) en el mismo trabajo, antes de la OC.

### 6.2 `ConvertirOcEnCompra` (reemplazo de `PSComInvoiceGenerate`)
Programado dentro del Bridge (cada N minutos por empresa habilitada, fuera de la ventana de mantenimiento) **y** encadenado al terminar un
`CrearOc` (así la compra aparece casi al instante, como hoy con el timer de 30 s).
- **Mismo filtro** que el worker: OC con filas no recibidas (`ROUND(StockingQtyReceived,2) < ROUND(Quantity,2)`), `ShipToAddress1` no vacío,
  no `ANULAD%`, últimos 12 meses, sin fila en `PurchaseOrderSync`.
- **Mismos campos**: nº factura = `ShipToAddress1`, fecha = fecha de la OC, vencimiento = `GoodThruDate`, términos, ShipVia, ShipTo 1/2/State,
  cuenta AP, cada línea aplicada a su línea de OC (`AddOrderLine`).
- **Verifica la empresa**: el Bridge abre la compañía del RUC — desaparece el riesgo del worker de escribir en la empresa equivocada.
- Inserta `PurchaseOrderSync` (POPostOrder, PIPostOrder, RUC) en el mismo paso; Retenciones sigue funcionando sin cambios.
- Empresas habilitadas: tabla/config por RUC (hoy 14 en `PurchaseOrderSync`); se activa una a una en el corte.

### 6.3 Numeración
Máximo **numérico** de `Reference` por prefijo (`OC-`, `10-`, `20-`…), sin tope de 4 dígitos (`OC-10000`). Nº de retención: el mismo cálculo
del exe (`twhNumNext` sobre `ShipToAddress2`, serie del establecimiento electrónico, `startNumerationTwh`) **más** la validación de duplicado
que el exe tenía escrita y nunca llamaba (`CheckForTwhNumberAlreadyUsed`).

---

## 7. Notas de crédito de compra

El SDK no puede crearlas (§1.3) y COM exige un Sage abierto en un escritorio. Con ~1–3 NC/mes por empresa:
1. La contadora abre la NC recibida (desde Conciliación «Solo en SRI» tipo NC, o subiendo el XML).
2. La web arma la NC: proveedor, factura que modifica (`numDocModificado` → compra en Sage por `Reference` + proveedor), líneas con los
   mismos ítems/cuentas de la compra original, IVA y `AUT-SRI` con la clave de la NC (convención que ya lee Conciliación).
3. Genera el **archivo de importación del diario de compras de Sage** (CSV con «Credit Memo» = TRUE y «Apply to Invoice Number»).
4. La contadora lo importa en Sage (Archivo › Importar › Diario de compras). La web registra la auditoría y la NC aparece conciliada.

F0-d confirma el formato exacto del CSV importándolo a mano en la copia de PREDATOR. Si el volumen crece, se reevalúa COM.

---

## 8. Port fiel: qué se corrige y qué no

| Comportamiento del exe | Decisión |
|---|---|
| Actualizar borra la OC antes de guardar la nueva; si falla, se pierde | **Corregir** (atómico, §6.1) |
| `Validate()` fallido sin mensaje | **Corregir** (error visible) |
| Ítem de IVA: si ninguno calza con la tarifa usa el primero; `Contains("5%")` calza con «15%» | **Corregir**: tarifa exacta; error si no existe el ítem |
| Recargar OC guardada asume IVA 12 % fijo | **Corregir**: tarifa desde la línea de IVA de la OC |
| Nº de retención duplicado no se valida | **Corregir** (§6.3) |
| Numeración `OC-####` por MAX de texto, tope 4 dígitos | **Corregir** (§6.3) |
| Total del XML ≠ suma de líneas, ANULADO, ambiente de pruebas | **Advertir** (no bloquea) |
| Descuentos: sin línea propia (el monto ya viene neto) | Fiel |
| Líneas del XML con total 0 se descartan | Fiel |
| Ítems C, 332/332G/332I, 322 al 10 %, retención asumida, orden y campos de la OC | Fiel |
| Worker: escribe en la empresa abierta sin verificar RUC | Desaparece (el Bridge abre la empresa del trabajo) |

Cada corrección queda como `// Corrección Cn` en el código con su test; cada fidelidad dudosa como `// Bug Bn` (doctrina §6 de
`ESTADO-MIGRACION-WEB.md`).

---

## 9. Permisos y auditoría

- Llaves nuevas en `allowAction` (`allowCode` ≤ 10), **un par por módulo**:
  - Compras: **`qupurchinv`** «Ver compras» y **`mkpurchinv`** «Registrar compras» (incluye NC de compra).
  - Retenciones en venta recibidas: **`qutwhrec`** «Ver retenciones en venta recibidas» y **`mktwhrec`** «Registrar retenciones en venta recibidas».
  - Script idempotente `docs/sql/permisos-compras-ventas.sql` que copia los roles de `qupurchtwh`/`mkpurchtwh` (compras) y de
    `qusaleinv`/`mksaleinv` (ventas). `GateProvisional` mientras el área no las asigne.
- **Auditoría** en `PsaWebPlataforma.AuditoriaRegistrosSage` (una tabla para ambos módulos): usuario, fecha, RUC, módulo, acción (crear/actualizar
  OC, crear proveedor, generar archivo de NC, registrar retención), referencias (OC, PostOrder, documento, clave, trabajo), resultado y huella
  del payload y del XML de origen. Visible en el detalle del documento.

---

## 10. Fases

Cada fase se cierra con tests + validación contra datos (doctrina del proyecto). **Nada se escribe fuera de las copias de PREDATOR hasta F8.**

| Fase | Contenido | Cierre |
|---|---|---|
| **F0** Spikes en PREDATOR (scripts descartables, sin código de producto) | a) SDK desde **consola y como servicio** con cuenta dedicada, sin Sage abierto: abrir/cerrar la copia de prueba · b) crear una OC idéntica a una real (OC-8764) y compararla fila a fila por ODBC · c) `AddOrderLine`: compra resultante vs la del worker (104467) · d) importar a mano un CSV de NC · e) ¿el «Allow Access» del exe cubre al Bridge con la misma clave? · f) ¿una sesión SDK ocupa licencia de usuario? · g) comportamiento si Sage está en backup | Informe en este documento; D3–D4 cerradas |
| **F1** Sage Bridge base | Contratos (netstandard2.0), tabla `TrabajosSage`, servicio + modo consola, lease, ventana, logs, `ProbarEmpresa`, `/admin/sage-bridge` | Trabajo de prueba de punta a punta en PREDATOR |
| **F2** Lógica pura `PsaWeb.Compras` | Catálogos ODBC, ítem C, IVA, retenciones (322, asumida, 332*), resumir, validaciones, proveedor + ID propuesto, parser XML factura | Reconstruir OC reales de CPTDC desde su XML y comparar líneas con Sage (0 diferencias) |
| **F3** Escritura de OC | `CrearOc`/`ActualizarOc` (atómico), proveedor, numeración, idempotencia | OC escritas en la copia = OC del exe para las mismas facturas (comparación ODBC campo a campo) |
| **F4** Módulo Compras — manual | `/compras`, `/compras/nueva`, `/compras/{postOrder}`, Copiar, permisos, auditoría, aviso de numeración (D5) | Prueba del usuario en PREDATOR con casos reales (factura física, nota de venta, liquidación, retención asumida, 322) |
| **F5** Facturas recibidas | `src/PsaWeb.Recibidos` (almacén de XML §13.1 con vigencia 7 años, descarga por WS al subir el reporte, subida manual), bandeja `/compras/recibidos`, «Registrar en Sage» desde Conciliación (§4.2, deja listos los 3 destinos), `VendorConfiguration`, advertencias; **medir** cuántos documentos quedan fuera de la ventana | Lote real de un día de CPTDC registrado en la copia |
| **F5b** (condicional) | Descarga de XML por la extensión (§13.3), solo si la medición de F5 lo justifica; requiere HAR de una sesión real | Prueba con la contadora en el portal |
| **F6** OC → compra en el Bridge | `ConvertirOcEnCompra` + `PurchaseOrderSync` + programación | Compras idénticas a las del worker; Retenciones (DryRun) las ve como pendientes |
| **F7** NC de compra | Parser NC, armado, CSV, pestaña NC de la bandeja, auditoría | NC importada a mano en la copia, conciliada por el módulo de Conciliación |
| **F7b** Retenciones en venta recibidas | Revisión fiel de `TwhAtSale`/`FrmTwhAtSalesBatch` → plan detallado en este documento → categoría Ventas, módulo, parser de retención, `CrearRetencionRecibida` (NC de cliente `4R`), alerta de período | Retenciones registradas en la copia = las del exe para los mismos documentos; conciliadas por Conciliación |
| **F8** Corte en SERWEBPSA01 | Instalar servicio, config/secretos, «Allow Access» por empresa, permisos, activar empresa por empresa, **apagar el worker y retirar del exe los menús Compra y Retenciones en Venta**, runbook de rollback | Runbook `docs/DESPLEGAR-OLA2-COMPRAS-VENTAS-EN-SERWEBPSA01.md` + script, como los anteriores |

Orden confirmado F1→F8 (D4), con F5b condicional y F7b antes del corte.

---

## 11. Riesgos

| Riesgo | Mitigación |
|---|---|
| El SDK no funciona como servicio (perfil, rutas de datos de Sage) | F0-a; si falla, el Bridge corre como tarea programada «al iniciar» con una cuenta dedicada (sin RDP diario) |
| Una sesión SDK consume licencia o bloquea el backup | F0-f/g; cerrar la empresa tras cada lote y respetar la ventana 22–06 |
| Duplicar una OC por reintento | Idempotencia en `TrabajosSage` + verificación ODBC previa (§3.1) |
| Numeración en paralelo con el exe | Decisión 9: no hay convivencia; el corte es por empresa |
| El WS del SRI deja de entregar XML o cambia la ventana | Subida manual del XML siempre disponible (§4.4) |
| `OC-9999` en CPTDC antes del corte (~25-nov) | D5: aviso en `/compras` desde `-9900`; parche del exe solo si se decide |
| Fidelidad del formulario (~3.000 LOC, reglas implícitas) | F2 validado contra OC reales antes de escribir nada |

---

## 12. Decisiones (2026-09-25)

- **D1 — cerrada**: categoría nueva **Compras** en el menú (y **Ventas**, ver E1).
- **D2 — cerrada**: almacén de XML en F5 con **vigencia de 7 años**; descarga por la extensión = F5b, condicionada a la medición (§13).
- **D3 — cerrada**: el Bridge corre con un **usuario local dedicado** de SERWEBPSA01 con permisos sobre la carpeta de datos de Sage (F0-a lo valida).
- **D4 — cerrada**: orden **F1→F8**, sin adelantar F6.
- **D5 — cerrada**: no se parcha el exe por ahora. La web numera sin tope (§6.3) y además, mientras el exe siga en uso, `/compras`
  muestra un **aviso cuando un prefijo pasa de `-9900`** («la numeración OC- del exe se agota en N comprobantes») para decidir a tiempo.
- **E1 — cerrada**: cada módulo contiene solo lo suyo. **Compras** (categoría Compras) = compras + NC de compra; **Retenciones en venta
  recibidas** = módulo propio en la categoría nueva **Ventas**, que agrupará lo relacionado con ventas.
- **E2 — cerrada**: Conciliación abre el formulario de registro del módulo dueño de cada uno de los 3 tipos recibidos (§4.2).
- **E3 — cerrada**: **Compras** y **Ventas** son categorías (grupos «padre») nuevas del menú. No se mueve ningún módulo existente ahora;
  la reasignación de módulos a estos grupos se hace al terminar la Ola 2.

---

## 13. D2 — XML de documentos recibidos y descarga fuera de la ventana del WS (aprobado)

### 13.1 Almacenamiento (aplica también al XML que baja el servidor por WS y al que se sube a mano)
Tabla `XmlComprobantesRecibidos` en `PsaWebPlataforma` (migración EF), **una fila por `Ruc` + `ClaveAcceso`**:
`Ruc` · `ClaveAcceso` · `TipoComprobante` · `FechaEmision` · `FechaAutorizacion` · `Xml` (el documento completo tal como llegó,
comprimido gzip en `varbinary`) · `HashSha256` · `Origen` (`WsSri` / `Extension` / `SubidaManual`) · `SubidoPor` · `FechaCargaUtc`.

- **Validación al guardar** (el servidor no confía en la extensión): el XML parsea, su `claveAcceso` interna = la clave de la fila,
  el receptor (`identificacionComprador` en factura y NC, `identificacionSujetoRetenido` en retención) = RUC de sesión, y es factura, NC o
  retención. Si no, se rechaza con el motivo.
- **Inmutable**: se inserta una vez. Si más adelante llega otro XML distinto para la misma clave (hash diferente), no se pisa: se registra el
  conflicto para revisión.
- **Tamaño**: ~8–10 KB por factura sin comprimir; CPTDC ≈ 500/mes → ~5 MB/mes, ~2 MB/mes comprimido. No es un problema de espacio.

### 13.2 Vigencia
**Decidido (2026-09-25): conservar 7 años** (plazo de conservación tributaria de los comprobantes) contados desde la fecha de emisión, y
purgar después con un job anual. El XML sigue existiendo en el portal del SRI; esta copia es de trabajo y de respaldo del registro.

### 13.3 Flujo de la extensión (mismo botón, sin pasos nuevos para la contadora)
1. La contadora consulta en el portal y aprieta «Subir a PSA», como hoy.
2. PSA procesa el reporte y **responde con la lista de claves de facturas, NC y retenciones que todavía no tienen XML**. Antes de responder, el servidor ya
   intentó el WS para las que están dentro de la ventana; solo quedan las viejas.
3. La extensión descarga esas pocas **una por una** desde la misma pantalla de resultados, con la sesión que la contadora ya abrió (réplica
   del enlace de XML de cada fila, igual que hoy con «Descargar reporte»), con una pausa entre descargas y avance visible («XML 3 de 12»).
4. Sube cada XML a `POST /compras/api/xml` con el token de la extensión que ya existe. El servidor valida (§13.1) y responde.
5. Límites duros: si aparece un captcha, la sesión expiró o el portal cambió, **se detiene** y muestra el motivo. Nunca intenta resolver un
   captcha ni ingresa credenciales. Lo que falte queda para la subida manual.

### 13.4 Después de la descarga
- En la bandeja de su módulo (`/compras/recibidos` o `/ventas/retenciones-recibidas`) la fila pasa de «Falta XML» a **Pendiente** y se
  registra como cualquier otra (formulario precargado → OC → compra, o → NC de cliente `4R`).
- La NC pasa a «Lista para generar archivo de importación» (§7).
- Conciliación gana un «Ver XML» en su popup sin trabajo extra.
- Auditoría: cada compra registrada guarda qué XML (hash) la originó.

### 13.5 Qué falta saber y cuándo se hace
- **No está confirmado** cómo descarga el portal el XML de cada fila (id del enlace JSF, parámetros, si pide captcha, si se aplica a todas las
  filas o solo a las visibles). Se confirma igual que en la F0 de Conciliación: una sesión real de la contadora con DevTools grabando
  (HAR); el asistente no inicia sesión ni maneja credenciales.
- Recomendación: construir §13.1 y la subida manual en **F5** (se necesitan igual) y **medir** cuántos documentos quedan fuera de la ventana.
  Si el volumen justifica la automatización, se agrega §13.3 como **F5b** (solo extensión + endpoint; el resto ya existe).

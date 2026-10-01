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
  entorno), nunca en git. Se reutiliza la clave de aplicación del exe, pero la F0 mostró que la autorización de Sage va atada al
  **ejecutable y a la cuenta de Windows** (§14.2, §14.6): cada empresa se autoriza una vez para el anfitrión del Bridge.

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

---

## 14. Informe de la F0 (en curso)

Spike descartable (consola .NET FW 4.8 x86 en el scratchpad de la sesión, **fuera del repo**), SDK 2023.0.0.222 del GAC de PREDATOR,
clave de aplicación del exe leída en tiempo de ejecución (nunca impresa ni copiada).

### 14.1 Resultados (2026-09-25)
- **F0-a (parcial)**: el SDK **abre sesión desde una consola sin Sage abierto** (`Begin` + `CompanyList`: 31 compañías en PREDATOR). Falta
  probarlo como servicio con la cuenta dedicada.
- **Hallazgo — errores que envenenan el proceso**: tras un error de base (Btrieve 12, carpeta inexistente de «CPTDC 2024-2025»), **todas** las
  llamadas siguientes del mismo proceso fallan con «You must call SetDefaultDatabase…», **aun con sesiones nuevas**. Consecuencia para el
  Bridge (F1): ante un error de base de datos del SDK, **reciclar el proceso** (proceso de trabajo hijo supervisado, o reinicio controlado del
  servicio) en vez de reintentar en el mismo proceso.
- **Hallazgo — nombre de base**: el `DatabaseName` del SDK (p. ej. `cptdc220242025`) no coincide con el DBQ que usa el ODBC local de PREDATOR
  (alias `CPTDCECUADORSA202520` agregado a mano en `dbnames.cfg`). En el server el exe usa `PeachConnString.dbq` para ambos. El Bridge debe
  tomar el nombre del SDK de `PeachConnString.dbq` y no asumir que coincide con un alias local.
- **F0-e**: las compañías locales (CPTDC, Roller Dance, SANCEV, Efemedio, DGRV) traen `APIACCSS.DAT` del respaldo y responden
  `VerifyAccess = NoCredentials` (valores posibles: None, Pending, Denied, Granted, CorruptedOrTampered, LoginRestricted, CompanyLocked,
  NoCredentials): la clave del exe está autorizada **con credenciales de usuario de Sage**. El Bridge necesita autorización **sin login**
  («Always allow»), que se concede una vez por compañía desde Sage; el asistente no maneja credenciales.

### 14.2 Copia de prueba y autorización (2026-09-25)
- Copia creada por el usuario: **«PUEBAS CPTDC ECUADOR S A 2025-2026»**, base SDK `cptdcecuadorsa202525`, carpeta `cptecusa` (respaldo al
  23/09, más reciente que la fixture local). El spike tiene **lista blanca**: solo pide acceso / escribe en esa base y ese nombre.
  La compañía tiene **seguridad de usuarios** (roles, claves).
- Aviso de Sage 2023 («Third Party Application Access»): muestra **aplicación = clave + nombre del .exe**, autor, «sin certificado», y las
  opciones *Always allow access / Only allow access this time / Always deny access / Remind me later*. Se eligió **Always allow access**.
- **Hallazgo clave — la autorización va atada al ejecutable de entrada**: recompilar el `.exe` (cambia su hash) devolvió
  `VerifyAccess = NoCredentials` en la misma compañía. El usuario confirma que hoy **cada actualización del exe exige re-autorizar cada
  empresa**. Prueba de la salida: anfitrión fijo `SpikeHost.exe` que carga la lógica desde `SpikeLogica.dll` → se aprobó una vez, se cambió
  **solo la DLL** (hash distinto) y el acceso **siguió en `Granted`**.
  **Decisión de diseño para F1**: el Bridge = **anfitrión mínimo y estable** (servicio, sin lógica) + **DLLs de lógica** actualizables sin
  re-autorizar. El anfitrión solo cambia en casos excepcionales (y entonces se re-autoriza una vez por empresa). Firma de código con
  certificado = mejora opcional a evaluar (el aviso lo pide para «verificar al desarrollador»).
- La documentación del SDK (kit 2018 instalado, `Sage.Peachtree.API.XML`) define `NoCredentials` como «la aplicación no tiene credenciales
  con la compañía; usar `RequestAccess` para crearlas» y `Open` usa «credenciales basadas en el entorno de la aplicación».

### 14.3 F0-b — OC creada por SDK = OC del exe (✅)
OC-8903 (PostOrder 104846, creada por el exe) re-creada como **OC-8904** (104864) con el mismo orden de llamadas que `LoadPurchaseOrder`,
factura y retención de prueba `999-999-000000001`. Comparación ODBC de **todas** las columnas (168 de `JrnlHdr`, 47 de `JrnlRow`, 8 filas):
iguales salvo identidad (PostOrder, GUIDs, nº interno, fechas de grabado), los datos de prueba, el estado de recepción y dos detalles:
- Fila 0 (CxP): el exe/worker deja el nombre del proveedor **cortado a 30** (todas las OC con nombre largo lo muestran así: probable límite
  de `VendorName` del import COM); el SDK deja el nombre completo. Cosmético.
- `CcStoredAcctRef` / `DrctDpstGUID1-4`: ceros en las del exe, espacios en las del SDK. Campos de tarjeta / depósito directo; cosmético.
Numeración: el spike calculó `OC-8904` con el máximo **numérico** del prefijo (§6.3).

### 14.4 F0-c — compra por `AddOrderLine` vs compra del worker COM (✅ con 3 puntos para F6)
OC-8904 convertida por SDK en la compra 104865 con los campos del worker (factura = `ShipToAddress1`, fecha de la OC, vencimiento =
`GoodThruDate`, términos, ShipVia, ShipTo 1/2/State, AP 20000, solo líneas con cantidad pendiente → 4 líneas). Contra la compra del worker
(104847): **montos, cantidades, cuentas, fechas, enlace a la OC (`LinkToAnotherTrx`) y número de factura iguales**. Diferencias a resolver
en F6:
1. **`IncludeInInvLedger`** en las líneas: 1 (worker; las 1.353 líneas de compras del worker de sep-2026 lo tienen) vs 0 (SDK). No lo lee
   ningún módulo web ni el legado; hay que medir su efecto con una compra de un **ítem de stock** (Kardex/inventario) en la copia.
2. **`DiscountDate`**: vacío (worker) vs calculado por los términos del proveedor (SDK) → fijarlo explícitamente.
3. **`POSOIsClosed` por fila** de la OC recibida: 1 (worker) vs 0 (SDK); la cabecera queda cerrada en ambos. Revisar en el reporte de OC
   abiertas de Sage.

### 14.5 F0-d — NC de compra por archivo de importación (✅)
Plantilla: export de Sage 2023 *Accounts Payable › Purchase Journal* (53 columnas, con *Include headings*), que trae una NC real
(`200-111-000000018`: filas negativas aplicadas por `Apply to Invoice Distribution`, más `AUT-SRI` sin aplicar). Cinco intentos sobre la
compra de prueba, importados a mano por el usuario en la copia:

| Variante | Resultado |
|---|---|
| Copia fiel de la NC real (`AUT-SRI` cantidad 0, sin aplicar) | ❌ «Line 4 · Quantity». **Un error en una línea descarta toda la NC** (no quedan NC a medias) |
| 1 · solo C1 + IVA aplicadas | Entra y se aplica, pero montos **×100** |
| 2 · + `AUT-SRI` cantidad −1 sin aplicar | Entra, montos ×100 |
| 3 · + `AUT-SRI` cantidad 0 con nº de factura | ❌ «Apply to Invoice Distribution» |
| 4 · dinero con punto (`200.00`) | Entra, montos ×100 |
| **5 · todo en formato regional** (`"-1,00"`, `"200,00"`, `"-200,00"`) | ✅ **Exacta**: 200 + 30, aplicada a la compra (`LinkToAnotherTrx`), `JournalEx 12`, estructura igual a la NC real |

Reglas para F7:
- Sage **recalcula el precio unitario** (monto ÷ cantidad) y lee **cantidad, precio y monto con la configuración regional de la sesión
  que importa** (PREDATOR: es-ES, coma decimal). El **export** de Sage escribe el monto con punto: no es reimportable tal cual.
  → el CSV se genera con la cultura de la sesión de importación (configurable por empresa; se verifica en el server en F8).
- Fechas `d/M/yy` (misma regla regional). Líneas negativas; `Apply to Invoice Number` + `Apply to Invoice Distribution` = nº de línea de
  la compra; `AUT-SRI` **con cantidad −1**, sin nº de factura y distribución 0 (con cantidad 0 el import lo rechaza).
- La fila 0 (CxP) también queda con el nombre del proveedor cortado a 30 (confirma que es el límite del import, como en el worker).
- Quedaron en la copia NC de prueba `999-999-000000003/4/6` (montos ×100) y `…007` (correcta), todas aplicadas a la compra de prueba.

### 14.6 F0-a — SDK como servicio de Windows con cuenta local dedicada (✅)
Servicio temporal `PsaSpikeF0` (anfitrión `SpikeServicio.exe` + la misma `SpikeLogica.dll`) con la cuenta local estándar
`svc-sagebridge-f0` (miembro de Usuarios, derecho «Iniciar sesión como servicio»), creada por el usuario con los scripts de la F0.
- Corre en **sesión 0, no interactiva**; `PeachtreeSession.Begin` funciona.
- Primera corrida: `NoCredentials` → `RequestAccess = Pending` (queda en `APIACCSS.DAT`). **Sage abierto por otra cuenta de Windows no
  muestra el aviso; aparece al volver a abrir la compañía.** Tras aprobar: `Granted`, compañía abierta en **8,7 s**, 16.123 OC leídas.
- **La autorización es por ejecutable Y por cuenta de Windows**: el mismo SDK con otro `.exe` o con otra cuenta pide aprobación propia.
- Permisos: en PREDATOR no hizo falta ninguno extra (las carpetas de Sage dan *Modificar* a Usuarios autenticados). Revisar en el server.
- **Procedimiento de alta de una empresa en el Bridge (F8)**: con el servicio instalado, el Bridge pide acceso a la empresa → alguien
  abre esa empresa en Sage → *Always allow access*. Una sola vez por empresa mientras no cambie el anfitrión.
- Rendimiento: abrir una compañía cuesta **~9–12 s** → el Bridge agrupa los trabajos por empresa y la cierra al terminar el lote.

### 14.7 F0-f / F0-g — licencia y backup con una sesión SDK abierta (✅)
El spike mantuvo la copia abierta por SDK 8 min (12:05:09–12:13:10), leyendo cada 30 s, mientras el usuario operaba Sage:
- **Licencias**: *User Security* siguió mostrando **37 licenses remaining** → la sesión SDK **no consume licencia** de usuario. Pero
  Sage abrió *User Security* **solo lectura** («other users are accessing this company»): la sesión SDK cuenta como usuario conectado.
- **Backup**: *File › Back Up* arrancó unos segundos y falló con **«Another user or application is processing data in Sage 50. Please ask
  all users to log out…»**. Las lecturas del SDK siguieron OK durante el intento y la compañía se cerró bien al final.
- Consecuencias para el Bridge (F1): **no mantener compañías abiertas en reposo** (abrir → procesar el lote → cerrar); **ventana de
  mantenimiento** configurable que coincida con los backups automáticos de cada empresa (hoy 22–06 h), durante la cual el Bridge no
  abre compañías; y ante un trabajo que llega en esa ventana, queda en cola con el aviso correspondiente.

### 14.8 Cierre de la F0 (2026-09-25)
| Prueba | Resultado |
|---|---|
| F0-a SDK sin Sage abierto / como servicio con cuenta dedicada | ✅ (sesión 0; autorización por .exe **y** por cuenta; aviso al reabrir la compañía) |
| F0-b OC por SDK = OC del exe | ✅ (solo diferencias cosméticas) |
| F0-c OC → compra por `AddOrderLine` = worker COM | ✅ con 3 puntos para F6 (`IncludeInInvLedger`, `DiscountDate`, `POSOIsClosed` por fila) |
| F0-d NC de compra por CSV de importación | ✅ con formato regional de la sesión que importa; `AUT-SRI` con cantidad −1 |
| F0-e autorización | ✅ atada al .exe de entrada → **anfitrión fijo + DLLs** (probado) |
| F0-f licencia | ✅ no consume licencia; sí cuenta como usuario conectado |
| F0-g backup | ✅ el backup falla con la compañía abierta → abrir/cerrar por lote + ventana de mantenimiento |

Requisitos nuevos para F1 surgidos de la F0: anfitrión mínimo estable + DLLs de lógica; reciclar el proceso de trabajo ante errores de base
del SDK (§14.1); cuenta local dedicada fija; `VerifyAccess` en la misma sesión antes de `Open`; abrir/cerrar la compañía por lote;
ventana de mantenimiento por empresa; `DatabaseName` desde `PeachConnString.dbq`.

Datos de prueba que quedaron en la copia (a limpiar restaurándola): OC-8904 (104864), compra 104865 (`999-999-000000001`) y las NC
`999-999-000000003/4/6/7`. En PREDATOR queda instalado el servicio temporal `PsaSpikeF0` con la cuenta `svc-sagebridge-f0`
(`C:\PSA-F0\servicio\03-limpiar-F0.ps1` lo borra).

---

## 15. F1 — Sage Bridge base (implementada 2026-09-25)

### 15.1 Qué se construyó
| Pieza | Ubicación | Notas |
|---|---|---|
| Contratos y reglas puras | `src/PsaWeb.SageBridge.Contratos` (netstandard2.0) | `TiposTrabajo` (F1: `ProbarEmpresa`), `EstadosTrabajo`, `VentanaMantenimiento` (cruza medianoche), `ClasificadorErroresSage` (textos reales de la F0), `PoliticaReintentos` (5 intentos: 2/5/10/20 min), `ResultadoProbarEmpresa` |
| Cola (dueña del esquema) | `src/PsaWeb.SageBridge.Cola` (net9, EF) | Tablas `TrabajosSage` (índice único Ruc+Tipo+ClaveIdempotencia), `LatidosBridge`, `EmpresasBridge`; migración `Inicial` aplicada al arrancar el Host. `IColaSage`: encolar idempotente, listar, resumen, cancelar (solo en cola), reintentar (solo Error/Cancelado), config por empresa |
| Anfitrión | `bridge/PsaWeb.SageBridge` (net48 x86, `.exe`) | Servicio `PsaSageBridge` / `--consola` / supervisor; lanza **el mismo .exe** con `--trabajador <pid>` y lo relanza (código 3 = reciclar al instante; caída = 5 s…2 min). Sin SDK ni SQL. Compilación determinista (verificado: mismo hash al recompilar) |
| Lógica | `bridge/PsaWeb.SageBridge.Logica` (net48 x86, DLL) | Ciclo: empresa con el trabajo listo más antiguo fuera de su ventana → lote con lease → `VerifyAccess` + `Open` una vez → manejadores → `Close` siempre. Empresas no habilitadas: solo `ProbarEmpresa` (que deja la solicitud de acceso pendiente). `SoloBases` como candado de desarrollo. Clave de Sage con DPAPI (`--proteger-clave`). JSON con el serializador del Framework (sin paquetes: el .exe no necesita binding redirects) |
| Administración | `/admin/sage-bridge` (Host, solo `Plataforma:Admins`) | Latido, empresas (Probar / Habilitar solo con acceso Granted / ventana / nota), trabajos recientes con Cancelar/Reintentar; refresco cada 5 s. Pestaña agregada en Usuarios y Auditoría |
| Tests | `tests/PsaWeb.SageBridge.Tests` (29), `tests/PsaWeb.Host.Tests` (2, primer test de páginas del Host, x86) | Solución completa en verde |

### 15.2 Validación en PREDATOR (copia de prueba, solo lectura en Sage)
- `ProbarEmpresa` con `.exe` nuevo → solicitud pendiente (`AccesoSage = Pending`); tras aprobar en Sage → **Granted**, abierta en
  1,1 s (Sage abierto por el usuario), 1.088 cuentas leídas, cerrada. **La aprobación del .exe cubre al trabajador hijo.**
- `SoloBases` rechazó una empresa fuera de la lista sin tocar Sage.
- Ventana de mantenimiento sobre la hora actual: el trabajo quedó en cola; al quitarla, se procesó.
- Error de base (Btrieve 12 sobre una carpeta inexistente): trabajo reprogramado (+2 min), trabajador reciclado al instante, el
  trabajo siguiente se procesó bien en el proceso nuevo. Un error de base permanente termina en Error al agotar los intentos.
- Matar el supervisor: el trabajador lo detecta y se detiene ordenadamente.
- Recompilar solo la DLL: el `.exe` conserva su hash y su autorización.

### 15.3 Pendiente para fases siguientes
- Correr el Bridge como servicio con la cuenta dedicada (F0-a ya lo validó con el spike; se hace con el anfitrión real en F8 o antes si
  se prefiere) y login SQL de esa cuenta en `PsaWebPlataforma`/`PeachEBills`.
- Encolado de trabajos que escriben (F3): la web solo debe encolar en empresas habilitadas.

---

## 16. F2 — Lógica pura de la compra (implementada 2026-09-25)

### 16.1 Qué se construyó (`src/PsaWeb.Compras`, sin escribir en Sage)
| Pieza | Port de | Notas |
|---|---|---|
| `Sri/LectorComprobanteSri`, `LectorFacturaSri` | `ImportXmlLib.XmlReader` + `FacturaE.LoadSaleInvoice` | Desenvuelve la respuesta SOAP del WS, el `<autorizacion>` del portal (CDATA) y el comprobante suelto; exige `id="comprobante"`; reconoce retención/NC/ND (para F7/F7b). Números en cultura invariante, `decimal` |
| `Catalogo/LectorCatalogoCompras` (ODBC) | `sageItems`, `sageVendors`, `SageContext.GetTwh*` | Una sola lectura de `LineItem` y los mismos filtros del `.exe`: ítems C (`CustomField1='COMPRA'`, **incluye inactivos**), IMPUESTO/R-IRF/R-IVA activos, detalle (clases 0/1/10, incluye inactivos), `AUT-SRI`, primera cuenta. Proveedor por identificación/ID |
| `Catalogo/LectorCatalogoPeachEbills` + 4 entidades nuevas en `PsaWeb.PeachEbills` | — | `AboutApplyTwh`, `dicImpuestosTipo`, `IdentityType`, `VendorConfiguration` (partial `PeachEbillsContext.Compras.cs`) |
| `Armado/PreparacionCompra` | `LoadInfoBill`, `LoadDetails`, `LoadingPaymentsAboutTwh`, `Twh332CodeUpdate`, `UpdateTaxLines` | Detalles del XML (total > 0, ítem aprendido de `VendorConfiguration`), «Resumir» (`Gruoped000…`), forma de pago sugerida, «no aplica retención» (332/332G/332I), líneas de IVA del XML y de una compra digitada |
| `Armado/CalculadorRetenciones` | `CheckTwhApply` + `LoadTwhItems` | Renta por ítem (322 al 10 %), IVA por (ítem, tarifa) con base redondeada lejos de cero, contrapartida de retención asumida (7) |
| `Armado/ArmadorOc` | `CorrectToLoad`, `LoadPurchaseOC`, `LoadPurchaseOrder` | Ítem C por línea, ítem C de ICE y propina, validaciones (mismos textos), reparto 10/90 de seguros, líneas en el orden del `.exe` (detalle, impuestos, propina, AUT-SRI, retenciones, asumidas, dos «.») y cabecera (§1.2). Devuelve errores, avisos y confirmaciones (factura a otra empresa) |
| `Catalogo/ReglasProveedor` | setters de `sageVendor` | Proveedor nuevo/actualizado desde el XML (tipo de identificación, nombre 30+30, dirección 30+30, `PhoneNumber2`) e **ID propuesto** (§5.1) |

### 16.2 Correcciones y fidelidades documentadas (§8)
| | Qué | Decisión |
|---|---|---|
| C1 | `otrosRubrosTerceros`: el `.exe` sumaba solo el primer `<rubro>` a la propina | Se suman todos |
| C2 | Ítem de IVA sin tarifa que calce: el `.exe` usaba el primer ítem IMPUESTO | Error |
| C3 | IVA de compra digitada con el `float` de `dicTaxRate` (0.15000001) | Tasa exacta |
| C4 | Nº de OC exigido de 7 caracteres | `XX-` + 4 o más dígitos (`OC-10000`) |
| C5 | `PhoneNumber2`: el `.exe` truncaba el nº de contribuyente especial a 3 caracteres al releerlo | Número completo |
| — | Suma de la compra ≠ total de la factura | Aviso (no bloquea) |
| B1 | Ítem C del ICE compara el código de impuesto en vez del de porcentaje (suma todas las líneas) | Fiel |
| B2 | Seguros 322 con cantidad ≠ 1: el 10 % queda multiplicado por la cantidad (el total cuadra) | Fiel |

### 16.3 Cierre: las OC reales se reconstruyen sin diferencias
`tests/PsaWeb.Compras.Tests/Validacion/ReconstruccionOcRealesTests` (x86, se salta sin `PSAWEB_TEST_SAGE_COMPRAS`): lee por ODBC las
OC de la copia de prueba desde el 11/09, su XML de `C:\PSA-F2\xml` (bajado del WS con `descargar-xml.ps1`, fuera del repo) y los
catálogos reales, deduce lo que eligió el digitador (forma de pago, cuenta y retenciones de cada línea, «Resumir», descripción editada)
y compara **cabecera y cada fila** (ítem, descripción, cantidad, precio a 15 decimales, monto, cuenta, job).

**Resultado (2026-09-25): 146 OC comparadas, 0 diferencias** (33 resumidas, 44 descripciones editadas por el digitador). Fuera de
la comparación: OC-8904 (la de prueba de la F0) y **OC-8902**, donde el digitador resumió y cambió a mano el monto del grupo con IVA
(61,51 → 57,60) sin tocar el IVA: esa OC no cuadra con su factura (74,36 vs 78,27) — justamente lo que el aviso nuevo detecta.

Tests: 49 nuevos (lector, preparación, armador, retenciones, validaciones, proveedor); solución completa 896 en verde.

### 16.4 Pendiente para F3
- Numeración de OC (máximo numérico) y de retención (`twhNumNext` + `CheckForTwhNumberAlreadyUsed`) — necesitan leer Sage al guardar.
- `LoadVendor` (crear/actualizar el proveedor por SDK) antes de la OC; `VendorConfiguration` nuevas/modificadas al guardar.
- Contrato del trabajo `CrearOc` (payload = `OcArmada` + proveedor) y su manejador en el Bridge.

---

## 17. F3 — Escritura de la OC por el Sage Bridge (implementada 2026-09-25)

### 17.1 Qué se construyó
| Pieza | Ubicación | Notas |
|---|---|---|
| Contrato `GuardarOc` | `src/PsaWeb.SageBridge.Contratos/GuardarOc.cs` | `PayloadGuardarOc` (OC armada + proveedor + numeración), `ResultadoGuardarOc`. Propiedades en **orden alfabético** (el Bridge lee con `DataContractJsonSerializer`, la web escribe con System.Text.Json; un test lo exige). Fechas como texto |
| Numeración | `NumeracionCompras` (Contratos, pura) | OC: máximo **numérico** del prefijo + 1 (`OC-10000` tras `OC-9999`). Retención: máximo de la serie + 1, con `startNumerationTwh` como mínimo |
| Manejador | `bridge/.../ManejadorGuardarOc.cs` | Port de `LoadVendor` (crea o actualiza los campos que actualiza el `.exe`, reactiva) + `LoadPurchaseOrder` (cabecera y líneas en el mismo orden y con el mismo orden de asignación). Numera al guardar leyendo Sage por ODBC (`OdbcSage`: misma fuente que la web, `PeachConnString` con la contraseña descifrada) |
| Web → Bridge | `src/PsaWeb.Compras/Bridge/SolicitudGuardarOc.cs` | `OcArmada` + proveedor → payload y clave de idempotencia `oc|proveedor|factura|huella`: mismo contenido = mismo trabajo (doble clic); contenido distinto = trabajo nuevo (actualización) |
| Errores | `RechazoTrabajoException` → Error sin reintentos; `OdbcException` → reintentar sin reciclar el proceso | |

### 17.2 Correcciones (§8)
| | Qué | Decisión |
|---|---|---|
| C6 | Actualizar: el `.exe` borraba la OC antes de armar la nueva y le daba un **nº de retención nuevo** | En el lugar: se carga la OC, se reemplazan las líneas (`RemoveLine`/`AddLine`) y se guarda una vez; conserva PostOrder, nº de OC y nº de retención. Las distribuciones quedan renumeradas (`DistNumber` sigue el contador de la OC); no afecta la conversión por SDK de la F6 |
| C7 | `Validate()` fallido sin mensaje | El trabajo queda en Error con los problemas de Sage |
| C8 | Nº de retención duplicado no se validaba | Se rechaza (y un nº de OC escrito a mano que ya existe) |
| — | OC ya recibida (convertida en compra) | No se toca: «ya se convirtió en compra (contabilizada)», como el botón deshabilitado del `.exe` |
| — | Proveedor nuevo con un ID que ya usa otro proveedor | Se rechaza (7c); si el ID existe con la misma identificación (reintento), se actualiza |

### 17.3 Hallazgo: el anfitrión cambiaba con cada commit
El SDK de .NET agrega `+<commit>` a la versión informativa: cada commit cambiaba el hash de `PsaWeb.SageBridge.exe` y Sage pedía
autorizarlo de nuevo. Corregido con `IncludeSourceRevisionInInformationalVersion=false` en el anfitrión. **No alcanzaba** (visto en la F4): el PDB embebido
llevaba SourceLink con el commit. Solución final: sin consultas a git (`EnableSourceControlManagerQueries=false`, `EnableSourceLink=false`)
y sin PDB (`DebugType=none`). Verificado compilando en **dos commits distintos** (worktree temporal en ee296cf y HEAD): mismo binario,
SHA-256 `99C8E111…`. Re-aprobado en la copia el 2026-09-25 (dos veces en total por este motivo).

### 17.4 Cierre: las OC escritas por el Bridge = las del `.exe`
`tests/PsaWeb.Compras.Tests/Validacion/EscrituraOcCopiaTests` (**escribe en la copia**; solo con `PSAWEB_TEST_F3_ESCRIBIR=1` y el Bridge en
consola con `SoloBases` = la copia; habilita la empresa mientras dura y la deshabilita al final). Toma una OC real por perfil (forma de pago,
retención de IVA, resumida, más de 3 líneas, propina, impuestos) — **22 OC** —, la arma desde el XML con factura de prueba
`999-998-<PostOrder>`, la encola y compara por ODBC **todas** las columnas de `JrnlHdr` y de cada `JrnlRow` con la del `.exe`.

**Resultado (2026-09-25): 22/22 sin diferencias inesperadas** (OC-8905 … OC-8926, retenciones 001-001-000025978 … 25999, 7 proveedores
actualizados por la corrección C5). Diferencias esperadas y excluidas con su motivo: datos de prueba (nº de OC, factura, retención),
identidad y grabado (`rGUIDa-d`, `JrnlKey_TrxNumber`, `LastPostedAt`, `LastUpdateCounter`), recepción (las del `.exe` ya las convirtió el
worker: `POSOIsClosed`, `QtyReceived`, `StockingQtyReceived`, `AmountReceived`) y el nombre del proveedor cortado a 30 en la fila de CxP por
el worker (§14.3). Además: **actualizar en el lugar** OK en 2 OC (mismo PostOrder, nº de OC y de retención) y **rechazos** OK (nº de OC ya
existente; nº de retención ya usado por otra OC). Volver a correr el arnés no escribe (trabajos idempotentes).

Datos de prueba que quedaron en la copia: OC-8905…8926 (facturas `999-998-…`), trabajos 29–54 en `TrabajosSage`.
Tests: 913 en verde en la solución (12 saltados).

### 17.5 Pendiente
- F4: módulo Compras (manual) — encola `GuardarOc`, muestra el resultado; permisos y auditoría.
- F6: convertir OC → compra en el Bridge (`PurchaseOrderSync`).

---

## 18. F4 — Módulo Compras, captura manual (implementada 2026-09-25)

### 18.1 Qué se construyó
| Pieza | Ubicación | Notas |
|---|---|---|
| Lecturas de Sage | `src/PsaWeb.Compras/Sage/LectorOcs.cs` | Lista (`FrmPrintedPurchases`: estado Guardado/Contabilizado por la compra con `INV_POSOOrderNumber`, autorización), OC guardada con sus filas, cuentas, jobs, último prefijo y vista previa de numeración |
| Recarga de una OC guardada | `src/PsaWeb.Compras/Sage/RecargaOc.cs` | **C9**: el `.exe` recargaba con IVA 12 % fijo, sin retenciones por línea y con la propina como detalle. Acá: tarifa desde la línea de IVA (la que reproduce el valor redondeado), retenciones deducidas (ítem C + suma de bases), forma de pago por las retenciones, propina e impuestos aparte, reparto 322 vuelto a unir. Avisa si lo recalculado no coincide con Sage |
| Auditoría | `PsaWebPlataforma.AuditoriaRegistrosSage` (contexto de la cola, migración `AuditoriaRegistrosSage`) + `IAuditoriaSage` | Una fila por encolado o rechazo por validación: usuario, documento, tercero, trabajo, huella SHA-256 del payload |
| Módulo | `modules/PsaWeb.Modules.Compras` | `/compras` (lista, filtro, Nueva, Abrir, Copiar — no electrónicas), `/compras/nueva?tipo=01\|02\|03`, `/compras/{postOrder}`, `/compras/{postOrder}/copiar`. Formulario: comprobante, proveedor (buscar por identificación, nuevo con «Proponer ID», datos del exterior), forma de pago (332* automático), detalle, impuestos, retención, totales/NETO, guardar → `GuardarOc` con seguimiento del trabajo. Solo lectura si la OC ya es compra o sin permiso. Aviso D5 desde OC-9900. Muestra la base de Sage que lee |
| Menú y permisos | Categoría **Compras** (E3), `qupurchinv`/`mkpurchinv`, `docs/sql/permisos-compras-ventas.sql` (vista previa probada, **no aplicado**) | GateProvisional: `ServicioCompras.PermisosProvisionales = true` → también habilitan `qupurchtwh`/`mkpurchtwh` |
| Desarrollo | `PeachEbills:DbqPorRuc` (`"RUC=dbq"`, user-secrets) | La web lee la misma copia en la que escribe el Bridge (`BasesPorRuc`) |

La web no encola si la empresa no está **habilitada** en el Bridge (lo avisa). El proveedor existente solo se reescribe en lo que el
digitador edita (sin volver a partir nombre/dirección/`PhoneNumber2`).

### 18.2 Validación
- **Recarga (solo lectura)**: `RecargaOcRealesTests` — las 146 OC reales de la copia se reabren sin XML y se vuelven a armar: **0
  diferencias** (OC-8902 se advierte: no cuadra en Sage).
- **Usuario en PREDATOR (2026-09-25)**, sobre la copia: factura física nueva **OC-8927** (ítem elegido, IVA 15 %, retención 1 % y 30 %
  IVA; CxP 21,90) verificada fila a fila por ODBC; **actualización** de OC-8921 dos veces (mismo PostOrder, nº de OC y de retención);
  rechazo por validación auditado. El resto de casos (tarjeta, asumida, nota de venta, liquidación, proveedor nuevo, copiar, contabilizada)
  **se probará en el despliegue** (decisión del usuario).
- Tests: 923 en verde en la solución.
- Hallazgo del Bridge: el `.exe` todavía cambiaba con el commit por SourceLink (§17.3 corregido); re-aprobado en la copia.

### 18.3 Pendiente
- F5: documentos recibidos (XML del SRI → mismo formulario, «Resumir», `VendorConfiguration`), «Registrar en Sage» desde Conciliación.
- Al desplegar: aplicar `permisos-compras-ventas.sql` y quitar el GateProvisional; casos de prueba pendientes de la F4.

---

## 19. F5 — Facturas recibidas desde el XML del SRI (implementada 2026-09-25)

### 19.1 Qué se construyó
| Pieza | Ubicación | Notas |
|---|---|---|
| Almacén de XML (§13.1) | `src/PsaWeb.Recibidos` — `RecibidosDbContext` (migración `Inicial`): `XmlComprobantesRecibidos`, `ConflictosXmlRecibidos`, `DescargasXmlFallidas` | gzip + SHA-256, único por RUC + clave, **inmutable** (un XML distinto para la misma clave queda como conflicto; el mismo comprobante con otro envoltorio SOAP/portal no es conflicto). Valida: parsea, clave interna = esperada, receptor = empresa (salvo confirmación explícita, como el «factura para otra empresa» del `.exe`), factura/NC/retención |
| Descarga por el WS | `DescargadorXmlSri` (`AutorizacionComprobantesOffline`, 3 intentos) + `ServicioDescargaXml` | Lo que el WS no entrega queda en `DescargasXmlFallidas` (motivo `SinXml`/`Error`): mide lo que cae fuera de la ventana (§13.5) y no se reintenta pasados 20 días de la emisión |
| Descarga automática | `ColaDescargaXml` + `TrabajadorDescargaXml` (BackgroundService); gancho en `POST /conciliacion-sri/api/comprobantes` | Al subir la extensión el reporte, se encolan las claves de facturas, NC y retenciones (código 01/04/07 de la clave) y se bajan detrás; la respuesta a la extensión no espera |
| Bandeja | `/compras/recibidos` (módulo Compras) | Facturas / NC del período: reporte del SRI (staging de Conciliación) ∪ XML guardados. Estados **Falta XML / Pendiente / Guardado / Registrado** (por la OC con esa clave en su AUT-SRI y si ya se recibió). «Descargar XML faltantes», **«Desde texto»** (port del `.exe`: pegar texto, se toman las claves de 49 dígitos). NC solo se listan (F7) |
| Formulario desde el XML | `/compras/recibidos/factura/{clave}` (mismo formulario de la F4) | `FormularioCompraEstado.DesdeFactura`: proveedor por RUC (existente actualizado como `LoadInfoBill`, o nuevo con ID propuesto), ítem aprendido de `VendorConfiguration`, forma de pago sugerida (332* automático), impuestos **fijos del XML**, propina, «Resumir detalles». Cabecera y montos del XML en solo lectura. Si ya hay OC con la clave: actualizar (o solo lectura si ya es compra). Avisos: ANULADO (WS de estado), ambiente de pruebas, otro receptor, total ≠ suma. Sin XML: **subida manual** (validada contra la clave). Al guardar: huella del XML en la auditoría y **aprende** código del proveedor → ítem de stock (`VendorConfiguration`, nuevas/modificadas) |
| Conciliación | «**Registrar en Sage**» en las filas *Solo en SRI* de tipo factura (§4.2), con `?volver=conciliacion` | Solo si el usuario puede registrar compras (`ReglasCompras`, compartido con el módulo) |

### 19.2 Validación
- `PsaWeb.Recibidos.Tests` (8): almacén (guardar/leer, mismo comprobante con otro envoltorio, conflicto, rechazos, otro receptor con
  confirmación), descargador, servicio de descarga (fuera de ventana no se reintenta). Carga de los **146 XML reales** de la F2 en el
  almacén local: 0 rechazos, 0 conflictos, ~5 KB comprimido por factura.
- `RecibidosCopiaTests` (copia de prueba, solo lectura): la bandeja 11–23/09 muestra las 146 facturas **Registrado**; el formulario desde
  el XML de OC-8757 abre en modo actualizar/solo lectura, con el proveedor y la retención de la OC, y arma la misma OC que el `.exe`.
- 932 tests en verde en la solución.
- **Pendiente para el despliegue** (decisión del usuario): lote real de un día de CPTDC registrado desde la bandeja, y medir cuántos
  documentos quedan fuera de la ventana del WS (`DescargasXmlFallidas`) para decidir la **F5b** (descarga por la extensión).

## 20. F6 — OC → compra por el Sage Bridge (implementada 2026-09-25)

### 20.1 Qué se construyó
| Pieza | Ubicación | Notas |
|---|---|---|
| Trabajo `ConvertirOcs` | `Contratos/ConvertirOcs.cs` (payload opcional `PostOrders`, resultado con convertidas/errores/pendientes/solo anotadas) + `Logica/ManejadorConvertirOcs.cs` | Reemplazo del worker COM (§6.2). Filtro idéntico (`OdbcSage.OcsPendientesDeCompra`: fila con ítem sin recibir, `ShipToAddress1` no vacío, no `ANULAD%`, últimos `ConversionMesesAtras` = 12 meses, sin fila en `PurchaseOrderSync`). Compra por SDK: `PurchaseInvoice` + `AddOrderLine` por cada línea con ítem pendiente; mismos campos que el worker (factura = `ShipToAddress1`, fecha OC, vencimiento `GoodThruDate`, términos, ShipVia, envío 1/2/estado, CxP de la OC, `DiscountDate` vacía, precio unitario a 5 decimales) |
| `PurchaseOrderSync` | `SincronizacionCompras` (PeachEBills) | Se anota **en el mismo trabajo** (idempotente); si la compra ya existía (reintento, o la hizo el worker) solo se anota. El worker lo hacía en el tick siguiente |
| Programación | `Trabajador.ProgramarConversiones` / `EncolarConversion` + `ColaSql.EncolarSiNoHayPendiente` | Cada `ConversionMinutos` (5; 0 = apagado) por empresa **habilitada** y fuera de su ventana de mantenimiento, y **al terminar cada `GuardarOc`** (`ConvertirAlGuardar`). Nunca dos `ConvertirOcs` en cola/proceso por empresa |
| Errores | por OC | Un error de negocio/SDK en una OC queda en `Errores` y no frena a las demás (se reintenta en la próxima corrida); ODBC/compañía ocupada/Btrieve se propagan al trabajo (reintento/reciclado normal) |

Mejoras frente al worker: abre **la compañía del RUC** (el worker escribía en la empresa que estuviera abierta en Sage), no requiere Sage
abierto ni sesión RDP, respeta la ventana de mantenimiento por empresa y deja resultado auditable en `TrabajosSage`. Lógica `0.3.0-F6`;
el anfitrión no cambió (hash `99C8E111…`, sin re-autorizar en Sage).

### 20.2 Validación (copia de prueba)
- Arnés `ConversionOcCopiaTests` (`PSAWEB_TEST_F6_CONVERTIR=1` + Bridge en consola): convierte las 22 OC de prueba de la F3 y compara
  **todas** las columnas de `JrnlHdr`/`JrnlRow` con la compra que el worker hizo de la OC original. **22/22 sin diferencias no
  explicadas**; `PurchaseOrderSync` anotado para las 22; una segunda corrida no encuentra pendientes.
- Diferencias explicadas: datos de prueba (factura, OC, retención, enlace a la OC y sus distribuciones renumeradas en las OC actualizadas
  en el lugar); compras del worker **editadas después por contabilidad** (cuenta y nombre; `LastUpdateCounter` > 1) o ya pagadas; la fila
  0 lleva el nombre completo del proveedor (el worker lo cortaba a 30: corrección, no pierde dato).
- Precio unitario: la importación COM lo dejaba a 5 decimales (el monto no cambia). Se replica (`Math.Round(…, 5)`) para que Retenciones/ATS
  lean lo mismo que hoy. Las 22 primeras conversiones de prueba son anteriores a ese ajuste.
- **Programación periódica**: con la empresa habilitada, el Bridge encoló solo un `ConvertirOcs` (#87) y convirtió OC-8927.
- **Ítem de inventario** (OC-8927, CS-001): la compra crea la capa de costo en `InventoryCosts` (tipo 10, diario 4, cantidad 1, $20) con
  el mismo patrón que las compras del worker → cantidad y costo del inventario correctos.
- **F0-c cerrados**: `DiscountDate` = vacía como el worker. `POSOIsClosed`: la cabecera de la OC queda cerrada (=1) igual que con el
  worker; por fila el worker deja 1 y el SDK 0 (la recepción por fila, `StockingQtyReceived`, es igual y es lo que usan el filtro y la web).
  `IncludeInInvLedger`: el SDK no lo expone y deja **0** en las filas de compra (el worker 1; todas las 25 467 filas históricas de ítems no
  inventariables lo tienen en 1). No lo lee ningún módulo web ni el legado; el inventario no se afecta (ver arriba). **Queda por mirar en
  Sage** si el reporte *Item Ledger* lista estas compras (C3, CS-001, sep-2026, facturas `999-998-…`/`999-999-000111222`).
- Sin probar en vivo: el encolado al terminar un `GuardarOc` (misma ruta de código que el periódico) → en el despliegue.

## Punto abierto (2026-10-01): marca de inventario en las compras de `ConvertirOcs`

Visto en la Liquidación de Importaciones (`PLAN-OLA2-LIQUIDACION-IMPORTACIONES.md` §15–§16): la compra del SDK aplicada a la OC
(`AddOrderLine`) queda con `IncludeInInvLedger = 0` en la fila del ítem y **no sale en el Item Costing Report** de Sage; las del worker
COM la tienen en 1 (en la original, últimos 12 meses: 13.437 filas de ítems no de stock y 15 de stock en 8 compras). El SDK no ofrece
otra forma de aplicar a la OC (6 variantes probadas), y la alternativa de líneas propias rompe el vínculo OC↔compra, que en Compras es
indispensable (decisión del usuario). **Resolver antes de apagar el worker COM en cada empresa** (p. ej. autorizar la importación COM
del worker desde el Bridge, o mantener el worker).

### Resuelto (2026-10-01): compra mixta en `ConvertirOcs`

- Alcance medido en producción (últimos 12 meses): compras con OC que llevan ítems de inventario — CPTDC 64 de 4.564 (1,4 %), **SANCEV
  506 de 1.583 (32 %)**.
- El vínculo OC↔compra (`INV_POSOOrderNumber`) lo usan Retenciones (fecha de emisión = `GoodThruDate` de la OC), ATS (anulados) y el
  estado «Contabilizado» de la lista de Compras: no se puede perder.
- Solución (aprobada por el usuario): **compra mixta**. Si la OC trae ítems de inventario (stock, sub-ítem, serializado o ensamblado; el
  tipo se obtiene cargando el ítem), esas líneas van como líneas propias de la compra (`AddPurchasesLine`) y Sage las marca para los
  reportes de inventario; el resto (IVA, retenciones, servicios) sigue aplicado a la OC, de modo que la compra **conserva su vínculo**;
  al final se cierra la OC. Las OC sin ítems de inventario no cambian.
- Validación en la copia: clon de la OC-8021 y después `Validacion/CompraMixtaCopiaTests` (`PSAWEB_TEST_COMPRA_MIXTA=1`), que copia la
  OC-8021 como la web (OC-8929), la guarda con `GuardarOc`, la convierte y la compara con la compra del worker COM: vinculada, OC
  cerrada, mismas filas contables, filas de inventario con `IncludeInInvLedger = 1`, mismas capas de `InventoryCosts`. El usuario vio
  la compra en el **Item Costing Report** (CP-001/MD-001). Retenciones lee la compra igual que la del worker (mismo sustento, fecha de
  emisión desde la OC y líneas de retención).
- Sigue igual que en la F6: las filas que no son de inventario (IVA, retenciones) quedan con `IncludeInInvLedger = 0` (el worker COM las
  deja en 1); no afecta reportes de inventario.
- Datos de prueba en la copia: OC 104936/104938 (`PR-102441…`) con compras 104937/104939, y OC-8928/OC-8929 con sus compras.
- Nota: `LectorOcsRealesTests` falla porque espera alguna OC «Guardado» entre el 11 y el 23-09 (las de prueba de la F3, ya convertidas
  por la F6): dato de prueba que cambió, no relacionado.

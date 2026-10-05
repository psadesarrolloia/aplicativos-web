# Plan — Portal de Ventas (inventario en tiempo real + órdenes de venta) y POS

Estado: **F0 en curso** (2026-10-02). Sin commit. **Cambio de rumbo 2026-10-02: no se usan órdenes de venta; el portal crea directamente la factura de venta (Sales Invoice).** Las menciones a SO de §2.3–§4 quedan como antecedente; vale §2.5. Continúa [[Ola 2: compras]] y [[Liquidación de importaciones]] (mismo Sage Bridge).

## 1. Objetivo y alcance

Dos soluciones; si se pueden unificar, mejor; si no, la urgente es la 1.

1. **Portal** (módulo de la web, categoría **Ventas**): los vendedores de **SANCEV** consultan el inventario en tiempo real y generan
   órdenes de venta que terminan en Sage.
2. **POS completo** (retail y otros giros), moderno y conectado a Sage. Nicho que hoy se rechaza por no tener POS.

Del POS antiguo en C#/WinForms **no hay código** en `sage50Apps-master` ni en el resto del disco (búsqueda 2026-09-30): nada que
aprovechar.

## 2. Hechos verificados (2026-10-01, solo lectura salvo indicación)

### 2.1 Cómo vende hoy SANCEV (base `sancevcialtda2202520`)

- **No usa órdenes de venta ni cotizaciones de Sage**: 0 filas con `JrnlKey_Journal = 11` ni `JournalEx 19/20`. CPTDC tampoco (1 fila de 2010).
  Hoy el vendedor pide y contabilidad factura. La Sales Order sería un **proceso nuevo** para la empresa.
- Facturas de venta = `JrnlKey_Journal 3 / JournalEx 8`: **≈ 280 al mes** (260–296 de marzo a agosto de 2026), ≈ 11 líneas por factura
  (2908 facturas y 33 339 líneas en el último año). 15 vendedores distintos en el último año; cada uno tiene además un «… TABLEROS»
  (reps duplicados por línea de producto: afecta a quién se atribuye una orden).
- Clientes: 2071 activos, **1629 con vendedor** (`Customers.EmpRecordNumber`), con `PriceLevel`, `Terms_CreditLimit`, `Balance`.
- Ítems: 2147 de stock activos (`ItemClass 1`), **2252 ensamblados** (`ItemClass 3`), 6 servicios. Precios: nivel 2 en los 2147, nivel 3 en 391,
  `SalesAmt1` en 261 (**los niveles de precio se usan de verdad**: el portal tiene que respetar el nivel del cliente).
- `LineItem` **no trae existencias**: se calculan desde `InventoryCosts` (como el Kardex).

### 2.2 Existencias en tiempo real

- `InventoryCosts.MajorType` 1 = compra, 2 = venta, 3 = saldo. **Existencia = Σ `Quantity` de `MajorType` 1 y 2** (las ventas van negativas) y es igual
  al último saldo `MajorType 3`.
- **Validado contra el SDK** (`InventoryItem.QuantityOnHand(fecha)` / `QuantityAvailable(fecha)`) en 40 ítems de la copia de CPTDC: **40/40 coinciden**
  (incluye ítems con 0 y con cientos de miles de unidades). Falta repetirlo en una base con **órdenes de venta abiertas** para ver si
  `QuantityAvailable` descuenta lo comprometido (§4, F0-b).
- Lectura **solo por ODBC, sin Bridge y sin cola**: sirve para la consulta en vivo. Sage 50 no tiene bodegas (`LineItem.Location` es texto libre).

### 2.3 SDK: lo que existe para ventas (por reflexión, SDK 2023.0.0.222)

- `SalesOrder` (`Save`, `Delete`, `AddLine`, `RemoveLine`, `CustomerReference`, `SalesRepresentativeReference`, `ShipToAddress`, `TermsDescription`,
  `CustomerPurchaseOrderNumber`, notas, `ShipByDate`, `IsClosed`…) y `SalesOrderLine` (`InventoryItemReference`, `Quantity`, `UnitPrice`, `Amount`).
  Mismo patrón que la OC de compra, que ya escribimos. `SalesOrderFactory` existe.
- `Quote` / `QuoteLine` (cotización, sin cierre contable) también existen.
- `SalesInvoice.AddSalesOrderLine(SalesOrderLine)` → la factura puede salir **aplicada a la orden** (conversión posterior por el Bridge o a mano).
- `Customer` expone `PriceLevel`, `Balance`, `CreditStatus`, `Terms`, `SalesRepresentativeReference`, `ShipToContact`.
- La numeración de la orden (`ReferenceNumber`) hay que calcularla nosotros por ODBC, como con la OC (el SDK no numera solo).

### 2.4 Bridge: latencia

- El Bridge abre la compañía por lote: el **primer trabajo tarda ≈ 23 s** (apertura); los siguientes del mismo lote, segundos. Para el vendedor la orden es
  **asíncrona** («enviando… confirmada con número»). Esto descarta el Bridge para una caja de POS (§5).

### 2.5 Factura directa (decisión del usuario 2026-10-02) — hechos de SANCEV, solo lectura

- **El número de la factura es el del SRI**: `Reference = 001-003-000013538` (establecimiento 001, punto 003, 9 dígitos; 3003 en el último año, de `…001791` a
  `…013538`). Hay también documentos internos `XC-D-INT-####` / `XC-T-INT-####`. El número se asigna al guardar, calculado por ODBC como en las OC, pero con **riesgo
  de choque si contabilidad factura a mano en Sage a la vez** (la secuencia del SRI no admite huecos ni repetidos): numerar y verificar dentro del mismo trabajo y reintentar
  si el número ya existe.
- **Vendedor y etiqueta**: `JrnlHdr.EmpRecordNumber` = el rep (los «… TABLEROS» son reps aparte, p. ej. el 22 = JACHO WILSON TAB) y `JrnlHdr.PurchOrder` (la «orden de compra del
  cliente») lleva una **etiqueta** `APELLIDO NOMBRE (EQU|TAB)` que se imprime en la factura: el portal debe llenar ambos como contabilidad.
- **Estructura contable**: fila de cabecera (cuentas por cobrar), fila de impuesto `RowType 5` «SRI», y por cada ítem de stock 3 filas (`RowType 0` venta, `1` costo de venta,
  `2` inventario con `IncludeInInvLedger = 1`). Una factura **mueve inventario y cartera al instante**; anular = `Void` (descripción `ANULAD…`).
- **Aguas abajo**: el módulo de Facturas electrónicas ya lista las facturas `JournalEx 8` no anuladas y las emite por Datil; la factura del portal entra a ese flujo sin tocar
  nada. Mientras `Datil:DryRun=true` no hay emisión real.
- **SDK**: `SalesInvoiceFactory.Create()`, `AddSalesLine()` → `SalesInvoiceSalesLine` (ítem, cantidad, precio), `SalesRepresentativeReference`, `CustomerPurchaseOrderNumber`,
  `TermsDescription`, `ShipToAddress`, `Void(fecha)`, `Delete()`.
- **Consecuencias de diseño**: (1) la existencia se descuenta al facturar: desaparece la necesidad del «comprometido» y la consulta de stock es la fórmula ya validada;
  (2) no hay paso intermedio de revisión: hace falta **validación fuerte antes de guardar** (cliente activo y con crédito, existencia suficiente, nivel de precio, IVA, vendedor/etiqueta,
  número libre) y un permiso específico; (3) anular una factura ya emitida al SRI es otro trámite: el portal solo anula si aún no se emitió.

### 2.6 F0 con factura (reemplaza F0-b/F0-c)

Arnés `SondeoVentasF0Tests`, etapa `so`: crea una factura de prueba `999-999-…` con `AddSalesLine`, compara sus filas con las de una factura real, mide si cambian `InventoryCosts` y la
existencia, la anula y verifica que se revierte. Escribió en la base de SANCEV de PREDATOR (Bridge con `SoloBases` = SANCEV; resultados en §2.7 y §2.8).
**El spike se eliminó el 2026-10-05** (`ManejadorSondeoVentas`, `TiposTrabajo.SondeoVentas` y `SondeoVentasF0Tests`; recuperables del historial de la conversación, no de git, porque nunca se commitearon). Para el F3 se reescribe como `GuardarFactura`
con lo aprendido: precio por `PriceLevels` (lista = `Customers.PriceLevel + 1`), `SalesTaxCodeReference` = `4-15%`, `AddSalesLine` + `CalculateAmount`, sin `Void()`.

### 2.7 Resultado de la primera corrida de factura (2026-10-02, SANCEV en PREDATOR; informe `psa-f0-ventas-so.txt`)

Decisiones del usuario: **el vendedor lo elige quien factura; solo contabilidad anula.**

- ✅ El SDK crea la factura por el Bridge (≈ 9 s con la compañía ya abierta, ≈ 12 s la primera) con las 3 filas por ítem (`RowType 0/1/2`, la de inventario con `IncludeInInvLedger = 1`), vendedor,
  etiqueta en `PurchOrder`, términos «Net 30 Days» y vencimiento tomados del cliente.
- ✅ **La existencia se descuenta al guardar** (30→28 y 30→27) y se revierte al anular. `InventoryCosts` recibe las capas (4 filas; 6 tras anular).
- ❌ **El SDK no pone precio ni IVA solo**: la factura salió con total 0 (`unit=0`), sin fila de impuesto `RowType 5` y sin `SalesTaxCode` en la cabecera. Las reales llevan
  `SalesTaxCode = '4-15%'` en el 97 % de las cabeceras y precio en cada línea. El manejador ahora toma el precio de `InventoryItem.PriceLevels` según `Customer.PriceLevel` y fija el código de
  IVA (**pendiente de reprobar**).
- ⚠️ **`Void()` no sirve para SANCEV**: crea un segundo documento `…V` con cantidades negativas y deja la descripción del cliente; el lector de Facturas electrónicas filtra `Description LIKE 'ANULAD%'`
  y listaría la `…V` como factura por emitir. **Contabilidad anula editando la misma factura** (212 desde 2025: descripción `ANULADA`, monto 0, mismo número). Como solo contabilidad anula,
  queda fuera del portal; si algún día se automatiza, se replica ese método.
- ⚠️ **Los vendedores cambian el precio**: de las líneas facturadas desde marzo, el precio coincide con el nivel del cliente en ≈ 59 % (nivel 0 → lista 1) y ≈ 25 % (nivel 1 → lista 2). Hay
  que decidir si el portal deja precio libre, con tope de descuento o con aprobación.
- ⚠️ Se pueden facturar ítems sin existencia suficiente (Sage lo permite): el portal debe validarlo él.
- Datos de prueba que quedan en la base de SANCEV de PREDATOR: facturas `999-999-010021618` (PostOrder 46539) y `…V` (46540), de total 0.

### 2.8 Segunda corrida (precio e IVA) — F0 cerrada 2026-10-02

- ✅ Factura `999-999-010021624` (PostOrder 46541): total **176,44** = (13,90 + 139,53) × 1,15; fila de impuesto `RowType 5` «SRI» (−23,01, cuenta 68) con `4-15%`; 3 filas por ítem
  (venta −monto, costo e inventario con monto 0 y `IncludeInInvLedger = 1`, igual que las reales: el costo vive en `InventoryCosts`). Misma estructura que una factura de SANCEV; solo falta la
  fila final vacía «.» que deja la pantalla de Sage (no hace falta).
- ✅ Existencia 30→28 / 30→27 al guardar y vuelve a 30 al anular. ≈ 9 s por factura.
- **Niveles de precio**: el SDK lista `PriceLevels` 1…10 (`Level`, `UnitPrice`, `Enabled`; en estos ítems solo 1–4 con valor). `Level n` = `LineItem.PriceLevel{n}Amount`. El **nivel del cliente es 0-based**
  (`Customers.PriceLevel = 0` → lista 1; `1` → lista 2): así cuadra el 59 % / 25 % de coincidencias con facturas reales. **El spike usó `Level == PriceLevel` y por eso tomó la lista 1 para un
  cliente de nivel 1: el manejador real debe usar `Level == PriceLevel + 1`.**
- ⚠️ `Void()` confirmado como inservible para SANCEV: además de la `…V` negativa genera un documento extra `JrnlKey 1 / JournalEx 3` (046543, un cobro de 0). Queda fuera del portal (solo contabilidad anula).
- Basura de prueba en la base de SANCEV de PREDATOR: facturas `999-999-010021618`/`…624` y sus `…V`, y los documentos 46543 y siguientes de total 0. No afectan numeración real (serie 999-999).

**Decisiones del usuario (2026-10-02):** el vendedor lo elige quien factura; solo contabilidad anula; **el precio por defecto es el de los niveles de Sage del cliente y el vendedor puede teclear otro, sin aprobación en ese momento.**
(Para auditoría el portal guarda precio de lista, precio facturado y quién lo cambió.)

## 3. Decisiones tomadas por la evidencia (a confirmar con el usuario)

- **Existencias por ODBC (lectura directa)**, nunca por el Bridge.
- **Órdenes de venta por el Bridge** (`GuardarOrdenVenta`), numeración por ODBC, actualización en el lugar, rechazo si ya se facturó.
- Código compartido en `src/PsaWeb.Ventas` (catálogo, precios por nivel, cliente, disponibilidad) para que el POS lo reutilice.

## 4. F0 — spikes (copia de prueba de CPTDC; SANCEV solo lectura)

| # | Spike | Estado |
|---|---|---|
| F0-a | Existencias ODBC = SDK | ✅ 40/40 |
| F0-b | Crear/actualizar/borrar una `SalesOrder` por SDK; comparar con una hecha a mano en Sage; ¿`QuantityAvailable` descuenta la orden abierta? ¿se mueve `InventoryCosts`? | ⏳ **pendiente de autorización de escritura** (arnés listo) |
| F0-c | `SalesInvoice.AddSalesOrderLine` desde la orden | ⏳ (misma corrida) |
| F0-d | Precio por nivel del cliente: ¿lo calcula el SDK o hay que fijar `UnitPrice`?; impuesto (IVA) por ítem/cliente | ⏳ |
| F0-e | Cliente: crédito (`Terms_CreditLimit` vs `Balance`), vendedor por defecto, dirección de envío | ⏳ (etapa `cliente`, solo lectura vía SDK) |
| F0-f | Orden de venta creada por el vendedor: qué ve contabilidad y cómo factura sin duplicar | pregunta al usuario |

Arnés: `bridge/PsaWeb.SageBridge.Logica/ManejadorSondeoVentas.cs` (**temporal**, tipo `SondeoVentas`) y
`tests/PsaWeb.Compras.Tests/Validacion/SondeoVentasF0Tests.cs` (`PSAWEB_TEST_F0_VENTAS=stock|cliente|so`, Bridge en consola, `SoloBases` = copia de CPTDC).
Se eliminan al cerrar la F0, como el spike de la liquidación.

## 5. POS: por qué no es lo mismo que el portal

- Sage no sirve de base transaccional de una caja: escritura en serie, ≈ 23 s de apertura por lote, sin backup con la compañía abierta por el SDK.
- El POS necesita **base propia** (ventas, turnos de caja, pagos, existencias comprometidas), **sincronización por lotes** hacia Sage, factura
  electrónica inmediata (Datil; hoy `DryRun`), impresión de tickets y tolerancia a cortes de red (probablemente PWA con cola local).
- Reutiliza del portal: catálogo, precios por nivel, cliente, existencias y la escritura por el Bridge.
- Orden: portal primero; el POS se planifica cuando el núcleo de ventas esté probado.

## 6. Preguntas abiertas para el usuario

1. Alcance v1 del portal: ¿solo consulta + orden, o también crédito del cliente, aprobaciones y precios por lista?
2. ¿Quién factura: contabilidad convierte la orden en factura (como las compras) o el portal también factura?
3. Vendedores «… TABLEROS»: ¿la orden debe llevar el vendedor del cliente o el que la captura? ¿cómo se elige entre el rep normal y el de tableros?
4. ¿Se acepta que la orden confirme en ≈ 5–30 s (asíncrona)?
5. POS: ¿cuántos locales, ventas por hora y necesidad de vender sin internet?

## 7. Fases propuestas del Portal (2026-10-02, a aprobar)

Módulo `modules/PsaWeb.Modules.Ventas` (categoría **Ventas**, ya prevista en el plan de Compras) + lógica pura en `src/PsaWeb.Ventas` + manejador `GuardarFactura` en la DLL del Bridge (el anfitrión no cambia).

| Fase | Contenido | Prueba |
|---|---|---|
| **F1 – Lectura** ✅ implementada 2026-10-02 (ver §8) | Consulta de inventario en tiempo real (ODBC): buscador por ítem/descripción/categoría, existencia (fórmula validada), precios por nivel 1–4, costo oculto salvo permiso; ficha de cliente (nivel, crédito, saldo, términos, vendedor). Solo lectura, sin Bridge. | Contra SANCEV: existencia = Kardex y SDK |
| **F2 – Prefactura (cotización) + PDF + correo** ✅ implementada 2026-10-02 (ver §9) | Lógica pura y almacén: la prefactura se guarda en `PsaWebPlataforma` (`Prefacturas`/`PrefacturaLineas`, número interno `PF-####` por empresa, **no** el del SRI) con **todo lo que Sage pedirá después**: cliente, vendedor y etiqueta `(EQU/TAB)`, términos, `4-15%`, por línea ítem, cantidad, precio de lista del nivel del cliente (`Level = PriceLevel + 1`), precio facturado (manual sin aprobación, con auditoría), IVA y totales con el redondeo de Sage, dirección de envío, notas, vigencia y una **foto de existencias** al emitir. Validaciones (cliente activo, crédito, existencia, líneas > 0). **PDF descargable** en el dispositivo (QuestPDF, como el Talón del ATS) y **correo automático a 1 o 2 destinatarios por empresa** (PDF adjunto + todos los datos en el cuerpo) por `IServicioCorreo`; el envío queda registrado (enviado/falló/reintento). Estados: Emitida → Enviada → Facturada/Vencida. | xUnit (totales = facturas reales reconstruidas, PDF con PdfPig, correo con servidor SMTP falso) |
| **F3 – Bridge: convertir prefactura en factura** (⏸ pospuesta: en esta primera etapa Contabilidad digita la factura a mano en Sage, sin escritura; ver §9) | `GuardarFactura` en la DLL del Bridge: toma la prefactura (sin volver a teclear nada), **numera `001-003-#########` al guardar con reintento si el número ya existe** (contabilidad también factura), revalida existencia y precios, crea la factura y deja en la prefactura el número y el `PostOrder`; idempotencia por clave; sin anulación. | Arnés en la copia: factura = factura real fila a fila |
| **F4 – Pantalla** | Nueva prefactura (cliente → ítems → precios → emitir), descarga del PDF y reenvío del correo, historial «mis prefacturas» por vendedor, botón **Facturar** para quien tenga el permiso (con «enviando…» ≈ 10 s y número devuelto), permisos `mkSalesQuote`/`quSalesQuote`/`mkSalesInv`. | Prueba del usuario en SANCEV |
| **F5 – Corte** | Deploy único con el resto de aplicativos de escritura; ensayo con una factura real de bajo valor; guía para vendedores. | — |

Pendientes a resolver antes de F3: (a) numeración exacta (¿una sola serie 001-003 para todos los vendedores? ¿y los `XC-D-INT`/`XC-T-INT`?), (b) quién ve costo y utilidad, (c) etiqueta `(EQU|TAB)` y el vendedor TABLEROS: lo elige quien factura (confirmado) — la pantalla ofrece los reps del cliente y los «… TABLEROS»,
(d) ítems ensamblados (`ItemClass 3`, 2252): ¿se facturan desde el portal? (e) ~~eliminar el spike del F0~~ ✅ hecho el 2026-10-05.

(f) **Ajuste 2026-10-02:** el vendedor emite una *prefactura/cotización* con PDF y correo; la factura de Sage se genera en un paso posterior (F3) con los mismos datos. Falta definir quién convierte (contabilidad o el mismo vendedor), la vigencia de la prefactura, los 1–2 correos por empresa y si el correo sale a cada emisión o solo a la primera.

## 8. F1 — Lectura (implementada 2026-10-02, sin commit)

- **Módulo** `modules/PsaWeb.Modules.Ventas` (categoría nueva **Ventas**, entrada «Inventario y precios», ruta `/ventas/inventario`, `GateProvisional`: visible para cualquier empresa hasta definir permisos).
  Registrado en el Host (`AddVentas`, `Routes.razor`, `Program.cs`) y en `PsaWeb.sln`.
- **Datos (solo `SELECT`, ODBC, empresa de sesión, sin Bridge):** `OdbcVentasRepository` — ítems de `LineItem` (`ItemClass 1`; los ensamblados `3` —en SANCEV los 2252 tableros «TE», uno por proyecto— vienen **incluidos por defecto**, decisión del usuario 2026-10-02, con casilla para ocultarlos),
  búsqueda por todas las palabras (código, descripción, parte, UPC), categoría y «solo con existencia»; existencia = Σ `InventoryCosts.Quantity` de `MajorType` 1 y 2, en una consulta agrupada por lote de 200;
  precios `PriceLevel1Amount…10`; clientes con nivel, días de crédito, límite, saldo y vendedor.
- **Reglas puras** (`PreciosDeVenta`, `ItemVenta`, `ClienteVenta`): lista del cliente = `PriceLevel + 1`; una lista sin precio no inventa precio; cupo disponible y exceso (límite 0 = sin límite configurado).
- **Pantalla:** buscador de cliente (opcional) → columna «Precio de <cliente> (lista n)» además de las listas con datos; existencia en rojo si es ≤ 0; solo se muestran las listas con precio en el resultado; tabla con scroll horizontal (celular).
- **Pruebas** `tests/PsaWeb.Ventas.Tests` (x86): 27 en verde — reglas puras, repositorio de muestra, **render real de la página** (encontró un bug: el texto de búsqueda no llegaba al filtro) y **4 contra SANCEV real**
  (`PSAWEB_TEST_SAGE_VENTAS`): existencia de 60 ítems = último saldo de `InventoryCosts`; cliente CARLOTA RODRIGUEZ nivel 1 / lista 2 / 30 días / con vendedor; RT18Z-32/2P EBAS = 6,95 (lista 1) y 7,31 (lista 2); categoría «TE» solo con ensamblados.
- **Pendiente de F1:** probar la pantalla en el navegador con el Host (usuario) y decidir el permiso definitivo. La pantalla no muestra el costo.
  *(2026-10-02: probada en el navegador por el usuario, «funciona perfectamente»; la casilla de ensamblados «TE» viene marcada por defecto; commit `eab148a`.)*

## 9. F2 — Prefactura (cotización) con PDF y correo a Contabilidad (implementada 2026-10-02, sin commit)

**Decisiones del usuario (2026-10-02):** Contabilidad convierte la prefactura **manualmente** en esta primera etapa (**sin escritura en Sage**); el correo automático llega a la persona de Contabilidad
y a una más (**2 correos por empresa**) con todos los datos para digitarla en Sage; **vigencia 15 días**. El F3 (Bridge `GuardarFactura`) queda pospuesto: todo lo que Sage necesitará ya queda guardado en la prefactura.

- **Flujo:** vendedor → `/ventas/prefacturas/nueva` (cliente → vendedor y etiqueta → ítems con precio de la lista del cliente o manual → emitir) → se guarda con número interno `PF-####` por empresa
  (**no** es el de la factura del SRI) → PDF descargable → correo a Contabilidad → Contabilidad digita en Sage y anota el N.º de factura en la prefactura (estado *Facturada*) o la anula.
  Estados: Vigente / Vencida (a los 15 días, calculado) / Facturada / Anulada.
- **Código:** `src/PsaWeb.Ventas` (lógica pura: modelo, `CalculadoraPrefactura` con el redondeo de Sage —reproduce la factura real 176,44—, `ValidadorPrefactura`, `EtiquetasVendedor`, `PrefacturaPdf` con QuestPDF,
  `PrefacturaCorreo`); módulo `PsaWeb.Modules.Ventas` (`VentasDbContext` + migración `Prefacturas`, `AlmacenPrefacturasEf`/`Memoria`, `ServicioPrefacturas`, páginas `PrefacturaNueva`, `ListaPrefacturas`, `PrefacturaDetalle`);
  Host: endpoint `GET /ventas/prefacturas/{id}/pdf?ruc=` (descarga, exige acceso a la empresa), migración al arrancar, **Configuración > Ventas** (`/admin/ventas`, solo administradores: correos 1 y 2, vigencia, código y % de IVA por empresa).
  `PsaWeb.Notificaciones`: `MensajeCorreo` acepta adjuntos (`AdjuntoCorreo`), compatible con el uso anterior.
- **El PDF es para el cliente** (cotización con logo de texto de la empresa, vigencia, líneas y totales; sin notas internas, existencias, lista de precios ni etiqueta). **El correo es para Contabilidad**: campos con el nombre de la pantalla de Sage
  (Customer ID, Sales Rep, Customer PO = etiqueta, Terms «Net n Days», Sales Tax `4-15%`, Ship To), líneas con la lista del cliente y ✎ si el vendedor cambió el precio, existencia al emitir, totales a ver en Sage, notas internas y el PDF adjunto.
- **El correo nunca bloquea la emisión:** sin destinatarios, sin SMTP o con error del SMTP la prefactura se guarda con el estado del correo (*Sin configurar* / *Falló* + mensaje) y se puede **reenviar** desde el detalle.
- **Validaciones:** errores (cliente, vendedor, ≥ 1 línea, cantidad > 0, precio en rango, ≤ 200 líneas) impiden emitir; advertencias (precio 0, más de la existencia, ítem repetido, supera el cupo del cliente) no. Precio manual **sin aprobación**.
- **Etiqueta:** el vendedor elige el rep de Sage y la línea EQU/TAB; la etiqueta se sugiere como `APELLIDO NOMBRE (EQU)` (invierte solo si son dos palabras) y es editable.
- **Pruebas** (`tests/PsaWeb.Ventas.Tests`, 80 en verde): lógica y cálculo, PDF leído con PdfPig (contenido y ausencia de datos internos, paginación), correo (campos, escape de HTML), servicio con almacén en memoria
  (numeración por empresa, vigencia, correo enviado/sin configurar/falla/reenvío, facturada/anulada, aislamiento entre empresas), **render real de las 3 páginas**, **SMTP real contra un servidor mínimo en proceso** (2 destinatarios + PDF adjunto) y
  **SQL Server local** (`PSAWEB_TEST_PLATAFORMA`: migración, 8 emisiones simultáneas numeradas 1..8 —encontró que 5 reintentos no alcanzaban; ahora 25 con espera aleatoria—, actualización y limpieza).
- **Pendiente de F2:** configurar el SMTP del servidor (`Correo:Servidor/Puerto/Usuario/Clave/Ssl`; el remitente hoy es `anulaciones@paredes.com.ec`, `Correo:De`) y los 2 correos en Configuración > Ventas; probar el flujo en el navegador con SANCEV; permisos definitivos (`GateProvisional`).
- **Pospuesto (F3):** `GuardarFactura` por el Bridge con numeración del SRI cuando se decida pasar de digitación manual a escritura automática.

## 10. Permisos del portal (implementados 2026-10-05, sin commit)

Hasta F2 el menú era visible para todos y **las páginas no verificaban nada** (la URL directa funcionaba). Ahora hay 4 llaves nuevas en `allowAction` y se exigen **en el servidor** (el servicio y el endpoint del PDF), no solo en el menú.

| Llave | Nombre | Qué permite | Roles que el script propone |
|---|---|---|---|
| `quSalesStk` | Ver inventario y precios de venta | `/ventas/inventario` | los de `qusaleinv` |
| `quSalesQte` | Ver prefacturas | lista y detalle de **las propias** + PDF | los de `qusaleinv` |
| `mkSalesQte` | Emitir prefacturas | nueva prefactura (PDF + correo), reenviar el correo de **las suyas** | los de `mksaleinv` |
| `auSalesQte` | Contabilidad | ver **todas** las de la empresa, anotar la factura de Sage, anular | los de `mksaleinv` |

- **Reglas** (`ReglasVentas`, `PsaWeb.Seguridad`): emitir implica ver prefacturas y ver inventario; cerrar implica ver (pero **no** emitir); ver inventario solo no abre las prefacturas. Un usuario sin `auSalesQte` solo ve las que emitió él
  (la lista fuerza el filtro y las ajenas responden «no existe»; el PDF las responde 404). Contabilidad entra a la lista viendo todas.
- **GateProvisional** (`ReglasVentas.PermisosProvisionales = true`, igual que Compras): mientras el área no cargue las llaves, `qusaleinv` (ver) y `mksaleinv` (emitir y cerrar) habilitan el portal. Al aplicar el script: poner `false` y dejar en
  `AppCatalogo` solo las 4 llaves nuevas. **Consecuencia a tener en cuenta:** con las llaves provisionales los vendedores de SANCEV **no** entran si no tienen `qusaleinv`/`mksaleinv`; hay que crear un rol «Vendedor»
  (`quSalesStk` + `quSalesQte` + `mkSalesQte`) y asignarlo por empresa desde el administrador de roles.
- **Script:** `docs/sql/permisos-ventas.sql` (vista previa con `-v Aplicar=0`, aplica con `-v Aplicar=1`; idempotente; códigos ≤ 10 y nombres ≤ 50). **Vista previa corrida en el PeachEBills local el 2026-10-05**: las 4 llaves se crean y se asignan a los roles
  `HacerFactElec`, `Hacer Comprobantes Electrónicos`, `Hacer Facturas electrónicas por Lotes` (y `Ver Comprobantes electrónicos` para las de lectura). **No aplicado** (decisión del área).
- **Código:** `PermisosVentas`/`ActorVentas`/`ServicioPermisosVentas` en el módulo; `ServicioPrefacturas` recibe el `ActorVentas` en cada operación y lanza `AccesoDenegadoVentasException`; las 4 páginas muestran «Sin acceso» y la
  subnavegación (`SubnavVentas`) solo ofrece lo que el usuario puede usar; `/admin/ventas` sigue siendo solo de `Plataforma:Admins`.
- **Pruebas** (98 en `PsaWeb.Ventas.Tests`): matriz de reglas con y sin provisionales, menú por llaves, permisos desde un directorio falso, el servicio exige cada permiso (emitir, ver solo lo propio, cerrar, reenviar) y las páginas con permisos limitados.
- **Pendiente:** que el área apruebe el reparto y aplique el script; crear el rol de vendedor; luego `PermisosProvisionales = false`.

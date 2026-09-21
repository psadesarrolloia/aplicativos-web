# Plan — Reportes Access (PWC · Comisiones · Cheques) → módulo web

Migra 3 reportes hechos en Microsoft Access (`Reportes Access/ReportesEmpresas.mdb` y
`ReportesEgresosDemoR.mdb`) a **un módulo web con 3 páginas**, solo lectura, sobre la
empresa de sesión. Mismo molde que Kardex (`docs/PLAN-KARDEX-INVENTARIOS.md`): repo ODBC
`SELECT` + repo de muestra + ClosedXML + página acotada a la empresa de sesión.
**No usa el SDK ni el Sage Bridge** (`docs/APPS-SDK-VS-SOLO-LECTURA.md`): no se escribe nada en Sage.

Estado: **F0 (plan) hecho; implementación en curso en `main`, sin push ni deploy** (decisión del usuario).
Las decisiones abiertas están en §8; cada una se implementa con el valor por defecto
recomendado y se cambia con un ajuste de configuración, no reescribiendo código.

## 1. Fuentes leídas

`C:\PROYECTOS IA\Interfaz Gráfica Aplicativos PSA\Código fuente Aplicativos\Reportes Access\_export-texto\`

| Reporte | Origen | Notas |
|---|---|---|
| PWC | `ReportesEmpresas/Modules_ReportPWC` + `Modules_Sage50connection` | Excel por automatización COM; sin filtros, sin `ORDER BY`. |
| Comisiones | `Modules_ReportpComissions` + `Forms_frmComissionsReport` + `Modules_Sage50connection` | Filtro por rango de **número de recibo**. |
| Cheques | `Modules_paymentsChecks`, `Forms_PaymentsSearch`, `Reports_PaymentsProof`, `Modules_Numero a letras`, `Modules_general` | **La versión con prefijo/tipo de pago está en `ReportesEgresosDemoR`, no en `ReportesEmpresas`** (ver §5.1). |

Fuera de alcance: las entradas de menú `MnuReqCobro` (`frmFiltros`) y `MnuEstadoCta`
(`frmEstadoCta`) del menú de Access — no vienen en el volcado a texto.

**Lo que NO se trae:** `Sql_Connection` de `Modules_general` (usuario y clave de SQL Server
escritos en el código), `apiServerUrl`, `RUC_EMPRESA` fijo y el módulo `SageOdbcString.mdb`.
La web resuelve la conexión por RUC de sesión con `PeachConnStringResolver`. La tabla local
`Empresas.CurrConnected` de Access (empresa "abierta") se reemplaza por la empresa de sesión.

## 2. Verificación previa contra datos reales

Antes de diseñar se reescribieron los dos reportes de cartera como scripts ODBC descartables
(las consultas del VBA **verbatim**, 32 bits) contra el Sage local de Radio FM Efemedio
(`EFEMEDIO20252026`, 77.626 asientos) y se compararon con los Excel del usuario:

| Excel | Resultado |
|---|---|
| `CXC PWC EFEMEDIO.xlsx` (85 facturas) | **80 de 82** facturas del Sage local coinciden en *todas* las columnas (subtotal, IVA, total, retenciones IR/IVA, descuentos, monto a cobrar, fechas, cliente, orden, ciudad, anunciante). 3 facturas del Excel (`…15614`, `…15615`, `…15618`) y 2 retenciones (`…15603`, `…15611`) son **posteriores** a la copia de Sage de PREDATOR: diferencia de fecha de corte, no de lógica. |
| `COMISIONES EFEMEDIO.xlsx` (rango de recibos 5122–5146) | **41 de 41** filas idénticas (V. FACT., IVA, TOTAL, RET., CRUCE, ABONO, SALDO, recibo, fechas, cliente). |

Consecuencia: el VBA se entiende bien y el port puede ser fiel. Los scripts de referencia
quedan **fuera del repo** (scratchpad de la sesión); la validación repetible está en los tests (§7).

## 3. PWC — «CXC PWC»

**Qué es.** Facturas de venta con saldo pendiente, con retenciones y el monto a cobrar. Una fila por factura.

**Consultas (Sage 50, todo `SELECT`):**

1. Cabecera — `JrnlHdr` × `Customers`: `JrnlKey_Journal = 3`, `JournalEx = 8`, `JrnlTypeEx = 0`,
   `MainAmount <> AmountPaid`, `NOT Description LIKE 'ANULAD%'`. Cliente =
   `Customer_Bill_Name + CustomField1` (concatenado **sin separador**: el nombre largo se parte en 2 campos
   de Sage; de ahí `PUBLICIDAD DE MEDIOS` + `PUBLIMEDIOS S.A.` → «…MEDIOSPUBLIMEDIOS S.A.»).
2. Subtotal e IVA por factura — `JrnlRow` (`RowNumber <> 0`) agrupado por `RowType, TaxAuthorityCode`:
   `RowType = 0` → SUBTOTAL, resto → IVA; `Abs(Subt)` solo si `<> 0`. **El último grupo no cero gana**
   (bug P3, §6).
3. Retenciones — notas de crédito (`JournalEx = 9`, `JrnlTypeEx = 2`, `PurchOrder = '4R'`) enlazadas
   por `JrnlRow.LinkToAnotherTrx = PostOrder` con `Jobs.JobID` = `IRF` (renta) o `IVA`. El **porcentaje se lee
   de `RowDescription`** (`InStr(...,"1%")`).

**Columnas del Excel** (A..O; P sin rótulo):
`A` razón social · `B` factura · `C` emisión · `D` cobranza (vence) · `E` agencia/cliente
· `F` orden (=`ShipToCity`) · `G` ciudad cobros (=`ShipToZIP`) · `H` anunciante (=`ShipToName`)
· `I` subtotal · `J` IVA · `K` total · `L` ret. IR · `M` ret. IVA · `N` descuentos =`K−L−M−O`
(fórmula) · `O` monto a cobrar = `MainAmount − AmountPaid` · `P` `ShipToAddress2`.
Estilo: Georgia 8, encabezado naranja `#FF6600`, bordes medios, moneda contable.

**Aclaraciones de nombres**: «CIUDAD COBROS» no es `ShipToCity` sino `ShipToZIP` (el VBA los tiene
cruzados con «ORDEN»). En el Excel de Efemedio la ciudad está vacía en 76 de 85 filas (dato de Sage).

**Resúmenes del encabezado.** En el `.mdb` los cuadros `K1:N4` suman columnas auxiliares `V:Y`
(RF 1 %, RF 2 %, IVA 20 %, IVA 70 %). El cuadro «GYE / UIO» (`G3:G4`) **solo trae los rótulos**: no hay
ninguna fórmula ni valor detrás (verificado contra el Excel del usuario).

### Personalización (todo por empresa)

| Qué | Cómo |
|---|---|
| Rango de fechas | Emisión desde/hasta y cobranza (vence) desde/hasta. Vacío = todo (como Access). |
| Cliente | Texto («contiene»), o elección de la lista de clientes con saldo. |
| Ciudad | Selector con las ciudades presentes (`ShipToZIP`), incluida «(sin ciudad)». |
| Factura / anunciante | Texto «contiene». |
| Encabezado | Rótulo de la columna A, título del reporte y **cobrador** («PWC» → «MONTO A COBRAR POR PWC»). |
| Nombre de empresa | Por defecto el de la empresa de sesión; editable. |
| Columnas a mostrar | Por bloque: Empresa · Orden · Ciudad · Anunciante · Retenciones y descuentos · Dirección. Las básicas (factura, fechas, cliente, subtotal, IVA, total, monto a cobrar) siempre. |
| Excel | ClosedXML, **fechas como fecha real**, importes como número, `N` = fórmula `K−L−M−O`, fila de totales, resúmenes. |

**Resumen de retenciones (dinámico).** En vez de 4 columnas fijas, el porcentaje se extrae con una
expresión regular de la descripción (`309 - RF 3%` → 3; `1.75% RET IMP RENTA` → 1,75; `IVA 70%` → 70) y
se arma una columna auxiliar por cada porcentaje **encontrado**. Las descripciones sin porcentaje
(`RETENCION IVA`, `RETENCION FUENTE`) van a «sin % en la descripción». Así el resumen siempre cuadra con
las columnas `L` y `M`. Con datos de Efemedio: RF 1 % = 17,60 y IVA 70 % (por descripción) = 5.659,05 —
mismas cifras que el `.mdb`, ver D9 por el rótulo cruzado.
**Resumen por ciudad** (nuevo, generaliza el cuadro GYE/UIO vacío): cantidad y monto a cobrar por ciudad.

## 4. Comisiones

**Qué es.** Facturas cobradas por recibos de cobro dentro de un rango de recibos, agrupadas por cliente.

**Consulta principal.** `JrnlHdr` (recibo) × `JrnlRow` × `JrnlHdr` (factura) × `Customers`:
`Receipt.JournalEx = 3`, `Receipt.JrnlKey_Journal = 1`, referencia del recibo **sin «-»**, factura no anulada,
`JrnlRow.LinkToAnotherTrx = factura.PostOrder`. Por cada fila, 3 consultas auxiliares: subtotal/IVA (misma
que PWC), retenciones (`SUM(Amount)` de NC `4R`) y cruces (`SUM(Amount)` de asientos con referencia `CJ%`).

**Cálculo del abono:** `ABONO = PAID − CRUCE − RET`, con `PAID = SaleInv.AmountPaid`.
**Orden:** `Receipt.TrxName, Fecha, Receipt.TransactionDate DESC` (+ desempate por `PostOrder`, ver §6).

**Excel** (A..K, Arial 10): fila de cabecera por cliente (nombre + n.º del primer recibo en la columna
«FECHA», artefacto del original que se conserva) y una fila por factura:
`CLIENTE / FACTURA · FECHA · V. FACT. · IVA · TOTAL · RET. · CRUCE · ABONO · SALDO · RECIBO · FECH REC`.

### Personalización

Rango de **recibos** (desde/hasta, como en Access) más filtros nuevos: fecha del recibo, cliente y ciudad
(`ShipToZIP` de la factura); columnas opcionales (ciudad, «importe aplicado por el recibo» para control);
nombre de empresa y título del archivo. Excel con fechas reales y una fila de total general al final.

## 5. Cheques (pagos con comprobante de egreso)

### 5.1 Datos

- Pagos: `JrnlKey_Journal = 2`, `JournalEx = 5`, `JrnlTypeEx = 0`, por **rango de fechas**, `ORDER BY fecha DESC`.
- Detalle por pago: `JrnlRow` × `Chart` con `LEFT OUTER JOIN` a la factura pagada (`LinkToAnotherTrx`).
- **Referencia = prefijo + número**, sólo en la versión de `ReportesEgresosDemoR`
  (`NumberReference`): `PI-1234` → tipo `PI-`, número `1234`; `TRF-015-A` → número `015`; sin guión → tipo
  `Default`. La versión de `ReportesEmpresas` **descartaba** todo lo que no fuera 100 % numérico. Se porta la
  de DemoR (es un superconjunto: `Default` = lo que mostraba `ReportesEmpresas`).
  En Efemedio (histórico): 8.387 numéricas, 2.756 `CJ-`, 1.396 `DNRS-`, 1.168 `AJ-`, 847 `TRANS-`, 474 `CC-`, 133 `CH-`…
- Filtros: fechas, **rango de número** (numérico, `CLng`) y **tipo de pago**. Se elige qué pagos imprimir
  (por defecto todos los filtrados, como Access) y qué imprimir: cheque, comprobante de egreso o ambos.
- Monto = `Abs(Round(MainAmount, 2))`. Detalle: **PAGOS** = `RowAmount` si `Amount > 0`; **CHEQUE** = `Abs(RowAmount)` si `Amount < 0`.

### 5.2 Página impresa — medidas

Cada pago = **una hoja A4 vertical**. Las medidas del reporte están en twips (1 twip = 0,017639 mm) y
coinciden con las que diste (verificado): 

| Elemento | X (mm) | Y (mm) | Ancho | Alto/otros |
|---|---|---|---|---|
| Rótulo «CH.No.:» | 170,1 | 0,0 | 13,8 | — |
| N.º de cheque | 170,1 | 5,0 | 18,9 | — |
| Beneficiario | 15,0 | 7,0 | 80,0 | — |
| Monto (der.) | 109,0 | 7,0 | 25,0 | derecha |
| Monto en letras | 15,1 | 15,0 | 100,0 | 10,2 (2 líneas) |
| «Ciudad, aaaa/mm/dd» | 15,1 | 26,0 | 84,0 | — |
| Recuadro del comprobante | 7,9 | 77,0 | 170,0 | 17,0 |
| Logo | 49,9 | 77,5 | 16,3 | 16,6 |
| Empresa (negrita) / «COMPROBANTE DE EGRESO» | 70,0 / 70,1 | 78,5 / 86,7 | — | — |
| Nombre / Fecha / Concepto | 9,0 / 117,9 / 9,0 | 98,4 / 98,5 / 106,4 | — | — |
| Línea + encabezado de tabla | 7,9 | 112,5 / 114,6 | 170,0 | — |
| Columnas: CÓDIGO · REFER./FACTURA · DESCRIPCIÓN · PAGOS · CHEQUE | 8,5 · 24,6 · 58,7 · 139,7 · 159,5 | filas desde 120,5, alto 6,5 | 15,0 · 33,1 · 79,9 · 18,8 · 18,8 | — |
| Firmas «RECIBIDO POR» · «REVISADO/ELABORADO POR» ×2 | 23,0 · 80,0 · 136,0 | y = 253,7 (pie de 47,0 mm anclado al margen inferior de 6,35; la línea queda 10,0 mm bajo su inicio) | 40,0 | — |

Fuente 10. Ancho de página del reporte 18,9 cm.

### 5.3 Lo que el `.mdb` SÍ dice del papel (decodificado de `PrtMip` / `PrtDevMode`)

Me dijiste que el tamaño de papel y los márgenes no se pudieron leer. Están en el volcado, en hexadecimal:

- **Papel A4 vertical** (`dmPaperSize = 9`, `dmOrientation = 1`; los `dmPaperLength/Width` de 279,4×215,9 mm
  del `.mdb` de Demoradio no aplican: su bandera de campos no los habilita).
- **Márgenes del reporte: izquierdo 10,0 mm · superior 13,0 mm · derecho 7,0 mm · inferior 6,35 mm.**
- Ancho útil 189 mm (10.716 twips) → 10,0 + 189 + 7,0 = 206 mm ≤ 210.
- La impresora guardada en ese momento fue «Microsoft Print to PDF» → no revela el modelo de la matricial.

⚠️ **Esto puede cambiar tu lectura de las coordenadas.** En Access el (0,0) de un reporte es la esquina
del **margen**, no de la hoja. Si esos márgenes se usaron al imprimir, el n.º de cheque no cae en
(170,1; 5,0) sino en (180,1; 18,0) desde la esquina de la hoja. No lo puedo saber sin una impresión real →
**D3**: la página trae corrección X/Y configurable (por defecto 0/0, tu lectura) y una hoja de prueba.

### 5.4 Cómo se imprime — evaluación

| Opción | Alineación | Velocidad/calidad | Cómo llega a la matricial | Veredicto |
|---|---|---|---|---|
| **A. PDF con coordenadas en mm** (QuestPDF, ya en el repo) | Exacta: cada campo se coloca por su (x, y); la fuente sólo cambia el ancho, no el origen | Sale como gráfico: más lenta en 9 agujas, calidad de borrador | Navegador → controlador de Windows → matricial. Sin instalar nada | **Elegida para probar.** Exige imprimir al **100 % / «tamaño real»**, sin «ajustar a página» ni márgenes |
| **B. Texto monoespaciado** (`.txt` + rejilla 10 cpi × 6 lpi) | Cuantizada: ±1,3 mm en X y ±2,1 mm en Y (4,23 mm por línea). Y = 5,0 → línea 1,2; 7,0 → 1,65; 15,0 → 3,5. No sirve con estas medidas | Muy rápida, nítida | Bloc de notas / «Generic / Text Only» | Descartada por la cuantización |
| **C. ESC/P crudo** (posición absoluta `ESC $` 1/60″, avance `ESC J`/`ESC 3` 1/216″ ≈ 0,12 mm) | Exacta (≈ 0,4 mm en X, 0,12 mm en Y) | Modo texto nativo: rápida y nítida | El navegador **no** envía bytes crudos; hace falta un ayudante local o una impresora compartida a la que el servidor escriba en RAW | **Fase 2** si A falla o es demasiado lenta. Depende del modelo y la conexión |

El modelo de página se guarda como **lista de campos con (x, y, ancho, alineación) en mm**, independiente del
formato: hoy lo consume el renderizador PDF; el de ESC/P sería un segundo consumidor de la misma lista.

Fuente: **monoespaciada** (Courier) por defecto — con 100 mm por línea caben 47 caracteres y el texto en letras
(90 caracteres con relleno `xxxxx`) llena exactamente las 2 líneas — o Arial, configurable.

**Hoja de prueba** (`/bancos/cheques/prueba`): marcas de esquina, regla en milímetros en los 4 bordes, una línea de
100,0 mm (para comprobar que el 100 % se respetó), marcas en (10,0; 13,0) por si rige el margen del reporte, y un cheque
de ejemplo con cada campo enmarcado y rotulado con su (x, y). Se pega el cheque, se imprime, se mide y se
carga la corrección X/Y en la página.

### 5.5 Monto en letras

Se porta la `valor_letras` **del reporte** (`Reports_PaymentsProof`), que agrega a la del módulo el `CON xx/100`
y el relleno: `texto + " CON " + Format(centavos,"##00") + "/100   "` y, si mide menos de 90 caracteres,
`x` hasta 90 con un espacio cada 5 (`TamNumLetters = 90`). Se conservan las rarezas: `UN MIL`, dobles espacios entre
bloques, `CERO DOLARES` sin relleno y — **bug Q1** — **montos menores a $2,00 pierden el «CON xx/100» y el relleno**
(`If … unidades > 1`). Ejemplo: 1,50 → «UN». Ver D7.

### 5.6 Configurable (nada fijo a Efemedio)

Nombre de la empresa (hoy fijo: «EFEMEDIO CIA. LTDA.» / «DEMORADIO CIA. LTDA.»), logo (PNG/JPG subido, hoy
`logoExa.png` embebido), **ciudad** (hoy «Quito» fija en `CityAndDate`), los 3 rótulos de firma («REVISADO POR» en
Efemedio, «ELABORADO POR» en Demoradio), rótulo «CH.No.», fuente, tamaño, formato del monto y corrección X/Y.

## 6. Bugs y rarezas heredadas (catálogo)

`Bug` = se conserva por defecto (port fiel) y se marca en pantalla; el usuario decide (§8). Los IDs son los de los comentarios del código.

| ID | Dónde | Qué pasa | Evidencia | Por defecto |
|---|---|---|---|---|
| **C1** | Comisiones | `ABONO = PAID − CRUCE − RET` usa el **total pagado de la factura**, no lo aplicado por *ese* recibo. Una factura con 2 recibos aparecería 2 veces con el mismo abono | Sin facturas repetidas en el Excel, pero `…13912` (recibo 5132) muestra abono **129,80** cuando ese recibo aplicó **1,32**; `…15422` (5143): 1.570,48 vs 317,65 | **Fiel** (opción «abono = lo aplicado por el recibo») |
| **C2** | Comisiones | El filtro compara **texto**: `'513' >= '5122'` es verdadero | El Excel del usuario incluye los recibos **513 y 514 (de 2013)** en el rango 5122–5146 | **Fiel** (opción «comparar como número») |
| **C3** | Comisiones | Sin desempate: facturas del mismo día salen en orden físico de Pervasive | Excel: `15463, 15461, 15462, 15465, 15464` | Se agrega `PostOrder` como último criterio (no cambia valores) |
| **P1** | PWC | Rótulos del resumen cruzados: `M3` (junto a «RET. IVA 70 %») suma la columna de **20 %** y `M4` (junto a «RET. IVA 20 %») la de **70 %** | Excel: `M3 = 0`, `M4 = 6.184,05` (≈ 5.659,05 del 70 % + 525 de una retención posterior al corte) | **Corregido por construcción** (resumen dinámico, D9) |
| **P2** | PWC | Los resúmenes sólo cubren 1 %/2 % (IR) y 20 %/70 % (IVA); el resto de porcentajes (3 %, 1,75 %, IVA 100 %) queda fuera | 76 de 85 filas tienen IR ≠ 1 %/2 % | Resumen dinámico |
| **P3** | PWC/Comisiones | Subtotal e IVA: **el último grupo no cero gana**, no se suman los grupos | En Efemedio los grupos extra son de monto 0 → sin efecto | **Fiel** (sin diferencia observada) |
| **P4** | PWC | La ciudad («CIUDAD COBROS») sale de `ShipToZIP` y «ORDEN» de `ShipToCity` | Excel | Fiel |
| **P5** | PWC | Sin `ORDER BY`: el orden es el físico | Excel: `15613` antes de `15612` | `ORDER BY PostOrder` (orden de captura) |
| **Q1** | Cheques | Montos < $2,00 sin «CON xx/100» ni relleno | Código | **Fiel** + advertencia en pantalla (D7) |
| **Q2** | Cheques | Doble espacio entre bloques; `UN MIL` | Código | Fiel |
| **Q3** | Cheques | `CityAndDate` fija «Quito» | Código | Configurable |
| **Q4** | Cheques | Un pago anulado (`ANULADO`, monto 0) sale igual en la lista | Efemedio ref. `6615` | Fiel; el monto sale «CERO DOLARES» |
| **X1** | Comisiones/Excel | En la fila del cliente, la columna «FECHA» trae el n.º de recibo | Excel | Fiel en el Excel |

## 7. Diseño técnico

**Módulo** `modules/PsaWeb.Modules.Reportes` (RCL). Rutas: `/cartera/pwc`, `/cartera/comisiones`, `/bancos/cheques`
(+ `/bancos/cheques/pdf`, `/bancos/cheques/prueba`, `/cartera/*/export`). Categorías nuevas del menú:
**Cartera** (PWC, Comisiones) y **Bancos** (Cheques), en `Categorias.Orden` tras «Caja».

**Permisos** (`allowAction`, `allowCode` ≤ 10, `allowName` ≤ 50, `GateProvisional` = visibles a todas las empresas hasta que el área
los asigne a roles): `quRptPwc`, `quRptComis`, `quRptChq`. Script `docs/sql/permisos-reportes-access.sql` (idempotente, **no
ejecutado**; se aplica en el deploy).

**Configuración por empresa** — tabla `ConfiguracionesReporte` en `PsaWebPlataforma` (RUC + reporte, JSON,
logo, quién y cuándo). Migración EF que se aplica sola al arrancar (como Conciliación SRI). Nunca en `PeachEBills`. Sin
`Plataforma:ConnectionString` (dev) la configuración vive en memoria.

**Capas** (por reporte): `I…Repository` + `Odbc…Repository` (parámetros `?`, consultas por lotes con `IN` en vez de N+1)
+ `Sample…Repository` + lógica pura (`Armador…`) + exportador/renderizador. `CultureInfo.InvariantCulture` en toda
conversión número↔texto de datos de Sage. Sage siempre a 32 bits; en PREDATOR `PeachEbills:SageServerNameOverride=localhost`.

**Tests** (`tests/PsaWeb.Reportes.Tests`): `NumeroALetras` (tabla de casos, Q1/Q2), parser de referencia y de porcentajes,
armadores de PWC y Comisiones (bugs C1/C2 con y sin corrección), exportadores (reabrir el `.xlsx`: fechas reales,
fórmula `N`, resúmenes), renderizador PDF (posiciones en mm extraídas del PDF), y **validación contra los dos Excel del usuario**
(pruebas opcionales: se saltan si no está el Sage local de Efemedio o los archivos; los archivos con datos de un cliente
**no se commitean**).

**Fixture:** Radio FM Efemedio (`EFEMEDIO20252026`, RUC 1792187796001) está en el Sage local. **Demoradio no** (Btrieve 2301) — sus
variantes (rótulo «ELABORADO POR», `DEMORADIO`) se cubren con la configuración, sin datos propios.

## 8. Decisiones

### 8.1 Necesito tu respuesta (la implementación ya usa el valor recomendado)

| # | Pregunta | Recomendado / implementado |
|---|---|---|
| **D3** | ¿El (0,0) de las medidas es la **esquina de la hoja** o el **margen del reporte** (10,0 / 13,0 mm, ver §5.3)? | Esquina de la hoja (tu lectura), con corrección X/Y y hoja de prueba; se resuelve con la primera impresión |
| **D4** | ¿Qué **matricial** (modelo), cómo está conectada (USB/paralelo/red, en qué PC) y con qué controlador? ¿Aceptas empezar por PDF-en-mm (A)? | PDF en mm; ESC/P (C) sólo si A falla |
| **D7** | Bug **Q1**: un cheque de $1,50 imprime «UN» sin centavos. ¿Corrijo (siempre «CON xx/100» y relleno)? | Recomiendo **corregir**; hoy se conserva (fiel) y avisa en pantalla. Cambio: `ConfiguracionCheque.CorregirMontosMenoresA2` |
| **D8a** | Bug **C1** (abono con el total pagado de la factura). ¿Abono = lo aplicado por el recibo? | Fiel por defecto; la página permite ver ambos y exportar con el corregido |
| **D8b** | Bug **C2** (rango de recibos comparado como texto; entran 513 y 514 en 5122–5146). ¿Comparar como número? | Fiel por defecto (opción en la página) |
| **D9** | Resumen de retenciones del PWC: ¿dinámico con rótulos correctos (recomendado) o las 4 columnas fijas 1 %/2 %/20 %/70 % con el rótulo de IVA cruzado como hoy? | Dinámico |

### 8.2 Decidido por defecto (se cambia sin dolor)

| # | Decisión |
|---|---|
| D1 | Menú: **Cartera** (PWC, Comisiones) y **Bancos** (Cheques), no «Ventas»/«Egresos». |
| D2 | Permisos `quRptPwc` / `quRptComis` / `quRptChq` con `GateProvisional`. |
| D5 | Fuente del cheque: monoespaciada 10 pt (configurable a Arial). |
| D6 | Monto del cheque con formato `1.234,56` (lo que hacía «Standard» con Windows es-EC); alternativa `1,234.56`. |
| D10 | El orden de PWC es `PostOrder`; el de Comisiones el del `.mdb` + desempate. |
| D11 | Quien puede ver un reporte puede editar su configuración (cosmética; se guarda quién y cuándo). Restringir a admins es una línea. |
| D12 | Totales en el Excel de PWC (`I:O`) y total general en Comisiones, más el resumen por ciudad de PWC: **adiciones**, no cambian ninguna cifra del original. |
| D13 | Las leyendas «COPIAR Y PEGAR FÓRMULA … EN COLUMNA L/M» del PWC se eliminan (eran instrucciones de pegado manual; ahora los resúmenes son fórmulas vivas). |

## 9. Fases y commits (todo en `main`, sin push)

| Fase | Contenido |
|---|---|
| **F0** | Este plan. |
| **F1** | Módulo `PsaWeb.Modules.Reportes`: scaffold, categorías y `AppCatalogo`, permisos + SQL, configuración por empresa (tabla + migración + servicio), `NumeroALetras`. |
| **F2** | PWC: repo ODBC/muestra, armador, página, Excel, endpoint, tests. |
| **F3** | Comisiones: ídem, con las opciones C1/C2. |
| **F4** | Cheques: repo, parser de referencia, modelo de página en mm, PDF, hoja de prueba, página, tests con posiciones en mm. |
| **F5** | Validación contra Sage local y los dos Excel; actualizar `ESTADO-MIGRACION-WEB.md`. |

**Deploy:** no se prepara hasta que lo pidas (zip + un script, estilo ya conocido). Antes del deploy: correr
`permisos-reportes-access.sql` y confirmar D3/D4 con una impresión real.

## 10. Lo que no pude verificar

- La impresión real (D3, D4): sin la matricial no hay forma de saber el origen ni la escala.
- El logo de Efemedio: `logoExa.png` está dentro del `.mdb` (binario), no en el volcado; se sube por la página.
- Demoradio no está en el Sage local: sólo se probó con Efemedio.
- El Sage local es una copia anterior a los Excel del usuario (3 facturas y 2 retenciones posteriores), de ahí la lectura «80 de 82».

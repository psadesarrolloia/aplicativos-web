# Plan — Ola 2 · Liquidación de Importaciones en Sage 50

Segundo aplicativo de la **Ola 2** (escritura en Sage), después de Compras (`PLAN-OLA2-COMPRAS-SAGE.md`). Reemplaza una pieza
de escritorio y un paso manual en Sage:

| Pieza actual | Qué hace | Destino |
|---|---|---|
| `Sage50usIntegration` → **Importaciones** (`Forms/ImportsMng/FrmImportMng` + `DetailsFromExcel`, `Services/sageContextCRUD/sageImportMgm`, `ExcelReports/ApportionImportsCPTDC`) | Toma los gastos de la cuenta de la importación, prorratea el costo a los ítems de stock y crea una **Purchase Order** por SDK | Módulo web **Liquidación de importaciones** (categoría **Compras**) |
| Paso manual en Sage (contabilidad) | Registra la compra `LIQ IMPORT nnn-aaaa` aplicando esa OC → capa de costo en `InventoryCosts` | **Sage Bridge**, conversión automática (decisión D1) |

El instalador «Sage 50 IntegracionFMimportaciones» (`.vdproj`) empaqueta el mismo exe (`Sage50usIntegration.csproj`); no hay
otro código.

La revisión del legado (fase 1, 2026-09-28) está resumida en §1; el detalle vive en la memoria
`ola2-liquidacion-importaciones-hallazgos` y en el código citado.

---

## 1. Hechos que condicionan el diseño (verificados 2026-09-28)

Medidos por ODBC (solo SELECT) en la original de CPTDC (`cptdcecuadorsa202520`) y en PeachEBills local.

1. **Cada importación = una cuenta GL propia** en Sage: `IMPORTACION nn-aaaa` (tipo 2, 138xx en 2026; 499 cuentas `IMPORTACION%`,
   10 inactivas, incluida la general `13300 IMPORTACIONES EN TRANSITO`). **La crea contabilidad a mano** en Sage (decisión D2: sigue
   así). El formulario lista `Chart` con `AccountDescription LIKE 'IMPORTACION%' + filtro`.
2. **Gastos y factura del exterior** = todas las filas de esa cuenta que no son OC (`JrnlKey_Journal <> 10`): compras de gastos locales
   (4/11 — muchas llegan como `OC-xxxx` del módulo Compras con la cuenta 138xx), pagos directos (2/5), diario general (0/1), NC de
   proveedor (4/12). Todas entran como gasto (`isCost = true`); el usuario **desmarca** la fila de la factura del exterior
   (`isCost = false`). Siempre hay exactamente 1 factura por importación (datos de PeachEBills).
3. **Ítems**: captura a mano (combo de ítems **StockItem**) o desde Excel (`DetailsFromExcel`: el usuario asocia columnas a
   Item Id / Cantidad / Valor Subtotal; ítem inexistente = se reporta y se omite). 1–18 ítems por importación.
4. **Prorrateo** (`btnApportion_Click`): exige Σ valor ítems = factura (a 2 decimales). Recorre los ítems **ordenados por valor**:
   `% = round(valor / factura, 8)`, `prorrateo = round(gastos × %, 2)`; el **último** recibe `gastos − Σ anteriores` y
   `% = round(1 − Σ%, 4)`. `Costo total = round(valor + prorrateo, 2)`, `Costo U = total / cantidad`.
5. **OC por SDK** (`sageImportMgm.loadingPOforImportsCost`), `JrnlKey 10 / JournalEx 18`, **total 0**:
   - `ReferenceNumber` = prefijo + «-» + número tecleados (`LIQ IMPORT-041-2026`; también `LIQ IMP DHL-001-2026`), fecha elegida,
     proveedor (`CPTDC-IMPORT-CHINA`, `-ZHONGCH`, `-HAIMO-A`…), dirección del proveedor, `AccountReference` (cuenta AP) = **la cuenta
     de la importación**.
   - Una línea por ítem: ítem, descripción, cantidad, `Amount = valor + prorrateo`, `UnitPrice = Amount / Quantity` (sin redondeo),
     cuenta = **cuenta de inventario del ítem** (13101, 13250, 13254…).
   - Línea final sin ítem: descripción `LIQUIDACION`, cuenta de la importación, `Amount = −Σ`.
   - Sin `ShipToAddress1` ni `ShipVia` → ni el worker COM ni `ConvertirOcs` ni la lista de Compras la tocan.
6. **Compra manual** (hoy): 198 OC `LIQ…` desde 2019, **todas** con compra (`JrnlKey 4 / JournalEx 11`); `Reference` =
   `LIQ IMPORT 041-2026` (**espacio** en vez del primer guion), `INV_POSOOrderNumber` = referencia de la OC, **misma fecha** en 186/198,
   total 0, filas de ítem con `IncludeInInvLedger = 1`, línea `LIQUIDACION` −total a la cuenta de importación. Cuenta AP de la compra:
   varía (20000 o la de la importación; irrelevante con total 0). Sage crea la capa `InventoryCosts` `MajorType 1` con
   `TransAmount = OptAmount` (Kardex §B4) y actualiza el saldo `MajorType 3`. La compra deja la cuenta de importación en 0.
7. **Estados** (`sageImportMgm`): sin datos / **En Tránsito** (sin guardar) / **En Tránsito \*** (guardada sin OC) / **En Proceso**
   (OC existe en Sage) / **Liquidada** (saldo de la cuenta en ±0,05). Hoy hay 5 en tránsito (13804–13808).
8. **PeachEBills**: `ImportCost` (RUC, cuenta, proveedor, `postOrderId` = PostOrder de la OC, `postOrderStrKey` = GUID,
   `postOrderNumber`, fecha), `ImportCostApportion` (ítems) e `ImportCostEx` (gastos con `isCost`). **Cada «Guardar» inserta un
   registro nuevo** y se lee el último (473 registros para 200 importaciones en CPTDC). Columnas `amountCFR`, `apportionCFR`,
   `apportionCFRPercent`, `postOrderSequence` y `costExType` nunca se usaron.
9. **Uso**: CPTDC (1792051800001) 200 importaciones 2019-02→2026-08 (41 en 2025, 43 en 2026 a septiembre ≈ 5/mes); **SANCEV ELECTRICA
   INDUSTRIAL** (1791313747001) 34 importaciones 2022-12→2026-05. 6–57 gastos por importación. Nombre de la base de SANCEV en Sage:
   desconocido (no hace falta para desarrollar: las pruebas van en la copia de CPTDC, decisión D5).
10. **Reporte** `ApportionImportsCPTDC` (EPPlus): título fijo «CPTDC ECUADOR S.A.» (también para SANCEV), código de importación =
    descripción de la cuenta; tabla gastos, tabla inventarios con % / prorrateo / total / costo unitario, resumen y firmas.
11. **Menú sin permiso** en el exe (cualquier usuario del exe lo abre).

## 2. Decisiones (usuario, 2026-09-28)

| # | Decisión |
|---|---|
| D1 | **Automatizar la compra**: el Bridge convierte la OC de liquidación en compra (`AddOrderLine`), sin paso manual. |
| D2 | La cuenta `IMPORTACION nn-aaaa` **se sigue creando a mano** en Sage. |
| D3 | **Guardar actualiza el mismo registro** de `ImportCost` (no más versiones). |
| D4 | Categoría **Compras**. |
| D5 | Pruebas y escritura **solo en la copia** «PUEBAS CPTDC ECUADOR S A 2025-2026» (`cptdcecuadorsa202525`). SANCEV se habilita en el corte. |

Se mantienen las reglas de la Ola 2: port fiel validado con datos reales; Bridge = anfitrión fijo (hash `99C8E111…`) + DLL de lógica;
sin convivencia exe + web (corte con guía); commit con lista explícita; push/deploy solo a pedido.

## 3. Arquitectura

```
Web (módulo Liquidación de importaciones)
  ├─ lee Sage por ODBC (cuentas IMPORTACION%, movimientos, OC/compra, ítems)     ← src/PsaWeb.Importaciones (lógica pura)
  ├─ lee/escribe PeachEBills (ImportCost*, actualización en el lugar)             ← PsaWeb.PeachEbills (entidades nuevas)
  └─ encola en TrabajosSage: GuardarOcLiquidacion → (encadena) ConvertirLiquidacion
Sage Bridge (net48 x86, mismo anfitrión)
  ├─ ManejadorGuardarOcLiquidacion: crea/actualiza la OC total 0
  └─ ManejadorConvertirLiquidacion: OC → compra `LIQ IMPORT nnn-aaaa` por AddOrderLine
```

- **`src/PsaWeb.Importaciones`** (netstandard/net9 como `PsaWeb.Compras`): `LectorImportaciones` (ODBC), `Prorrateo` (port fiel §1.4),
  `ArmadorOcLiquidacion`, `LectorExcelDetalle` (ClosedXML), `EstadoImportacion`. Reutiliza de `PsaWeb.Compras` el catálogo de ítems
  y proveedores (`LectorCatalogoCompras`) y la cuenta de inventario del ítem (`LineItem.InvAcctRecordNumber`).
- **Contratos** (`PsaWeb.SageBridge.Contratos`): `GuardarOcLiquidacion` (referencia, fecha, proveedor, cuenta de importación, líneas
  ítem/cantidad/monto/cuenta, PostOrder para actualizar) y `ConvertirLiquidacion` (PostOrder de la OC). `GuardarOc` no sirve tal cual
  (está hecho para la factura del SRI: retención, `ShipToAddress1`, líneas AUT-SRI).
- **Bridge**: se reutilizan sesión, cola, reciclado ante Btrieve, ventana de mantenimiento y auditoría. Los manejadores nuevos van en
  `PsaWeb.SageBridge.Logica` (solo cambia la DLL; el anfitrión no se re-autoriza).
- **PeachEBills**: mapear `ImportCost`, `ImportCostApportion`, `ImportCostEx` en `PeachEbillsContext.Compras.cs` (sin cambios de esquema).

## 4. Módulo web

Rutas bajo `/compras/importaciones` (misma categoría y navegación que Compras):

- **Lista**: cuentas `IMPORTACION%` de la empresa de sesión con estado (§1.7), proveedor, OC, compra, saldo, fechas. Filtro de texto
  como el exe; por defecto **activas** con opción «ver inactivas» (el exe mostraba las 499).
- **Detalle `/compras/importaciones/{cuenta}`**:
  - Gastos de la cuenta (lectura de Sage) con la marca **Factura del exterior** por fila; totales gastos / factura / importación.
  - Ítems: agregar/quitar, combo de ítems de stock con búsqueda, cantidad, valor; **Importar desde Excel** (asociar columnas como el
    exe, con vista previa y errores por fila).
  - **Prorratear** (§1.4) · **Guardar** (PeachEBills, en el lugar) · **Crear OC y compra** (encola; muestra el avance del trabajo).
  - Proveedor (catálogo), fecha, prefijo `LIQ IMPORT` + número propuesto a partir de la cuenta (`IMPORTACION 41-2026` → `041-2026`),
    editable.
  - Solo lectura cuando está En Proceso o Liquidada; **Reporte Excel** en cualquier estado con prorrateo completo.
- Permisos nuevos: `quimpliq` (ver) y `mkimpliq` (guardar / crear OC), con `GateProvisional` como en Compras hasta cargarlos.
  Auditoría en `AuditoriaRegistrosSage`.

## 5. Escritura en Sage

### 5.1 `GuardarOcLiquidacion`
Port de `loadingPOforImportsCost` (§1.5): `LoadVendor`, `PurchaseOrderFactory.Create` (o `Load` si hay PostOrder → actualización en el
lugar, como F3 de Compras), dirección del proveedor, cuenta AP = cuenta de la importación, líneas de ítem con cuenta de inventario,
línea `LIQUIDACION`. Antes de guardar: validar que la referencia no exista para otro PostOrder y que el total dé 0. Devuelve PostOrder
y GUID → se guardan en `ImportCost` (`postOrderId`, `postOrderStrKey`).

### 5.2 `ConvertirLiquidacion` (D1)
Se encola al terminar 5.1 (mismo trabajo o encadenado). `PurchaseInvoice` con `AddOrderLine` de cada línea de la OC:
`ReferenceNumber` = referencia de la OC con el primer guion cambiado por espacio (`LIQ IMPORT 041-2026`, convención §1.6), fecha =
fecha de la OC, proveedor y cuenta AP de la OC. Idempotente: si ya existe una compra con `INV_POSOOrderNumber` = la OC, no hace nada.
Se aceptan las diferencias ya conocidas de la F6 (`IncludeInInvLedger` 0 por SDK; la capa `InventoryCosts` sí se crea) si la F0 las
confirma para este caso.

### 5.3 Qué pasa si la liquidación cambia después
Igual que el exe: una vez hay OC la liquidación queda en solo lectura. Rehacerla = borrar compra y OC en Sage (contabilidad) → la web
detecta que el PostOrder ya no existe y vuelve a «En Tránsito \*» (comportamiento del exe).

## 6. Port fiel: qué se corrige y qué no

| # | Tipo | Qué | Tratamiento |
|---|---|---|---|
| C1 | Falsea | Título del reporte fijo «CPTDC ECUADOR S.A.» | Nombre de la empresa de sesión. |
| C2 | Pierde | Si la suma de gastos en Sage cambia, se descartan los guardados y se pierde la marca de la factura | Conservar la marca por fila (clave fecha + referencia + descripción + monto); avisar filas nuevas o desaparecidas. |
| C3 | Pierde | Cada guardado inserta un `ImportCost` nuevo (D3) | Actualizar el último registro en el lugar; los históricos quedan. |
| C4 | Automatiza | Compra manual (D1) | Trabajo `ConvertirLiquidacion`. |
| B1 | Verificar | Excel: números en texto con cultura del equipo (`1234.56` en es-EC → 123456) | En la web: celdas numéricas tal cual; texto con cultura invariante y error si es ambiguo. Validar con Excel reales. |
| B2 | Fiel | Último ítem absorbe residuo y su % se redondea a 4 decimales (los demás a 8) | Se mantiene. |
| B3 | Fiel | `UnitPrice = Amount / Quantity` sin redondeo | Se mantiene (la compra de Sage muestra ese precio). |
| B4 | Fiel | «Liquidada» = saldo de la cuenta en ±0,05 | Se mantiene. |

## 7. Fases

| Fase | Contenido | Sale |
|---|---|---|
| **F0** — spikes en la copia | (a) OC de liquidación por SDK = la del exe (total 0, línea sin ítem negativa, cuenta AP = cuenta de importación) sobre una cuenta `IMPORTACION` creada a mano en la copia; (b) compra por `AddOrderLine` de esa OC vs una compra manual: filas GL, `InventoryCosts` (capa y saldo), `IncludeInInvLedger`, cuenta AP; (c) actualización en el lugar de la OC. | Informe §9; ajuste del diseño de §5. |
| **F1** — lógica pura | `src/PsaWeb.Importaciones` + entidades PeachEBills. Arnés `ReconstruccionLiquidacionesTests`: para las ~200 liquidaciones de PeachEBills con OC, recalcular el prorrateo y armar la OC → **0 diferencias** con las OC reales de Sage. Tests del lector Excel (B1). | Prorrateo y OC idénticos a los reales. |
| **F2** — Bridge | `GuardarOcLiquidacion` + `ConvertirLiquidacion` (contratos, manejadores, encolado). Arnés de escritura en la copia: re-crear N liquidaciones reales → OC y compra = las de la original (salvo diferencias aceptadas en F0). | Escritura validada en la copia. |
| **F3** — módulo web | Lista, detalle, Excel, prorrateo, guardar en el lugar, crear OC + compra, permisos, auditoría, C2. Prueba del usuario en la copia. | Flujo completo en dev. |
| **F4** — reporte | Exportador ClosedXML (port de `ApportionImportsCPTDC` con C1). | `.xlsx` igual en layout al del exe. |
| **F5** — corte | Deploy junto con Compras; habilitar CPTDC y SANCEV en el Bridge (autorizar el anfitrión en cada compañía); retirar el menú «Importaciones» del exe con guía; verificar el Kardex de una liquidación real. | Exe sin Importaciones. |

## 8. Riesgos

- **OC total 0 / línea sin ítem por `AddOrderLine`**: no probado; F0-b lo resuelve. Plan B: compra por SDK con líneas propias (sin
  aplicar a la OC) o dejar la compra manual para ese caso.
- **`IncludeInInvLedger = 0`** en compras del SDK: el Kardex usa `InventoryCosts` (OK); falta revisar el reporte Item Ledger de Sage
  (pendiente también de Compras).
- **Coste de inventario**: la compra mueve el costo promedio de ítems de alto valor; cualquier error en el prorrateo se refleja en
  ventas posteriores. Mitigación: arnés F1 con 0 diferencias y confirmación explícita antes de encolar.
- **SANCEV**: no validado con sus datos (sin nombre de base). Mitigación: sondeo de solo lectura en el corte antes de habilitarla.
- **Fechas distintas OC/compra** (12/198): la compra automática usa la fecha de la OC; si contabilidad necesita otra, se agrega el
  campo en F3.

## 9. Informe de la F0

(pendiente)

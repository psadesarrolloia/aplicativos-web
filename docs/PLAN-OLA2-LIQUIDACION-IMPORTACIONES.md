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
| D6 | (Confirmada por el usuario 2026-10-01) **La compra se registra en un segundo paso** («Registrar compra»), no junto con la OC: en 15 de 200 OC reales contabilidad corrigió la OC en Sage antes de recibirla (9 con la cantidad pasada a metros). Mientras no hay compra, la OC se corrige desde la web (actualización en el lugar, C5). |

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

Rutas bajo `/compras/importaciones` (misma categoría y navegación que Compras). **Implementado (F3)**: la lógica quedó en
`src/PsaWeb.Compras/Importaciones/` (no en un proyecto aparte: comparte catálogos y la referencia a PeachEBills) y las páginas, el
lector Excel y el reporte en `modules/PsaWeb.Modules.Compras` (`Pages/Importaciones.razor`, `Pages/Importacion.razor`,
`Importaciones/`, `Servicios/ServicioLiquidaciones.cs`); el reporte se descarga por `/exportar/liquidacion-importacion` (Host; fuera de `/compras/importaciones/{cuenta}` para que la página no la tome como cuenta).


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
Se encola al terminar 5.1 (mismo trabajo o encadenado). `PurchaseInvoice` con `ReferenceNumber` = referencia de la OC con el primer
guion cambiado por espacio (`LIQ IMPORT 041-2026`, convención §1.6), fecha = vencimiento = descuento = fecha de la OC, términos vacíos,
proveedor y cuenta AP de la OC (la de la importación). Las líneas de ítem van con `AddOrderLine`; la línea `LIQUIDACION` va como línea
común (`AddPurchasesLine`) porque aplicada a la OC Sage exige cantidad, y al final se **cierra la OC** (`IsClosed`). Idempotente: si
ya existe una compra con `INV_POSOOrderNumber` = la OC, solo cierra la OC si seguía abierta. Resultado de la F0 en §9.

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
| C5 | Mejora | El `.exe` dejaba la liquidación en solo lectura apenas existía la OC | Editable mientras la OC no tenga compra; «Actualizar OC» la reescribe en el lugar (D6). |
| B5 | Fiel | El % se ve a veces con 1 ulp de diferencia: `Math.Round(double, 6)` de .NET Framework no es exacto y el de .NET 9 sí | Solo pantalla; el prorrateo en dinero es idéntico (200/200). |
| B6 | Falsea | Ya liquidada, la cuenta incluye la fila `LIQUIDACION` de la compra: con la regla del `.exe` la suma no coincidía, se perdían las marcas (200/200) y el reporte mostraba la factura y esa fila como gastos | C2 conserva las marcas; el reporte y el control de costeo excluyen la fila `LIQUIDACION` de la compra de la propia liquidación. |
| C6 | Decisión | La OC (y la compra) llevaban como cuenta por pagar la de la importación (el `.exe` la ponía «de prueba») | Cuenta por pagar **20000** en la OC; la compra la hereda (usuario, 2026-10-01). |
| C7 | Pierde | Compra con líneas aplicadas a la OC (`AddOrderLine`): el SDK deja la fila del ítem con `IncludeInInvLedger = 0` y **la compra no sale en el Item Costing Report** de Sage (visto por el usuario con la 042-2026) | Cada línea va como línea propia (`AddPurchasesLine`) y la OC se cierra: `IncludeInInvLedger = 1` como la manual y la compra sale en el reporte (CS-008, verificado por el usuario 2026-10-01). La compra ya no queda «aplicada» a la OC en Sage; la web la encuentra por su referencia. |

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

- **OC total 0 / línea sin ítem por `AddOrderLine`**: resuelto en la F0 (§9.3): línea común + cierre de la OC.
- **`IncludeInInvLedger = 0`** en compras del SDK con `AddOrderLine`: resuelto en la liquidación con C7. **Sigue abierto en Compras (F6)**, ver §15. Antes: el Kardex usa `InventoryCosts` (OK); falta revisar el reporte Item Ledger de Sage
  (pendiente también de Compras).
- **Coste de inventario**: la compra mueve el costo promedio de ítems de alto valor; cualquier error en el prorrateo se refleja en
  ventas posteriores. Mitigación: arnés F1 con 0 diferencias y confirmación explícita antes de encolar.
- **SANCEV**: no validado con sus datos (sin nombre de base). Mitigación: sondeo de solo lectura en el corte antes de habilitarla.
- **Fechas distintas OC/compra** (12/198): la compra automática usa la fecha de la OC; si contabilidad necesita otra, se agrega el
  campo en F3.

## 9. Informe de la F0 (2026-09-28, copia de prueba)

Manejadores `GuardarOcLiquidacion` y `ConvertirLiquidacion` en `PsaWeb.SageBridge.Logica` (el anfitrión no cambió: hash `99C8E111…`,
candado `SoloBases` = copia) y arnés `tests/PsaWeb.Compras.Tests/Validacion/LiquidacionCopiaTests` (`PSAWEB_TEST_F0_LIQ=1`): rehace por
el Bridge la OC de una liquidación real con la referencia `LIQ PRUEBA-…`, la actualiza en el lugar, la convierte en compra dos veces y
compara todas las columnas de `JrnlHdr`/`JrnlRow` y las capas de `InventoryCosts` con las reales. Corrido sobre
`LIQ IMPORT-041-2026` (2 ítems, cable) y `LIQ IMPORT-028-2026` (18 ítems, bombas).

### 9.1 F0-a — OC de liquidación por SDK = la del `.exe` (✅)
Sin diferencias salvo las esperadas: la real ya está recibida (`POSOisClosed`, `QtyReceived`, `AmountReceived`) y la de prueba se
actualizó en el lugar (`DistNumber`/`LastUsedDistNumber` renumerados, como en la F3 de Compras). Total 0, línea `LIQUIDACION` sin
ítem, cuenta AP = cuenta de la importación, cuenta de inventario del ítem: todo igual.

### 9.2 F0-c — actualización en el lugar (✅)
Mismo PostOrder y GUID; una OC ya convertida en compra se rechaza («ya se convirtió en compra: no se puede actualizar»).

### 9.3 F0-b — compra por el Bridge vs compra manual (✅ con diferencias documentadas)
- **Aplicar la línea `LIQUIDACION` a la OC no se puede por SDK**: `TransactionLineNotUsedProblem` («Quantity can't be zero»). Va como
  línea común (misma cuenta, monto y descripción) y la OC se cierra aparte (`IsClosed = true`). Diferencia: en la manual esa fila queda
  enlazada a la OC (`LinkToOtherTrxIndex`, `LinkJournalRowEx`) y la OC anota `AmountReceived` −total en su fila 3; en la del Bridge no.
  La OC queda cerrada igual.
- **Capas de costo idénticas**: `InventoryCosts` `MajorType 1` igual en cantidad, `TransAmount` y `OptAmount` (18/18 y 2/2) y el saldo
  `MajorType 3` del día se actualiza. Asiento idéntico en cuentas y montos.
- Cabecera igual a la manual en la 028 (cuenta AP de la importación). La manual usa 20000 en 22 de 198 compras (la 041 es una): se
  sigue la mayoría (cuenta de la OC).
- Diferencias de filas del SDK (mismas que las del worker/Bridge de Compras): `IncludeInInvLedger` 0 en la fila principal del ítem; en
  las dos filas secundarias de cada ítem (costo de venta y contrapartida de inventario, monto 0) el SDK deja `Quantity`/`UnitCost` en 0
  donde la pantalla de Sage pone la cantidad; `StockingUnitCost` con todos los decimales (la pantalla lo deja en 5).
- Avisos de `Validate()` aceptados: fecha fuera del período actual y «Main account is not of payable type» (la cuenta de la
  importación, igual que la manual). Solo los errores frenan.
- Idempotencia: la segunda conversión devuelve `YaExistia` sin crear otra compra.

### 9.4 Pendiente del usuario (en la copia)
Abrir en Sage la compra `LIQ PRUEBA 028-2026` y la OC `LIQ PRUEBA-028-2026`, y revisar el **Item Ledger** y la **Inventory
Valuation** de un ítem (p. ej. `ES-217`) y la lista de OC abiertas: confirmar que las diferencias de §9.3 no se ven (es el mismo punto
pendiente de la F6 de Compras).

### 9.5 Datos de prueba que quedan en la copia
OC 104916 (`LIQ PRUEBA-041-2026`) y 104918 (`LIQ PRUEBA-028-2026`), cerradas; compras 104917 y 104919. Las cuentas 13803 y 13789
quedan con saldo −1.577.713,38 y −593.450,45 y los ítems con la existencia duplicada (se liquidó dos veces la misma importación).

## 10. F1 — Lógica pura (implementada 2026-09-29)

- `src/PsaWeb.Compras/Importaciones/`: `Liquidacion.cs` (modelo, estados §1.7, `Prorratear` §1.4 en double como el `.exe`,
  `CosteoCompleto`, `Conciliar` C2, referencia y número propuesto, `ArmadorOcLiquidacion`), `LectorImportaciones.cs` (ODBC, solo
  SELECT, consultas del `.exe`) y `RepositorioLiquidaciones.cs` (PeachEBills: última liquidación, guardado en el lugar C3, anotar OC).
- Entidades `ImportCost`, `ImportCostApportion`, `ImportCostEx` en `PsaWeb.PeachEbills` (esquema existente, sin migración; las columnas
  CFR sin uso se guardan en 0 como el `.exe`).
- Arnés `Validacion/ReconstruccionLiquidacionesTests` (copia de CPTDC, solo lectura): **prorrateo idéntico 200/200**; **OC idéntica
  185/200**; las otras 15 las corrigió contabilidad en Sage después de crearlas (lista explícita en el arnés: 9 con la cantidad pasada a
  metros con el mismo monto, ítems cambiados, un monto cambiado, línea LIQUIDACION contra cuentas renombradas o divididas).
- `Importaciones/LiquidacionTests`: reglas puras y el prorrateo recalculado de **todas** las liquidaciones guardadas de las dos empresas
  (incluida SANCEV) sin diferencias.

## 11. F2 — Bridge (implementada 2026-09-29)

- Manejadores de la F0 (`ManejadoresLiquidacion.cs`) sin cambios de diseño; el anfitrión sigue en `99C8E111…`.
- `Validacion/LiquidacionCopiaTests` endurecido: filas de cada ítem ordenadas de forma estable y cada diferencia clasificada como
  esperada (§9) o inesperada (falla). Corrido en la copia con 041, 028 y 039-2026: **0 diferencias inesperadas**; rechazo de actualizar
  una OC ya contabilizada, conversión idempotente y cierre de la OC verificados. `PsaWeb.SageBridge.Tests` 47/47.

## 12. F3 — Módulo web (implementada 2026-09-29, sin probar en el navegador)

- Lista `/compras/importaciones` (estado, proveedor, OC, saldo; filtro, estado, inactivas) y formulario
  `/compras/importaciones/{cuenta}`: filas de la cuenta con la marca «factura del exterior» (C2, filas nuevas marcadas y avisos),
  ítems (a mano o desde Excel con asociación de columnas, B1), «Prorratear», «Guardar» (en el lugar), «Crear OC» / «Actualizar OC»,
  «Registrar compra» (D6), seguimiento del trabajo del Bridge y anotación del vínculo al terminar; recuperación del vínculo por
  referencia si la página se cerró antes.
- Permisos `quimpliq`/`mkimpliq` con GateProvisional (mientras no se carguen, los habilitan las llaves de Compras); filas agregadas a
  `docs/sql/permisos-compras-ventas.sql` (no aplicado). Entrada «Liquidación de importaciones» en `AppCatalogo` (Compras) y enlace
  desde `/compras`. Auditoría en `AuditoriaRegistrosSage` (módulo `LiquidacionImportaciones`).
- `Validacion/LecturasLiquidacionTests` (copia, solo lectura): 499 cuentas (321 liquidadas, 157 en tránsito, 18 sin movimientos);
  C2 conserva la marca en 194 de las 200 liquidaciones que la regla del `.exe` perdía (B6); OC, compra, ítems (2.065) y proveedores
  (2.848) leídos. Suites `Seguridad`, `Modules.Compras`, `PeachEbills`, `Modules.ComprobantesElectronicos` en verde. El Host compila
  (a una carpeta aparte: Visual Studio tiene bloqueado su `bin`).

## 13. F4 — Reporte (implementada 2026-09-29)

- `modules/PsaWeb.Modules.Compras/Importaciones/ReporteLiquidacion.cs` (ClosedXML): mismo layout, fórmulas y bordes que
  `ApportionImportsCPTDC`; título = nombre de la empresa (C1); sin la fila de la compra (B6). Prueba que reabre el `.xlsx` y verifica
  celdas y fórmulas; reportes de 038 y 035-2026 reales generados en `%TEMP%\psa-f4-*.xlsx`.

## 14. Pendiente antes del corte (F5)

1. **Usuario**: probar en dev contra la copia el flujo completo en `/compras/importaciones` (lista, abrir una importación en tránsito
   —13804…13808—, marcar la factura, cargar ítems a mano o por Excel, prorratear, guardar, crear OC, revisarla en Sage, registrar la
   compra, reporte) y comparar el reporte con el del `.exe` de una liquidación real.
2. **Usuario**: confirmar D6 (compra en segundo paso) o pedir la compra junto con la OC.
3. **Usuario**: revisión de §9.4 (Item Ledger / Inventory Valuation en Sage de `LIQ PRUEBA 028-2026`).
4. Nombre de la base de SANCEV en Sage (sondeo de solo lectura antes de habilitarla).

## 15. Prueba del usuario y ajustes (2026-09-29 → 2026-10-01)

- Flujo completo en la web con la **LIQ IMPORT-042-2026** (cuenta 13804, TU-004) en la copia: OC 104922 y compra 104923, cuenta en 0,
  liquidación vinculada en PeachEBills. Ajustes que salieron de la prueba:
  - Arnés: deja la empresa del Bridge como estaba (antes la deshabilitaba).
  - Página: no deja encolar otra OC/compra de la misma importación mientras haya un trabajo pendiente, retoma su seguimiento al
    recargar y avisa si no hay ningún Sage Bridge en marcha.
  - Reporte: descarga por `/exportar/liquidacion-importacion` (la ruta anterior caía en la de la página), motivo visible cuando no se
    puede y, ya liquidada, sin exigir el cuadre (como el `.exe`).
  - **C6** (cuenta por pagar 20000) y **C7** (líneas propias en la compra: el Item Costing Report no mostraba la compra de la 042).
- §9.4 resuelto: con `AddOrderLine` la compra **no** aparece en el Item Costing Report; con C7 sí (CS-008: la manual y la del Bridge
  con la misma cantidad y costo). La compra 104923 de la 042-2026 quedó con la forma anterior: para corregirla en la copia hay que
  borrarla en Sage y volver a registrarla desde la web («Registrar compra»).
- **Compras (F6) — pendiente**: `ConvertirOcs` también usa `AddOrderLine`. En la original, las compras del worker COM del último año
  tienen `IncludeInInvLedger = 1` en todas sus filas de ítem (13.437 de ítems no de stock y 15 de stock en 8 compras); las del Bridge
  quedarían en 0 y no saldrían en los reportes por ítem de Sage. Hay que decidir y probar el mismo cambio allí (allí la compra sí
  debe seguir enlazada a la OC por `PurchaseOrderSync`, no por la aplicación en Sage).
- Datos de prueba en la copia (además de §9.5): OC/compra 104920/104921 (039), 104924/104925 (038), 104926/104927 (037) y la 042
  real de la prueba del usuario (104922/104923). Las cuentas 13799–13801 quedan con saldo por la doble liquidación.

## 16. Vínculo compra–OC: ensayos y decisión final (2026-10-01)

- El usuario pidió primero que la compra **reciba el inventario desde la OC** (vinculada). Ensayos en la copia:
  - SDK `AddOrderLine` en 6 combinaciones de campos (031–033-2026): las que Sage acepta quedan vinculadas (`INV_POSOOrderNumber`,
    `LinkToAnotherTrx`) pero siempre con `IncludeInInvLedger = 0`; volver a guardar la compra no lo cambia. El SDK no tiene otra vía
    (no hay importación ni «recibir inventario»).
  - COM como el worker `PSComInvoiceGenerate` (importación del diario de compras con `AppliedToPO`, librería `ManagedCOM`): la consola
    de prueba conecta al Sage abierto pero `Login.GetApplication` devuelve `E_ACCESSDENIED` (aplicación no autorizada o credenciales).
- **Decisión del usuario (2026-10-01): se queda C7** — la compra con líneas propias (afectan el inventario y salen en el Item Costing
  Report) y la OC se cierra. Validado otra vez con la 030-2026 (OC 104934, compra 104935: 4 ítems con `IncludeInInvLedger = 1`, AP
  20000, OC cerrada). El código de los ensayos se descartó; la consola COM queda solo en el scratchpad.
- Datos de prueba nuevos en la copia: OC/compra 104928/104931 (031), 104929/104932 (032), 104930/104933 (033) — vinculadas con
  `IncludeInInvLedger = 0` — y 104934/104935 (030).
- **Compras (F6) — decisión del usuario (2026-10-01): no se aplica C7.** En las compras normales el vínculo OC↔compra es
  indispensable; la pérdida del vínculo solo se acepta en las liquidaciones de importación (pocas al mes). `ConvertirOcs` sigue con
  `AddOrderLine` (vinculada, `IncludeInInvLedger = 0`); el efecto en los reportes por ítem de Sage queda como punto abierto de Compras,
  a resolver antes de apagar el worker COM en cada empresa.

## 17. SANCEV (2026-10-01, solo lectura)

- Base de Sage: carpeta `C:\Sage\Peachtree\Company\sancialu`, base ODBC `sancevcialtda2202520` (la de `PeachConnString`).
- 44 cuentas `IMPORTACION%` (`IMPORTACION-001-2023`…, renombradas luego con sufijo: `13801-339`, `13834-340`); 24 liquidaciones con OC
  desde 2023, prefijos variados (`IMPORT-`, `LQ IMPORT-`, `LIQ IMPORT-`, `LIQ-IMPORT-`).
- Reconstrucción F1 contra su base: las diferencias son de datos (8 cargas de saldo inicial de dic-2022 sin gastos ni prorrateo, 4 OC de
  saldo inicial borradas, cuentas renombradas, OC corregidas a mano en Sage), no del port.
- Convenciones distintas de CPTDC, ahora deducidas por empresa (sin valores fijos):
  - **Cuenta por pagar** (C6): la más usada por sus compras del último año entre las de tipo «por pagar» → CPTDC `20000`, SANCEV
    `20000-513`.
  - **Referencia de la compra**: según sus liquidaciones de los últimos dos años → CPTDC con espacio (`LIQ IMPORT 041-2026`), SANCEV
    igual a la de la OC (`LIQ-IMPORT-002-2026`). La web la manda al Bridge (`PayloadConvertirLiquidacion.ReferenciaCompra`).
  - **Referencia propuesta para la OC**: la de su última liquidación con el número cambiado (`LIQ-IMPORT-003-2026` → `LIQ-IMPORT-004-2026`).
- Prueba `Validacion/ConvencionesSancevTests` (con `PSAWEB_TEST_RUC_COMPRAS=1791313747001` y su base). Antes de habilitar SANCEV en el
  Bridge: autorizar el anfitrión en esa compañía (Probar → Always allow access).
- Ojo Compras: `ArmadorOc.CuentaPorPagar` es `20000` fijo; en SANCEV esa cuenta no existe (es `20000-513`). Revisar antes de usar el
  módulo Compras en SANCEV.
- 2026-10-01: el usuario confirmó la cuenta por pagar por empresa (SANCEV `20000-513`). Autorización del Bridge en SANCEV en PREDATOR:
  «Probar» → Pending → el usuario eligió *Always allow access* → **Granted** (compañía «SANCEV CIA. LTDA.-2- 2025-2026», 391 cuentas
  leídas). SANCEV se agregó a `SoloBases` solo para la prueba (config de desarrollo restaurada) y **sigue sin habilitar** en el Bridge.
  En SERWEBPSA01 hay que repetir la autorización con la cuenta del servicio (la autorización es por ejecutable y cuenta de Windows).
- 2026-10-01: prueba completa del usuario en la web con la **LIQ IMPORT-043-2026** (cuenta 13805): OC 104944 creada por el Bridge y compra
  registrada desde la web con la forma definitiva (C6 cuenta por pagar de la empresa, C7 líneas propias + OC cerrada). El usuario revisó
  todo en Sage: **OK**. Pendiente para el corte (F5): despliegue (cuando el usuario lo pida), autorizar y habilitar CPTDC y SANCEV en el
  Bridge del servidor, permisos `quimpliq`/`mkimpliq` y guía de retiro del menú «Importaciones» del exe.

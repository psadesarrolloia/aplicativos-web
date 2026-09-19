# Plan — Comprobantes electrónicos v2 (Facturas · Retenciones · Notas de crédito · Liquidaciones)

Estado: **plan aprobado en lo esencial (2026-09-19); F0 (investigación SRI/Datil) ejecutada.**
Sigue todo en **DRY-RUN** (`Datil:DryRun=true`) — decisión 2026-09-07: nada de emisión
real hasta migrar los 26 aplicativos.

## 1. Objetivo

La v1 dejó 4 tipos de comprobante con interfaces parecidas pero no iguales
(Retenciones vive en su propio módulo y funciona distinto). La v2:

1. **Un solo módulo** con un submódulo por tipo. Los 4 comparten funcionalidad e interfaz.
2. **Mismos permisos** para los 4 en `allowAction`.
3. **Estado en el SRI** (columna, verificación individual y masiva, worker) para los 4.
4. **Anulaciones** (solicitud + seguimiento hasta que el SRI la confirme) para los 4.

## 2. Decisiones tomadas (2026-09-19)

| # | Decisión |
|---|---|
| 1 | Un solo módulo «Comprobantes electrónicos»; rutas `/fe/facturas`, `/fe/retenciones`, `/fe/notas-credito`, `/fe/liquidaciones`. `/retenciones` redirige a `/fe/retenciones`. |
| 2 | Los 4 tipos: «Generar» por fila, «Procesar lote», **rango de fechas**, y para ver hay que **elegir una empresa** (mi empresa / todas → clic en una). Se da de baja «Ejecutar todas» de la interfaz (el worker de emisión de retenciones se conserva, apagado). |
| 3 | «Solicitar anulación» en los 4. Seguimiento de anulación para los 4. **Facturas y liquidaciones no requieren aceptación manual del receptor en el SRI** (se anulan directo); retenciones y notas de crédito **sí** (el receptor acepta; si no acepta, a los 5 días vuelve a vigente). La validación contra el SRI aplica igual a los 4. |
| 4 | Se autoriza consultar el WS público del SRI **y** la API de Datil (solo lectura) para validar. |
| 5 | Permisos: ver §5 (propuesta a confirmar). |
| 6 | Se sigue en DRY-RUN. |
| 7 | Cobro/emisión real y SMTP quedan para la fase de pruebas reales. |
| 8 | **Alcance temporal: últimos 2 años** (vigencia máxima de la contabilidad en tiempo real en Sage; lo anterior queda en backups y no se verifica). Backfill de claves y verificación se limitan a esa ventana. |
| 9 | **No existe (ni en el servidor real) ningún caso de anulación pendiente de aceptación** que sirva para observar ese estado en el WS. Se diseña de forma defensiva: ver §7. |
| 10 | Permisos de §5 aprobados tal como están planteados (7 filas nuevas). |

## 3. Hallazgos de la fase F0 (medidos el 2026-09-19 sobre la copia local de `PeachEBills` + WS reales)

### 3.1 WS público del SRI (`ConsultaComprobante`, `cel.sri.gob.ec`)

- **Solo responde por comprobantes recientes.** Con fecha de emisión 03/08/2026 sí
  respondió; con 31/07/2026 y anteriores devolvió
  `No es posible validar la clave de acceso ya que la fecha de emision esta fuera del rango permitido.`
  (probado hoy 19/09: **todo lo emitido hasta el 31/07 rechazado, todo desde el 01/08 aceptado**,
  sobre 1.453 claves; lo de 2018–2025 también rechazado). Regla observada: el rango cubre
  **el mes en curso + el mes anterior completo** (a confirmar el 01/10: debería pasar a
  empezar el 01/09). **Consecuencia:** la ventana de 90 días del worker de Conciliación
  es mayor que lo que el WS acepta → esas filas se cuentan hoy como «error del servicio».
  Hay que acotar la ventana y tratar «fuera de rango» como estado propio, no como error.
- **Sí distingue `ANULADO`.** Respuesta real de una factura anulada:
  `estadoAutorizacion=ANULADO`, `tipoComprobante`, `rucEmisor`, `fechaAutorizacion`
  (no trae fecha de anulación ni «pendiente de aceptación»). Hoy el cliente lo clasifica
  como `NoAutorizado` (cualquier valor distinto de `AUTORIZADO`).
- **Hallazgo de negocio:** la factura `001-003-000013292` (RUC 1791313747001) figura
  `ANULADO` en el SRI y en Datil, pero en `PeachEBills` sigue con `IsValid=1` →
  exactamente la discrepancia que esta función debe detectar.
- Las notas de crédito de 2019 y las retenciones/facturas viejas ya no se pueden consultar
  por WS (fuera de rango) aunque estén marcadas `IsValid=0` localmente.

### 3.2 API de Datil (`GET link.datil.co/invoices/{id}`)

- Devuelve `estado` (`AUTORIZADO` / `ANULADO`) y `autorizacion{estado,mensajes,numero,fecha}`;
  para la factura anulada arriba devolvió `estado=ANULADO` (coincide con el SRI).
- **No tiene la restricción de antigüedad** del WS del SRI: probado el 2026-09-19 con
  facturas de 06/2026, 03/2026, 12/2025, 09/2025, 03/2025 y 11/2024 (≈ 22 meses), una nota de
  crédito (08/2025) y una retención (03/2025): **todas respondieron** `estado`, `autorizacion`
  y `clave_acceso` de 49 dígitos. Cubre la ventana de 2 años decidida (decisión 8).
- **Estrategia de fuentes:** comprobantes dentro del rango del SRI (mes en curso + anterior) →
  WS del SRI (fuente de verdad); más antiguos dentro de los 2 años → Datil. Datil además
  resuelve la **clave de acceso** cuando `DatilRequests` no la tiene (caso de las NC recientes).
- Limitación a tener presente: el `estado` de Datil es el que Datil conoce; para el
  rango reciente se prefiere el SRI directo.

### 3.3 Clave de acceso

- `Facturas` **no tiene** columna de clave de acceso; `TaxWithHoldings.claveAcceso` existe pero
  está **vacía en las 41.850 filas**.
- Se recupera de `DatilRequests.DatilRequest` (JSON de Datil con `clave_acceso`):
  **74.074 de 74.605 filas (99,3 %)** tienen una clave de 49 dígitos → backfill viable.
  Emisiones nuevas guardarán la clave al emitir (`DatilResult.ClaveAcceso`).

### 3.4 Retenciones — diferencias técnicas con los otros 3

- Sus «pendientes» salen de la tabla SQL `PurchaseOrderSync` (`POPostOrder`, `PIPostOrder`,
  `RUCTransmitter`) — **no tiene fecha**. Para el rango de fechas hay que consultar la
  fecha de la compra en Sage 50 (como ya hacen los pendientes de FE) → el panorama por
  empresa deja de ser SQL-puro para retenciones si se filtra por fecha.
- El procesador emite en bloque por empresa; la unidad «una compra» existe dentro del loop
  (`LectorCompraRetencion` → `RetencionBuilder` → `EmitirRetencionAsync` → `GuardarAsync`) y
  se extrae a `ProcesarUnaCompraAsync(ruc, piPostOrder)`.
- Marca de anulación local: `IsValid` / `ChangeIsValidDate` en `Facturas` y `TaxWithHoldings`
  (copia local: 5 facturas y 4 retenciones con `IsValid=0`, 0 notas de crédito).

## 4. Arquitectura propuesta

- Módulo `PsaWeb.Modules.ComprobantesElectronicos` (renombre de `…FacturacionElectronica`).
- `TipoComprobante` {Factura, Retencion, NotaCredito, Liquidacion} + estrategia por tipo
  (`IServicioTipoComprobante`): título, código SRI (01/07/04/03), etiqueta de persona
  (Cliente/Proveedor), permisos, listar pendientes (con rango), procesar uno, procesar lote,
  listar guardados, detalle.
- Una sola página genérica `Comprobantes.razor` + 4 wrappers de ruta.
- `TableroComprobantes` gana un lector de retenciones (`TaxWithHoldings`/`THDetails`).
- Migración **sin cambiar comportamiento** por fases; Retenciones se absorbe al final,
  con sus 19 tests como red de seguridad.

## 5. Permisos (propuesta — pendiente de confirmar nombres)

Patrón existente en `allowAction`: `qu*` = ver, `mk*` = hacer, `*Batch` = lote,
`auCance*` = autorizar anulación.

| Tipo | Ver | Hacer | Lote | Autorizar anulación |
|---|---|---|---|---|
| Facturas | `qusaleinv` ✔ | `mksaleinv` ✔ | `mksinBatch` ✔ | `auCanceInv` (nuevo) |
| Retenciones | `qupurchtwh` ✔ | `mkpurchtwh` ✔ | `mkTwhBatch` ✔ | `auCanceTwh` ✔ |
| Notas de crédito | `qusalenc` ✔ | `mksalenc` ✔ | `mkncBatch` (nuevo) | `auCanceNc` (nuevo) |
| Liquidaciones | `qupurchliq` (nuevo) | `mkpurchliq` (nuevo) | `mkliqBatch` (nuevo) | `auCanceLiq` (nuevo) |

= **7 filas nuevas** + asignación a roles copiando el de su equivalente (para no dar ni quitar
acceso por sorpresa). «Verificar en el SRI»: individual con «Ver», masivo con «Lote».
Sin permiso de «Autorizar anulación» el usuario solo **solicita** la anulación (correo al
Supervisor, como hoy en retenciones). Al cargar los códigos desaparece `GateProvisional`.
El script SQL lo genera este plan y lo ejecuta el usuario.

## 6. Estado en el SRI

- Capa común (sacada de `PsaWeb.Conciliacion`): `IVerificadorEstadoSri`, gate de una corrida a
  la vez, worker, colores/textos de la columna.
- **Tabla propia** `EstadoSriComprobante` (BD de Conciliación, no en `PeachEBills`): tipo,
  RUC, id de referencia, clave de acceso, estado, respuesta cruda, fecha de verificación,
  fuente (SRI / Datil), datos de anulación (§7). No se agregan columnas a `PeachEBills`
  (lo usan los `.exe` de escritorio).
- Estados: Sin verificar · Autorizado · **Anulado** · Anulación solicitada · No autorizado ·
  Fuera de rango del SRI · No encontrado · Formato inválido · Error del servicio.
- Verificación individual (botón por fila) y masiva («Verificar pendientes (N)») con el mismo
  gate y máx. 5 llamadas simultáneas.
- Worker: ventana acotada al rango real del WS; para comprobantes fuera de rango, Datil.
- Backfill de claves desde `DatilRequests`.

## 7. Anulaciones

- «Solicitar anulación» **guarda** la solicitud (quién, cuándo, tipo) además de enviar el correo.
- El SRI **no tiene API** para anular: la anulación real la hace una persona en el portal;
  la app la solicita y la **rastrea**.
- Máquina de estados por comprobante: `Vigente → Solicitada → (Anulado | Vigente por vencimiento de 5 días
  [retenciones y NC] | Rechazada)`. Facturas y liquidaciones: `Vigente → Solicitada → Anulado`.
- El worker verifica a diario los documentos con solicitud abierta y marca discrepancias:
  «anulado en el SRI pero vigente en PeachEBills/Sage» y viceversa.
- **Diseño defensivo (decisión 9):** no hay un caso real para observar «pendiente de aceptación»
  en el WS, así que el seguimiento **no depende de que el SRI lo informe**: se apoya en la fecha
  de solicitud que guarda la propia app.
  - Solicitud abierta + WS dice `ANULADO` → **Anulado (confirmado)**.
  - Solicitud abierta + WS dice `AUTORIZADO` → **Anulación solicitada** (pendiente); se reverifica a diario.
  - Retenciones/NC: solicitud abierta > 5 días y WS sigue `AUTORIZADO` → **Vigente (anulación vencida)**;
    se cierra el seguimiento y se avisa.
  - Cualquier valor de `estadoAutorizacion` distinto de `AUTORIZADO`/`ANULADO` → estado
    **«Otro: <valor>»** (no se lo confunde con «No autorizado») y se guarda la respuesta cruda,
    de modo que si el SRI llega a informar el estado intermedio quedará registrado sin cambiar código.
  - Facturas/liquidaciones (sin aceptación del receptor): `Solicitada → Anulado` directo.

## 8. Fases

| Fase | Contenido |
|---|---|
| F0 ✔ | Investigación SRI/Datil/claves (§3, §9). **Cerrada** (huecos resueltos 2026-09-19: alcance de 2 años; Datil cubre esa ventana; sin caso de «pendiente de aceptación» → diseño defensivo §7). Único punto por reconfirmar más adelante: el rango del WS el 01/10 (debería empezar el 01/09). |
| F1 | Permisos: script SQL, `Permisos.cs`, `AppCatalogo`, quitar `GateProvisional`. |
| F2 | Módulo único con `TipoComprobante`; FE ya migrado; extraer `ProcesarUnaCompraAsync`, pendientes con rango y tablero de retenciones. |
| F3 | Interfaz unificada de los 4 (Generar por fila, lote, rango, elegir empresa, solicitar anulación). Absorber Retenciones + redirección. |
| F4 | Estado SRI: tabla, backfill de claves, columna, verificación individual y masiva. |
| F5 | Worker de verificación para los 4 + ajuste de la ventana de Conciliación. |
| F6 | Seguimiento de anulaciones y discrepancias. |

Cada fase se despliega por separado (redeploy completo o swap de DLL según el caso), siempre en DRY-RUN.

## 9. Resultados del escaneo masivo del WS del SRI (2026-09-19)

Universo: 1.453 comprobantes emitidos en producción entre 27/07 y 03/09/2026 (copia local de
`PeachEBills` restaurada el 03/09), consultados de a 1 clave, solo lectura.

| Resultado del WS | Cantidad | Nota |
|---|---|---|
| `AUTORIZADO` | 1.349 | 440 facturas · 4 liquidaciones · 905 retenciones |
| **`ANULADO`** | **25** | **17 facturas + 8 retenciones**; los 25 figuran vigentes (`IsValid=1`) en la copia local |
| Fuera de rango del SRI | 68 | facturas emitidas ≤ 31/07 |
| Error de red transitorio | 11 (0,8 %) | «Could not establish trust relationship» (TLS); pasan al reintentar → el cliente necesita **reintentos** |
| Cualquier otro valor | **0** | **no apareció ningún «pendiente de aceptación de anulación»** |

Lectura:

- El WS emite solo **`AUTORIZADO` / `ANULADO`** (+ el error de rango). Los **25 anulados = 1,8 %** del
  universo son discrepancias candidatas a «anulado en el SRI, vigente en PeachEBills». Ojo: la copia
  local es del 03/09; producción podría ya tenerlas marcadas → confirmar contra producción.
- **No se observó el estado intermedio** (solicitud pendiente de aceptación). O el WS sigue
  devolviendo `AUTORIZADO` hasta que el receptor acepta, o no se dio el caso en la muestra.
  **F0b:** el usuario indica 1–2 retenciones/NC con anulación solicitada y aún pendiente hoy en el
  portal del SRI, y se consulta su clave en el WS y en Datil para ver qué devuelve cada uno.
- **Notas de crédito:** las 6 NC recientes (agosto) no tienen fila en `DatilRequests`, así que no
  se pudieron incluir. La clave de acceso debe poder recuperarse también por
  `GET Datil` con el `DatilID` (que sí tienen). **Orden de recuperación de la clave:**
  `DatilRequests` → si no hay, `GET Datil` → si no, sin clave (estado «no verificable»).
- Volumen para dimensionar el worker: ~1.400 comprobantes por mes-y-medio de una muestra de
  ~14 empresas → ≈ 1.000 consultas por corrida completa, ~2 s cada una en serie
  (≈ 8 min con concurrencia 4–5).

## 10. Riesgos

- Refactor de Retenciones en producción → fases sin cambio de comportamiento + tests.
- Carga sobre el WS del SRI desde la IP del servidor → concurrencia máx. 5, umbral entre verificaciones.
- El WS del SRI podría cambiar/ limitar consultas; Datil como respaldo.
- Sin casos reales de «pendiente de aceptación de anulación» no se puede afirmar cómo se ve; se
  guarda la respuesta cruda para no perder información desconocida.

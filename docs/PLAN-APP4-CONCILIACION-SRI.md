# Plan — Módulo de Conciliación SRI

Ola 1 lleva 4 módulos en producción (Cierre de Caja, Kardex, Retenciones,
Facturas/NC/Liquidaciones) más ATS recién cerrado. Este documento planifica
el **siguiente módulo: Conciliación SRI** — investigación + decisiones, listo
para arrancar implementación en un chat aparte.

**Distinto a todos los anteriores**: no es el port de un `.exe` de
`sage50Apps-master` — no tiene equivalente en el repo de escritorio. La
doctrina "port fiel" (§6 de `docs/ESTADO-MIGRACION-WEB.md`) no aplica: acá se
diseña la lógica de negocio desde cero. Sí aplica todo el resto del patrón de
arquitectura (`I<Nombre>Repository`, doble registro de rutas, `OdbcParameter`
posicionales, `InvariantCulture`, componentes `Psa*`, fila en
`AppCatalogo.Todas`).

---

## 1. Qué hace el módulo

Descarga los comprobantes electrónicos **recibidos** (compras) de cada
cliente desde el portal SRI en Línea y los concilia contra:

1. Lo ya contabilizado en Sage 50 (vía el campo de autorización que Sage
   guarda al contabilizar una compra).
2. Opcionalmente, contra el ATS ya armado del mismo período (el módulo ATS
   ya cubre esta conciliación puntual; acá solo se referencia si hace
   sentido, no se duplica).

No hay API oficial de descarga masiva del SRI — solo web services SOAP
puntuales (emisión/consulta por clave de acceso, sin login). La descarga es
semi-automática vía una **extensión de Chrome**, no un scraper server-side:
las credenciales del SRI (una por cliente, sin acceso delegado multi-RUC)
nunca deben tocar el servidor.

---

## 2. Investigación: qué ya existe en el código legado

Antes de diseñar desde cero, se relevó `sage50Apps-master` buscando cualquier
pieza que ya resuelva parte de esto. Aparecieron **3 activos reales y
reutilizables** que cambian el diseño:

### 2.1 Ya existe un cliente SOAP funcional contra el SRI (sin login)

`Sage50usIntegration/Services/SriQueryAuthorizationProof.cs` — clase en
producción (consumida por `Bill.cs`/`FrmBillBatch.cs`/`TwhAtSale.cs`/
`FrmTwhAtSalesBatch.cs`) que llama por HTTP crudo (`HttpWebRequest`, POST
`text/xml`, sin WCF/`Reference.cs`) al WS
`https://cel.sri.gob.ec/comprobantes-electronicos-ws/AutorizacionComprobantesOffline?wsdl`
pasando solo la **clave de acceso de 49 dígitos** — sin ningún login — y
recibe de vuelta el **XML completo del comprobante** (extraído del tag
`<comprobante>` de la respuesta SOAP). Es exactamente el patrón que pide la
tarea (§5, salida 5): consulta cruzada por clave de acceso, sin depender del
estado que muestre el portal.

Esto es un WS **distinto** al que mencionó el usuario
(`ConsultaComprobante`/`ConsultaFactura`) — hay que investigar en F1 si
`ConsultaComprobante` es el reemplazo moderno de `AutorizacionComprobantesOffline`
o un servicio paralelo (versionado de comprobante `2.0`/`2.1`, o pensado para
terceros en vez del emisor). El **patrón de cliente** (HTTP crudo + POST XML
`text/xml`, sin depender de `ServiceReference`/WCF) se porta igual sea cual
sea el endpoint final: se prueba primero contra `celcer.sri.gob.ec` (pruebas)
con una clave de acceso real conocida antes de fijar el WSDL definitivo.

Hay además una UI ya construida sobre esto —
`Sage50usIntegration/Forms/SRIwebReadAccessKeys.cs` (diálogo: el usuario pega
un texto con claves de 49 dígitos, un regex las extrae, se consultan todas al
WS) — no se porta tal cual (no hace falta pegar texto a mano si la extensión
ya entrega las claves), pero confirma que **este flujo ya se usó en
producción y funciona**.

### 2.2 Ya existe una librería de parseo de XML de comprobantes electrónicos

`ImportXmlLib/` (`Model/ComprobanteE.cs` + `Model/FacturaE.cs`/`NotaCreditoE`/
`RetencionE` + `SRIxsd/` con las clases generadas por `xsd.exe` de los 4
esquemas oficiales: Facturas v1.0/1.1/2.0/2.1, Notas de Crédito, Notas de
Débito, Retenciones). `ComprobanteE` ya resuelve lo más delicado del parseo:

- El XML que entrega el SRI (tanto el que se descarga del portal como el que
  devuelve el WS de autorización) viene **envuelto**: un XML raíz de
  autorización (`estado`, `numeroAutorizacion`, `fechaAutorizacion`,
  `ambiente`) con el comprobante real **escapado como texto dentro de un tag
  `<comprobante>`** — `ComprobanteE`/`XmlReader.DocumentoElectronico()` ya
  hacen ese des-escape y sacan el XML interno real.
- Detecta el tipo de comprobante (factura/NC/ND/retención) por la raíz del
  XML interno.
- Extrae los campos comunes (`infoTributaria`: RUC emisor, razón social,
  establecimiento-ptoEmi-secuencial; `infoFactura`/`infoCompRetencion`/...:
  fecha emisión, identificación del comprador/sujeto retenido).

**Se porta** (a `PsaWeb.Conciliacion`, ver §4.4) el desenvolvido del sobre +
la detección de tipo + los campos comunes. Los totales (subtotal/IVA/total)
también están en `infoFactura` (`totalSinImpuestos`, `importeTotal`,
`totalConImpuestos/totalImpuesto[]/valor`) — se leen igual, no hace falta
portar las clases completas de detalle de líneas (`SRIxsd/Facturas/*`) para
el motor de conciliación: solo hacen falta cabecera + totales, no el detalle
línea por línea.

### 2.3 El campo de "autorización" en Sage NO es un campo simple — ya está portado el lector

El código legado (`ATSfromPeach/ATSModel/LoadPurchases.cs::SriAuthorization`,
ya portado 1:1 en
`src/PsaWeb.Ats/Compras/LectorAuxiliarComprasAts.cs::NumeroAutorizacionAsync`)
revela que Sage 50 **no tiene un campo dedicado** para la clave de acceso de
una compra. Se resuelve con esta prioridad:

1. Un **ítem pseudo-línea `AUT-SRI`** en el detalle de la transacción
   (`JrnlRow`/`LineItem`): la clave se escribe en `JrnlRow.RowDescription` de
   esa línea. **Este es el campo real de "autorización" — no se autocompleta
   solo, lo teclea quien contabiliza la compra.**
2. Si la compra está vinculada a una Orden de Compra (`LinkToAnotherTrx`), se
   usa el AUT-SRI **de la OC**, no el del comprobante — con prioridad sobre
   el punto 1.
3. Respaldo: `JrnlHdr.ShipToAddress1` (solo si `JournalEx` = factura de
   compra).

**Implicación directa para el diseño del motor** (ver §4.4 y §7): al ser un
campo de **captura manual**, la tasa de éxito del cruce por clave de acceso
depende de la disciplina de quien contabiliza — exactamente el tipo de error
que el módulo busca atrapar (salida 3 de la tarea). Es esperable que una
fracción no trivial de compras reales tenga el campo vacío o mal tecleado
(clave incompleta, con espacios, truncada) — esas caen automáticamente en la
salida 2 ("Solo en Sage → revisar validez") aunque el comprobante sí exista
y sí esté bien contabilizado en cuanto a valores. **No se debe interpretar
"Solo en Sage" como sinónimo de "comprobante inválido"** — hay que decírselo
así al usuario en la UI (ver §7, textos de la pantalla).

`LectorAuxiliarComprasAts.NumeroAutorizacionAsync` y
`LectorComprasAts.LeerAsync` (que ya arma proveedor + clasificación de tipo +
totales por período para el diario de compras) **se reusan tal cual** como
base del Set B (§4.4) — no se duplica acceso a Sage.

---

## 3. Arquitectura general (2 componentes)

```
[Contadora] --login manual--> [Portal SRI en Línea]
     |                              |
     | (extensión Chrome activa    | (detecta login, navega,
     |  tras detectar login)       |  filtra fechas, descarga XML)
     v                              v
[Extensión] --POST XML + RUC-->  [Host: endpoint de subida]
                                       |
                                       v
                          [Staging: ComprobanteSriDescargado]
                          (PsaWebPlataforma, no PeachEBills — ver §4.3)
                                       |
                                       v
                    [PsaWeb.Conciliacion: motor de conciliación]
                     Set A (staging) x Set B (Sage, vía OdbcSage50)
                     + cruce puntual WS SOAP (clave por clave, bajo demanda)
                                       |
                                       v
                        [/conciliacion-sri — página del módulo]
```

---

## 4. Diseño por pieza

### 4.1 Componente 1 — Extensión de Chrome

- **Manifest V3**, service worker (sin background page persistente).
- **Distribución interna**: unpacked/sideload vía política de grupo o
  instalación manual guiada — no Chrome Web Store (evita revisión pública y
  el ciclo de aprobación de Google para una herramienta interna de 1-2
  usuarias). Si más adelante se necesita auto-actualización sin tocar cada
  máquina, evaluar publicarla como **no listada** en la Web Store (sigue sin
  ser pública, pero sí auto-actualiza) — no bloquea el diseño, es una
  decisión de F5.
- **Content script** inyectado en el dominio de SRI en Línea
  (`srienlinea.sri.gob.ec`, a confirmar el host exacto en F1). Detecta login
  exitoso por un elemento del DOM post-auth (ej. el menú de usuario/RUC
  activo) — **no** por la URL sola (evita falsos positivos en pantallas de
  error que reusan la misma ruta).
- **Cómo navegar/descargar** — dos estrategias, a decidir en F1 con acceso
  real al portal (no se puede login con credenciales de un cliente desde
  esta sesión de investigación):
  1. **DOM automation clásica**: simular clicks en "Comprobantes recibidos" →
     filtro de fechas → "Descargar XML" por fila. Más frágil (cualquier
     cambio de maquetación rompe selectores), pero no depende de entender
     una API interna.
  2. **Interceptar/repetir las llamadas internas de la SPA**: SRI en Línea es
     una aplicación con backend propio (probablemente REST/JSON) detrás de
     la UI; si el content script puede identificar esas llamadas (leyendo
     `chrome.webRequest`/`Network` o replicando el `fetch` con las cookies
     de sesión ya presentes en la pestaña), se obtiene el listado de
     comprobantes recibidos sin parsear HTML — más robusto a cambios de
     estilo, pero requiere inspeccionar el tráfico real una vez (F1).
  - **Recomendación**: probar 2 primero (menos frágil a largo plazo); si el
    backend interno no es identificable o cambia por sesión (tokens
    anti-CSRF por request), caer a 1. Documentar la elección con capturas de
    red reales en el propio código de la extensión (comentario con fecha),
    porque **esto se va a romper sin aviso** — no es "constrúyelo y
    olvídate": dejar un log/contador de errores visible a la usuaria
    (mensaje "el portal cambió, avisar a soporte") en vez de fallar en
    silencio.
- **Filtro de fechas**: rango configurable en la UI de la extensión (popup),
  default = mes en curso.
- **Subida**: batch de XML (probablemente decenas por corrida) al endpoint
  del Host — ver §4.2. Reintentos simples ante error de red; nunca reintenta
  el login ni guarda la clave del SRI en ningún lado (ni `localStorage` de la
  extensión, ni el servidor).
- **Nunca** hace `Process.Start` ni descarga a disco del usuario — los XML
  van directo del portal al POST.

### 4.2 Endpoint de subida en el Host

**Decisión de diseño clave**: la extensión **no** puede autenticarse con la
cookie de sesión del shell (`PsaWeb.Auth`) de forma confiable — es un
`fetch` desde contexto de extensión (`chrome-extension://...`), no una
navegación de la misma pestaña donde la contadora esté logueada al sitio
PSA; depender de que la cookie del sitio esté presente en ese momento en el
navegador es frágil (perfiles distintos, sesión expirada, SameSite).

**Propuesta: token de API propio**, no cookie:

- Nueva pantalla `/mi-cuenta/extension` (junto a `/mi-cuenta/seguridad`, ya
  existe el patrón de "área de mi cuenta" con `GestorSegundoFactor`): genera
  un token largo aleatorio, lo muestra **una sola vez**, guarda solo su hash
  (mismo patrón que un `PasswordHasher` de Identity) en una tabla nueva de
  `PsaWebPlataforma` (`TokensExtension`: `UsuarioId`, `HashToken`, `Creado`,
  `UltimoUso`, revocable). La contadora lo pega una vez en el popup de
  configuración de la extensión (`chrome.storage.local`, no sincronizado).
- Endpoint `POST /conciliacion-sri/api/comprobantes` en `Program.cs`:
  - Header `Authorization: Bearer <token>` — **no** `RequireAuthorization()`
    (esa es la policy de cookie/Identity); se valida el token a mano contra
    el hash guardado, se resuelve el `UsuarioId`, y se usa
    `ISecurityDirectory.EmpresasDelUsuarioAsync(usuario)` para confirmar que
    el RUC del payload es uno de los que ese usuario puede ver — mismo
    control de acceso que ya usan las páginas, reutilizado en vez de
    inventar uno nuevo.
  - Body: `{ ruc, xmls: [ { nombreArchivo, contenidoBase64 } ] }` (JSON;
    tamaño de un XML de comprobante es chico, un batch de 100 en base64 es
    manejable sin multipart).
  - Por cada XML: parsear con el lector portado de `ComprobanteE` (§2.2,
    §4.4) → si la clave de acceso ya existe para ese RUC+período, **upsert**
    (idempotente — la extensión puede volver a mandar el mismo rango de
    fechas sin duplicar); si el XML no parsea, se cuenta como error pero no
    aborta el batch.
  - Respuesta: `{ total, nuevos, actualizados, errores: [{archivo, motivo}] }`
    — la extensión puede mostrarle un resumen a la usuaria.
  - **Este es el primer endpoint del Host que NO usa la cookie de Identity**
    — es una pieza nueva de infraestructura (una policy de autenticación por
    Bearer token, además de la de cookie ya existente), a construir con
    cuidado y probar con tests de integración (token válido/inválido/
    revocado/RUC no autorizado). Vale la pena aislarla en
    `PsaWeb.Identidad` (o un `PsaWeb.Identidad.TokensApi` chico) para no
    ensuciar `Program.cs` con lógica de validación de token inline.

### 4.3 Dónde se guardan los XML descargados

**Decisión: `PsaWebPlataforma`, no `PeachEBills`.** `PeachEBills` es la base
compartida que ya usan los `.exe` de escritorio y otras herramientas del
área — no es de PSA-web, y hasta ahora solo se le agregan filas a tablas que
**ya existían** (nunca se creó una tabla nueva ahí). El staging de XML del
SRI es un dato **enteramente nuevo**, sin ningún consumidor fuera de este
módulo, así que va a la base que sí es propia y ya tiene el pipeline de
migraciones EF Core andando (`PsaWebPlataforma`, creada en F-Shell-0 para
Identity).

Tablas nuevas propuestas (vía migración EF, en un `DbContext` nuevo o
extendiendo el de Identidad — a decidir en F1, probablemente un
`ConciliacionDbContext` propio para no mezclar con el esquema de Identity):

- `ComprobantesSriDescargados`: `Id`, `Ruc` (empresa/cliente), `ClaveAcceso`
  (49 dígitos, índice único por `Ruc+ClaveAcceso`), `TipoComprobante`,
  `RucEmisor`, `RazonSocialEmisor`, `FechaEmision`, `Subtotal`, `Iva`,
  `Total`, `Estado` (tal como lo trae el XML/portal — **nunca** se trata como
  fuente de verdad, ver salida 5), `XmlOriginal` (guardar el XML completo,
  no solo los campos extraídos — hace falta para exportar/depurar y para la
  consulta SOAP de verificación), `FechaDescarga`, `SubidoPor` (usuario del
  token).
- `TokensExtension` (§4.2).
- Posible `CorridasConciliacion` (histórico de cuándo se corrió la
  conciliación de qué período/empresa y su resumen) — **backlog**, no
  bloquea F1-F4; el motor puede correr on-demand desde la página sin
  persistir resultados al principio.

### 4.4 Componente 2 — Motor de conciliación

**Estructura de proyecto: sí, `PsaWeb.Conciliacion` como librería pura**,
mismo patrón que `PsaWeb.Ats` (que tampoco es 100% libre de ODBC — tiene sus
propios lectores dentro del proyecto, ver `LectorComprasAts`; lo "puro" es
la lógica de armado/clasificación, testeable con datos en memoria). Para
Conciliación SRI:

- **Pura, sin ODBC/EF**: el motor de clasificación en sí (`MotorConciliacion`
  — el full outer join + las 5 reglas de salida), y el parseo de XML
  (`LectorXmlComprobanteSri`, port de `ComprobanteE`+`XmlReader` de
  `ImportXmlLib`). Ambos toman/devuelven DTOs, 100% testeables con XML de
  muestra y listas en memoria.
- **Con ODBC** (dentro del mismo proyecto, igual que `PsaWeb.Ats/Compras/`):
  un lector de Set B que envuelve/reusa `LectorComprasAts.LeerAsync` +
  `LectorAuxiliarComprasAts.NumeroAutorizacionAsync` de `PsaWeb.Ats` — **o**,
  si se prefiere no acoplar `PsaWeb.Conciliacion` a `PsaWeb.Ats` (son
  dominios conceptualmente distintos: uno arma un XML tributario, el otro
  concilia), extraer las piezas de lectura pura de compras+autorización que
  hoy viven en `PsaWeb.Ats/Compras/` hacia `PsaWeb.Comprobantes` (que ya es
  el lugar de piezas compartidas entre módulos de comprobantes) y que ambos
  (`PsaWeb.Ats` y `PsaWeb.Conciliacion`) referencien desde ahí. **Recomendado
  esto último** — evita que un módulo de conciliación dependa del módulo de
  declaración de impuestos solo para leer compras, y dado que el usuario ya
  pidió explícitamente "reusar los lectores/repositorios que ya existen...
  en vez de crear acceso a Sage duplicado", mover las piezas de lectura al
  lugar compartido es más consistente con esa instrucción que encadenar
  dependencias entre módulos de negocio.
- **Con EF** (staging): un lector de Set A sobre `ComprobantesSriDescargados`
  — vive en `PsaWeb.Conciliacion` o en un pequeño `PsaWeb.Conciliacion.Datos`
  si se quiere separar del todo la lógica pura del acceso a la BD propia (a
  decidir según cuánto crezca; para el tamaño de este módulo probablemente
  no hace falta el split).

**Algoritmo** (tal como lo describe la tarea, sin cambios): full outer join
por clave de acceso entre Set A (SRI, del staging) y Set B (Sage, del
lector de compras), 5 salidas:

1. Solo en SRI → pendientes de registrar.
2. Solo en Sage → revisar validez (incluye el caso "clave vacía/mal
   tecleada" de §2.3 — texto de UI debe distinguirlo de "SRI no lo tiene").
3. En ambos, valores distintos → delta campo a campo (subtotal/IVA/total).
4. En ambos, valores iguales pero otros datos distintos (fecha, RUC emisor,
   tipo) → error de captura sin impacto en saldo.
5. Anulados en SRI pero no en Sage → **nunca** confiar en el `Estado` que
   trae el XML/portal por sí solo para esta salida; para las compras que
   caen en 3, 4 o 5, hacer una consulta puntual al WS SOAP oficial
   (§2.1/§4.5) por clave de acceso como confirmación cruzada antes de
   mostrarlas como discrepancia — igual que pide la tarea. No se consulta el
   WS para *todas* las compras del período (sería lento e innecesario);
   solo para las que el join ya marcó como sospechosas.

**Tolerancias**: comparar montos con una tolerancia de redondeo (ej.
±$0.01) antes de marcar salida 3 — a definir el umbral exacto con el usuario
al implementar (no bloquea el diseño).

### 4.5 Cliente SOAP de verificación puntual

Port del patrón de `SriQueryAuthorizationProof.CallSriWS` (§2.1): HTTP crudo,
POST `text/xml`, sin cliente WCF generado (más simple de mantener y de
testear con `HttpClient`/`IHttpClientFactory` que un `ServiceReference`).
Vive en `PsaWeb.Conciliacion` (o `PsaWeb.Comprobantes/Sri/` si se decide que
es un helper compartido, ej. reutilizable a futuro por otros módulos que
necesiten verificar un comprobante puntual).

**A confirmar en F1**: si el endpoint correcto es `ConsultaComprobante`
(el que dio el usuario) o `AutorizacionComprobantesOffline` (el que ya
funciona en el código legado) — probar ambos contra `celcer.sri.gob.ec`
(pruebas) con una clave de acceso real conocida (ej. de la fixture CPTDC) y
quedarse con el que responda. Es razonable que ambos funcionen igual (el SRI
suele tener varios WS que devuelven el mismo comprobante por distintos
casos de uso) — si es así, preferir el que el usuario ya identificó
(`ConsultaComprobante`) porque viene con URLs de pruebas *y* producción
confirmadas; `AutorizacionComprobantesOffline` en el código legado solo
tiene la de producción.

### 4.6 Cruce con ATS

El ATS ya hace su propia conciliación interna al armar el XML (usa
`LectorComprasAts`/`LectorAuxiliarComprasAts`, los mismos lectores de base
de este módulo). No se duplica lógica: si en el futuro se quiere mostrar
"esta discrepancia de Conciliación SRI también aparece/no aparece en el ATS
ya declarado de este período", es un cruce de **presentación** (leer el XML
del ATS ya generado, si existe, y anotar coincidencias) — se deja como
mejora de fase posterior (backlog), no bloquea F1-F4.

---

## 5. Categoría en el menú

Dos candidatas razonables:

- **Comprobantes Electrónicos** — el objeto central de la pantalla son
  comprobantes electrónicos (mismo tipo de dato que Facturas/NC/
  Liquidaciones/Retenciones).
- **Impuestos** — hoy solo tiene ATS; el propósito del módulo es
  cumplimiento tributario (evitar diferencias antes de declarar), y cruza
  directamente con el ATS.

**Recomendación: Impuestos.** Las 4 entradas actuales de "Comprobantes
Electrónicos" son todas de **emisión** (generan y envían algo a Datil); este
módulo no emite nada, es de solo lectura/auditoría, y su razón de ser es
alimentar la calidad de la declaración de impuestos — más cerca en propósito
de ATS que de Facturas/Retenciones. Queda a decisión del usuario si prefiere
agruparlo por *tipo de dato* en vez de por *propósito*.

---

## 6. Gate del módulo

**Sí, `GateProvisional`** — mismo patrón que nació con `quKardex`/
`qupurchliq`/`quats`: entrada en `AppCatalogo.Todas` con lista de permisos
vacía (visible para cualquier empresa) hasta que el área cargue el código
real en `allowAction`/`adrAllowRol`. Nuevo código propuesto en
`Permisos.cs`: `VerConciliacionSri = "quconcsri"` (sigue la convención
`qu<algo corto>` de los códigos existentes — `quats`, `quKardex`).

No hace falta permiso de "ejecutar" separado (como sí tiene Retenciones con
`mkTwhBatch` para el lote): este módulo es enteramente de lectura/consulta,
no escribe nada en Sage ni emite nada a Datil.

---

## 7. Página y UX (borrador, se ajusta en F4)

- `/conciliacion-sri`, acotada a empresa+ambiente de sesión (reacciona a
  `EmpresaActual.Cambio`, igual que el resto de módulos).
- Filtro de período (Desde/Hasta, como Kardex/Cierre de Caja).
- 5 secciones/pestañas, una por salida de la clasificación, cada una con su
  propio contador en el título (ej. "Solo en Sage (12)"). La de "Solo en
  Sage" lleva una nota visible aclarando que puede ser por clave no
  digitada, no solo por comprobante inválido (§2.3).
- Botón "Verificar con el SRI" en las filas de las salidas 3/4/5 → dispara
  la consulta puntual al WS (§4.5) bajo demanda, no automático para toda la
  lista (evita pegarle al WS del SRI con cientos de llamadas en cada carga
  de pantalla).
- Sin export a Excel en el primer corte (a diferencia de los demás módulos)
  — es una pantalla de trabajo/triage, no un reporte para imprimir; se
  agrega si el usuario lo pide después de probarla.
- Sin acción de "marcar como resuelto" en el primer corte — es de solo
  lectura; si hace falta llevar un registro de qué discrepancias ya se
  revisaron, es una mejora de fase posterior (tabla `CorridasConciliacion`
  de §4.3 serviría de base).

---

## 8. Fases propuestas

- **F0 — investigación de portal + WS** (con acceso real a un cliente SRI en
  Línea, no se puede hacer desde esta sesión): confirmar host exacto del
  portal, mecanismo de listado de comprobantes recibidos (DOM vs API interna,
  §4.1), y cuál WS SOAP responde (`ConsultaComprobante` vs
  `AutorizacionComprobantesOffline`, §4.5) contra pruebas y producción con
  una clave de acceso real.
- **F1 — extensión mínima + endpoint de subida**: manifest v3 + detección de
  login + descarga de un rango de fechas fijo (sin UI pulida) → POST al
  endpoint nuevo con token de API. `PsaWeb.Identidad`: generación/validación
  de token, pantalla `/mi-cuenta/extension`. `ConciliacionDbContext` +
  migración (`ComprobantesSriDescargados`, `TokensExtension`).
- **F2 — parseo + Set A**: port de `ComprobanteE`/`XmlReader` (desenvolvido +
  campos comunes + totales) a `PsaWeb.Conciliacion`. Lector de Set A sobre
  el staging. Tests con XML de muestra (factura, NC, retención — ver
  fixture §9).
- **F3 — Set B + motor**: extraer/reusar los lectores de compras+autorización
  de `PsaWeb.Ats` (mover a `PsaWeb.Comprobantes` si se confirma esa
  dirección, §4.4). `MotorConciliacion` con las 5 clasificaciones + tests
  (casos: match exacto, solo SRI, solo Sage, delta de valores, delta de
  metadata, con tolerancia de redondeo).
- **F4 — cliente SOAP de verificación + página**: port de
  `SriQueryAuthorizationProof.CallSriWS` (§4.5). Módulo
  `PsaWeb.Modules.ConciliacionSri`, página `/conciliacion-sri` (§7), gate
  `quconcsri` (§6), fila en `AppCatalogo.Todas`, doble registro de rutas.
- **F5 — pulido de la extensión + deploy**: manejo de errores visible en la
  extensión (§4.1), distribución interna, empaquetado; deploy del módulo
  siguiendo el patrón delta de `docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md`
  (redeploy del mismo sitio IIS, sin BD/IIS nuevos salvo la migración de
  `PsaWebPlataforma`).

Estimado grueso: 10-15 días de dev, dominado por F0 (depende de acceso real
al portal, no de código) y F1 (primera pieza de infraestructura nueva —
token de API — que no tiene precedente en el repo).

---

## 9. Fixture de prueba

Reusar **CPTDC (RUC `1792051800001`)** — ya validada para Kardex y ATS,
`lparedes` ya tiene acceso, Sage local disponible en PREDATOR. Falta del
área: acceso al portal SRI en Línea de CPTDC (usuario/clave) para probar la
extensión de punta a punta en F0, y confirmar un período con comprobantes
recibidos variados (al menos un caso de cada salida: alguno bien
contabilizado, alguno pendiente, alguno con clave mal tecleada a propósito
para probar la salida 2, y si es posible algún anulado real).

---

## 10. Decisiones a confirmar con el usuario

1. **Categoría de menú**: Impuestos (recomendado, §5) vs. Comprobantes
   Electrónicos.
2. **Token de API vs. otra forma de autenticar la extensión** (§4.2) — es la
   pieza de infraestructura más nueva de este plan (primer endpoint no-cookie
   del Host). Alternativas descartadas y por qué: cookie de sesión
   compartida (frágil, cross-context), Windows Auth (la extensión no corre
   en el dominio), certificado cliente (sobre-ingeniería para 1-2 usuarias).
3. **Dónde vive el staging**: `PsaWebPlataforma` (recomendado, §4.3) —
   confirmar que no hay objeción a crear un `DbContext`/migración nueva ahí
   dedicado a este módulo.
4. **`PsaWeb.Conciliacion` referenciando `PsaWeb.Ats` vs. extraer los
   lectores de compras a `PsaWeb.Comprobantes`** (§4.4) — recomendado lo
   segundo, pero implica tocar `PsaWeb.Ats` (mover archivos, no lógica) para
   un módulo que todavía no existe; confirmar que vale la pena el reordene
   ahora en vez de cuando se implemente F3.
5. **Estrategia de descarga de la extensión**: DOM automation vs. interceptar
   la API interna del portal (§4.1) — no se puede decidir sin acceso real al
   portal; queda como primer paso de F0, no una decisión de escritorio.
6. **Endpoint SOAP definitivo**: `ConsultaComprobante` vs.
   `AutorizacionComprobantesOffline` (§4.5) — mismo caso, se resuelve
   probando en F0.
7. **Alcance del primer corte**: ¿el motor corre on-demand desde la página
   (sin persistir resultados, más simple) o se guarda un histórico de
   corridas (`CorridasConciliacion`, §4.3) desde el día 1? Recomendado
   on-demand primero — agregar histórico es un cambio aditivo, no rompe
   nada si se hace después.

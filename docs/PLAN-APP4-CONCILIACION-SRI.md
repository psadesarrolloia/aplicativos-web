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

### 2.4 El portal SRI ya tiene un export masivo — no hace falta bajar XML uno por uno

Cierre de F0 (2026-09-15), investigado **sin necesitar login real** — vía
búsqueda pública y triangulando con el código legado (§2.1): la pantalla
"Comprobantes recibidos" del portal (`srienlinea.sri.gob.ec`, ruta interna
`/sri-en-linea/consulta/55` — host confirmado navegando al portal público) no
obliga a descargar el XML comprobante por comprobante. Tiene un botón
**"Descargar reporte"** que exporta, de una sola vez, el listado completo de
comprobantes recibidos del filtro (fecha + tipo) como un **archivo de texto
tabulado**. Dos fuentes independientes lo confirman:

- Terceros que ya resuelven este mismo problema para contadores en Ecuador
  (`abcfacturas.com/sri-downloader`, `downloader.docu.ec`, `zettana.com`,
  `factuplan.com.ec`) — existe todo un mercado de herramientas que automatizan
  justo esto, lo que confirma que la descarga masiva NO es nativa del portal
  **para el XML completo**, pero sí para el listado/reporte.
- **Dátil** (la misma plataforma que ya usa este repo para emitir facturas y
  retenciones — `PsaWeb.Datil`) tiene documentado el flujo idéntico: el
  usuario entra a `srienlinea.sri.gob.ec` con sus propias credenciales, filtra
  fecha + tipo "Todos", click en **"Descargar reporte"**, y ese archivo se
  importa después a Dátil vía "Comprobantes recibidos" → "Importar archivo
  SRI". Dátil aclara que el listado del portal solo cubre **los últimos 5
  días de forma confiable** para meses corrientes — para períodos viejos hay
  que pedirlo aparte. Dátil no tiene una API propia para esto (no es un atajo
  de integración disponible, solo confirma el mismo flujo manual).
- El formato de ese reporte **coincide exactamente** con lo que ya parsea
  `SriQueryAuthorizationProof(string pathFile)` en el código legado (§2.1):
  texto tabulado (`\t`) con las 10 columnas oficiales `COMPROBANTE`,
  `SERIE_COMPROBANTE`, `RUC_EMISOR`, `RAZON_SOCIAL_EMISOR`, `FECHA_EMISION`,
  `FECHA_AUTORIZACION`, `TIPO_EMISION`, `IDENTIFICACION_RECEPTOR`,
  `CLAVE_ACCESO`, `NUMERO_AUTORIZACION` — con la particularidad (a confirmar
  contra un archivo real, es un detalle de parseo, no de diseño) de que el
  export legado trae **2 líneas por comprobante** (el `.exe` se queda con las
  pares). Esto no es una suposición sin base: es el mismo archivo que ya se
  usó en producción para alimentar `SRIwebReadAccessKeys`.

**Esto cambia el diseño de la extensión para mejor** (ver §4.1 reescrito):
en vez de automatizar N descargas de XML individuales (una por fila, la parte
más frágil de cualquier automatización de portal — es literalmente el
problema que varias empresas cobran por resolver), la extensión solo necesita
automatizar **una acción**: click en "Descargar reporte" y subir ese único
archivo. El reporte ya trae la `CLAVE_ACCESO` de cada comprobante — con eso,
el **servidor** (no la extensión, no el portal) hidrata cada fila con su XML
completo llamando al WS SOAP `AutorizacionComprobantesOffline` (§4.5) por
clave de acceso, uno por uno, sin depender más del navegador de la contadora.
Es una automatización muchísimo más chica y con muchísima menos superficie
de rotura.

### 2.5 F0 CERRADA DEL TODO (2026-09-17) — sesión real de CPTDC contra el portal

El usuario trajo capturas + 4 archivos `.har` + el archivo real del reporte
descargado, con una sesión real de CPTDC (RUC `1792051800001`) en
`srienlinea.sri.gob.ec`. Esto confirma (y en 2 puntos corrige) todo lo que
§2.4 daba por triangulado con fuentes externas:

**El reporte real trae más de lo esperado — cambia el diseño de la
hidratación (§4.4).** Formato real (2026), **1 línea por comprobante con
encabezado** (no 2 líneas por fila como el formato legado de ~2018), 12
columnas tab-separadas:

```
RUC_EMISOR  RAZON_SOCIAL_EMISOR  TIPO_COMPROBANTE  SERIE_COMPROBANTE
CLAVE_ACCESO  FECHA_AUTORIZACION  FECHA_EMISION  IDENTIFICACION_RECEPTOR
VALOR_SIN_IMPUESTOS  IVA  IMPORTE_TOTAL  NUMERO_DOCUMENTO_MODIFICADO
```

**El reporte YA trae `VALOR_SIN_IMPUESTOS`/`IVA`/`IMPORTE_TOTAL` por fila.**
Esto es más de lo que daba el formato legado (que solo traía metadata, sin
montos) y **elimina la necesidad de hidratar Set A vía WS SOAP solo para
tener los montos** — el paso de "hidratación" de §4.4 deja de ser
obligatorio en el alta: las salidas 1-4 del motor (existencia + comparación
de valores) se resuelven **directo con lo que trae el reporte**, sin ninguna
llamada a `AutorizacionComprobantesOffline`. El WS SOAP (ambos, §4.5) queda
reservado para lo que el reporte NO trae: **no tiene columna de estado/
anulado** — sigue haciendo falta `ConsultaComprobante` para la salida 5 y el
botón "Verificar con el SRI", pero ya no como paso obligatorio de cada fila
nueva, sino bajo demanda (como ya estaba planteado para las salidas
sospechosas). `NUMERO_DOCUMENTO_MODIFICADO` viene de regalo — sirve directo
para vincular una NC con la factura que modifica, sin tener que traer el XML
completo para eso.

**Encoding real: ISO-8859-1 (Latin-1), no UTF-8** — confirmado byte a byte
(`Ñ` → `0xD1`). Si el parser lee el archivo como UTF-8, nombres como
"COMPAÑIA"/"CORPORACIÓN"/"PEÑAHERRERA" quedan corrompidos de forma
irreversible (`COMPA�IA`). El endpoint de subida (§4.2) tiene que decodificar
explícitamente con esa codificación — detalle de implementación fácil de
pasar por alto sin este archivo real.

**El filtro "Tipo de comprobante" no tenía "Todos" tildado** en la corrida
que trajo el usuario (quedó en "Factura") — el reporte de prueba solo trae
facturas. Para cubrir notas de crédito/retenciones/liquidaciones también
hace falta correr con "Todos" (si el portal lo soporta en un solo reporte) o
repetir la descarga por tipo — a verificar en F1 al automatizar el filtro,
no cambia el diseño.

**Ruta real confirmada** (distinta a la que había encontrado por búsqueda
pública en §2.4 — bien haber marcado esa como "a confirmar"):
`https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/recibidos/comprobantesRecibidos.jsf`
(no `/sri-en-linea/consulta/55`). Es una pantalla **JSF/PrimeFaces
server-rendered clásica** (formularios con postback, `javax.faces.ViewState`,
componentes `frmPrincipal:...`) — **no** una SPA con API REST propia como se
especulaba en §4.1; la opción "interceptar la API interna" de la primera
versión del plan no aplicaba a esta pantalla en particular (sí es correcta
la descripción de SPA para el shell exterior del portal, ver login abajo).
El campo RUC/Cédula ya viene **pre-llenado con la empresa de la sesión
activa** — reduce el riesgo de que la extensión descargue por error el
reporte de otra empresa.

**Mecanismo de descarga confirmado — mejor que "simular un click".** La
descarga es un **POST de formulario normal** (no AJAX parcial de PrimeFaces)
a la misma URL de la pantalla, que responde directo con el archivo
(`Content-Type: text/plain`, `Content-Disposition: attachment`) — el
navegador lo entrega como una descarga común (así se vio: el gestor de
descargas de Chrome mostrando `1792051800001_Recibidos.txt`). Para la
extensión, esto habilita una opción más limpia que automatizar un click y
leer el archivo descargado del disco: **replicar el mismo POST con
`fetch(..., {credentials:'include'})`** desde el content script (mismo
origen, así que las cookies de sesión aplican solas), leyendo primero del
DOM el valor del campo oculto `javax.faces.ViewState` (obligatorio en
cualquier postback JSF) — y leer el **cuerpo de la respuesta directo en
memoria** (`await response.text()`), sin pasar nunca por la carpeta de
Descargas ni por la API `chrome.downloads`. El nombre exacto del botón/
parámetro de "Descargar reporte" queda para cuando se escriba el content
script en F1 (es autoinspección del formulario real, no una decisión de
arquitectura); si por algún motivo el POST replicado no alcanza (ej. el
componente exige un evento de click real de PrimeFaces), cae a la opción B:
simular el click y leer el archivo vía `chrome.downloads`.

**Login: confirmado que el portal usa Keycloak/OIDC real** (`/auth/realms/
Internet/...`, `client_id=app-sri-claves-angular`,
`protocol/openid-connect/token`) para el shell exterior (ese sí es una SPA
Angular con API REST propia — `sri-menu-portal-servicio-internet`,
`sri-catastro-sujeto-servicio-internet`, etc.). Señal de login exitoso
**mucho más simple y robusta** que "un elemento del DOM": la navegación
llega a `https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil?faces-redirect=true`
(la pantalla de perfil con el RUC y la razón social, confirmada en la
primera captura). La pantalla de "Comprobantes electrónicos recibidos" (JSF,
arriba) es un módulo legado aparte, puenteado por un token de un solo uso
que el propio shell le pasa por query string (`...comprobantesRecibidos.jsf?token=...`)
al navegar desde el menú — la extensión debe **navegar por el link del menú
del shell** (no construir la URL del JSF directo) para que ese puente de
sesión ocurra solo, como le pasa a cualquier usuaria real.

**F0 queda 100% cerrada.** No queda ningún punto de investigación abierto
para arrancar F1 — lo que sigue (nombre exacto del botón/campos del
formulario JSF) es autoinspección al escribir el content script, no
investigación de diseño.

---

## 3. Arquitectura general (2 componentes)

```
[Contadora] --login + navega/filtra a mano--> [Portal SRI en Línea]
     |                                              |
     | (extensión: botón "Subir a PSA"             | (pantalla de
     |  activo cuando detecta la pantalla          |  comprobantes
     |  de resultados ya cargada — §11.1)          |  recibidos ya
     |                                              |  consultada)
     v                                              v
[Extensión] --replica el POST de "Descargar reporte" vía fetch (§11.2)--
     |
     v
[Extensión] --POST reporte(texto)+RUC--> [Host: endpoint de subida]
                                       |
                                       v
                     [Staging: ComprobantesSriDescargados]
                     (fila por CLAVE_ACCESO — el reporte YA trae tipo,
                      proveedor, fechas y montos, §2.5; se guarda tal
                      cual — PsaWebPlataforma, no PeachEBills, §4.3)
                                       |
                                       v
                    [PsaWeb.Conciliacion: motor de conciliación]
                     Set A (staging, ya completo) x Set B (Sage, vía
                     OdbcSage50) — 4 de las 5 salidas se resuelven acá
                     directo, sin tocar el SRI de nuevo
                                       |
                    +------------------+------------------+
                    v                                      v
   [VerificacionEstadoSriWorker — automático,   [Botón individual/masivo
    cross-company, cada N horas, §13.4]          "Verificar (con el SRI)",
                    |                             bajo demanda, §7/§13.4]
                    v                                      v
              [WS SOAP ConsultaComprobante por CLAVE_ACCESO — §4.5]
                                       |
                                       v
                        [/conciliacion-sri — página del módulo]
```

La extensión ya no descarga ni sube XML individuales, ni navega sola por el
portal (§11.1) — solo replica el POST de **un archivo de reporte por
corrida** (§2.4/§2.5) que ya trae los montos. El WS SOAP ya no es un paso
obligatorio del alta de cada comprobante — queda reservado para lo único
que el reporte no trae (estado/anulado), y esa verificación ya no depende
solo de que alguien la pida a mano: corre sola vía el worker (§13.4),
complementada por los botones para el caso urgente.

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
- **Todo confirmado con sesión real en F0** (§2.5, 2026-09-17) — ya no queda
  nada de esto por investigar, solo por escribir:
  - Host: `srienlinea.sri.gob.ec`. Login vía Keycloak/OIDC real (shell
    Angular). Señal de login exitoso: navegación a
    `/sri-en-linea/contribuyente/perfil?faces-redirect=true` — más simple y
    robusta que "esperar un elemento del DOM" (era la suposición de la
    primera versión de este plan).
  - Pantalla de "Comprobantes electrónicos recibidos":
    `/comprobantes-electronicos-internet/pages/consultas/recibidos/comprobantesRecibidos.jsf`
    — es JSF/PrimeFaces server-rendered clásico (formularios con postback),
    **no** una SPA con API propia como se especulaba; la opción de
    "interceptar la API interna" de la primera versión del plan no aplicaba
    acá. Se llega a esta pantalla navegando por el link del menú del shell
    (el shell le pasa un token de sesión de un solo uso por query string) —
    **decidido con el usuario (2026-09-17): la extensión NO automatiza este
    paso** (§11.1). La contadora navega y filtra a mano, como siempre; la
    extensión solo reemplaza el paso final de "Descargar reporte". Difiere
    de la descripción original de la tarea ("la extensión... navega a
    comprobantes recibidos, filtra por rango de fechas") a propósito: menos
    superficie de automatización sobre el portal, que es justo el punto que
    la tarea ya marcaba como "necesita mantenimiento recurrente".
  - El campo RUC/Cédula del formulario **ya viene pre-llenado** con la
    empresa de la sesión activa — reduce el riesgo de bajar por error el
    reporte de otra empresa.
  - La descarga es un **POST de formulario normal** (no AJAX parcial) que
    devuelve el archivo directo (`Content-Type: text/plain`,
    `Content-Disposition: attachment`). Diseño recomendado para la
    extensión, más limpio que simular un click y leer un archivo del disco:
    **replicar ese mismo POST vía `fetch(..., {credentials:'include'})`**
    desde el content script (mismo origen → las cookies de sesión aplican
    solas), leyendo antes del DOM el campo oculto `javax.faces.ViewState`
    (obligatorio en cualquier postback JSF), y tomar el cuerpo de la
    respuesta directo en memoria (`await response.text()`) — sin tocar
    nunca la carpeta de Descargas ni la API `chrome.downloads`. Si el
    componente exigiera un evento de click real (no siempre alcanza con
    replicar el POST en apps PrimeFaces), cae a simular el click + leer el
    archivo descargado como plan B.
  - Formato del reporte descargado, confirmado con un archivo real de CPTDC
    (agosto/2026, 444 facturas): **1 línea por comprobante con encabezado**
    (no 2 líneas por fila como asumía el código de 2018), 12 columnas —
    tipo/proveedor/fechas/clave/**montos** ya incluidos (§2.5, cambia el
    diseño de §4.4: ya no hace falta hidratar por WS para tener los montos).
    **Encoding real: ISO-8859-1**, no UTF-8 — hay que decodificar así en el
    parser o los nombres con Ñ/tildes quedan corrompidos.
- **Límite de 5 días conocido** (dato de la documentación de Dátil, §2.4):
  el listado de "recibidos" del mes corriente en el portal solo se actualiza
  de forma confiable para los últimos 5 días — para conciliar meses ya
  cerrados hay que correr la extensión durante ese mes (no sirve esperar al
  cierre para bajar todo junto). Esto es una restricción operativa a
  comunicarle a la usuaria (correr la extensión seguido, no una vez al mes),
  no algo que el código pueda resolver.
- **Filtro de tipo de comprobante y de fechas: los elige la contadora en el
  portal, no la extensión** (§11.1) — el reporte de prueba se bajó con el
  filtro en "Factura" únicamente (no "Todos"); para cubrir NC/retenciones/
  liquidaciones hace falta que ella corra con "Todos" (si el portal lo
  soporta en un solo archivo) o repita la descarga por tipo — es una
  instrucción de uso a comunicarle, no algo que la extensión necesite saber
  de antemano.
- **Subida**: un solo archivo (el reporte) por corrida, al endpoint del Host
  — ver §4.2. Reintentos simples ante error de red; nunca reintenta el login
  ni guarda la clave del SRI en ningún lado (ni `localStorage` de la
  extensión, ni el servidor).
- **Nunca** hace `Process.Start` ni descarga a disco persistente del usuario
  — con el diseño de `fetch` de arriba, el reporte nunca toca el disco de la
  contadora en absoluto.
- Sigue habiendo una tarea de mantenimiento recurrente (como ya preveía el
  plan original) porque el portal puede cambiar sin aviso — pero ahora es
  "un formulario puede cambiar de campo/ruta", no "diez tipos de fila pueden
  cambiar de estructura". Mismo mensaje de error visible a la usuaria si algo
  falla.

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
  - Body (simplificado tras §2.4 — ya no es un batch de XML): `{ ruc,
    contenidoReporte }` (el texto tabulado tal cual lo entrega el portal;
    JSON con un solo string es más simple que `multipart/form-data` para un
    archivo de texto chico). El content script decodifica el `fetch` del
    portal como **ISO-8859-1** (§2.5 — el reporte no es UTF-8) antes de
    meterlo en el JSON como texto; si en cambio se manda el archivo crudo en
    base64, la decodificación de encoding se hace del lado del servidor —
    a definir en F1, cualquiera de las dos funciona, importa solo que se
    haga en algún punto del camino, una sola vez.
  - Parseo del reporte: formato real confirmado en F0 (§2.5) —
    `RUC_EMISOR/RAZON_SOCIAL_EMISOR/TIPO_COMPROBANTE/SERIE_COMPROBANTE/
    CLAVE_ACCESO/FECHA_AUTORIZACION/FECHA_EMISION/IDENTIFICACION_RECEPTOR/
    VALOR_SIN_IMPUESTOS/IVA/IMPORTE_TOTAL/NUMERO_DOCUMENTO_MODIFICADO`, 1
    línea por comprobante con encabezado (más simple que el formato legado
    de 2018 que asumía `SriQueryAuthorizationProof`, que traía 2 líneas por
    fila y sin montos). Por cada clave de acceso nueva para ese RUC:
    **upsert** en `ComprobantesSriDescargados` con **todos los campos ya
    completos** desde el reporte — no hace falta ningún paso de hidratación
    aparte para tener tipo/proveedor/montos (cambio respecto a la primera
    versión del plan, ver §2.5/§4.4). Idempotente: la extensión puede volver
    a mandar el mismo rango de fechas sin duplicar ni pisar datos.
  - Respuesta: `{ total, nuevos, yaExistian, errores: [{fila, motivo}] }` —
    la extensión puede mostrarle un resumen a la usuaria. Es una operación
    rápida (parseo + upsert de texto, sin llamadas SOAP en el camino) — el
    endpoint responde apenas termina de guardar.
  - **Este es el primer endpoint del Host que NO usa la cookie de Identity**
    — es una pieza nueva de infraestructura (una policy de autenticación por
    Bearer token, además de la de cookie ya existente), a construir con
    cuidado y probar con tests de integración (token válido/inválido/
    revocado/RUC no autorizado). Vale la pena aislarla en
    `PsaWeb.Identidad` (o un `PsaWeb.Identidad.TokensApi` chico) para no
    ensuciar `Program.cs` con lógica de validación de token inline.

### 4.3 Dónde se guarda el staging de comprobantes del SRI

**Decisión: `PsaWebPlataforma`, no `PeachEBills`.** `PeachEBills` es la base
compartida que ya usan los `.exe` de escritorio y otras herramientas del
área — no es de PSA-web, y hasta ahora solo se le agregan filas a tablas que
**ya existían** (nunca se creó una tabla nueva ahí). El staging de
comprobantes del SRI es un dato **enteramente nuevo**, sin ningún consumidor
fuera de este módulo, así que va a la base que sí es propia y ya tiene el
pipeline de migraciones EF Core andando (`PsaWebPlataforma`, creada en
F-Shell-0 para Identity).

Tablas nuevas propuestas (vía migración EF, en un `DbContext` nuevo o
extendiendo el de Identidad — a decidir en F1, probablemente un
`ConciliacionDbContext` propio para no mezclar con el esquema de Identity):

- `ComprobantesSriDescargados`: `Id`, `Ruc` (empresa/cliente), `ClaveAcceso`
  (49 dígitos, índice único por `Ruc+ClaveAcceso`) — el resto de las columnas
  **se completan todas de una sola vez desde el reporte** (§2.5, ya no hay
  paso de hidratación aparte para esto): `TipoComprobante`, `RucEmisor`,
  `RazonSocialEmisor`, `SerieComprobante` (estab-ptoemi-secuencial),
  `FechaEmision`, `FechaAutorizacion`, `IdentificacionReceptor` (para
  validar que corresponde al RUC de la empresa), `Subtotal`, `Iva`, `Total`,
  `NumeroDocumentoModificado` (para vincular una NC a su factura). Más 2
  columnas nullable que sí dependen del WS, llenadas bajo demanda (§4.4):
  `Estado` + `FechaVerificacionEstado` (null hasta que alguien presione
  "Verificar con el SRI" o corra la re-verificación de las filas
  sospechosas — nunca se asume "vigente" por default). `FechaDescarga`,
  `SubidoPor` (usuario del token).
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
  — el full outer join + las 5 reglas de salida) y el **parser del reporte**
  (`LectorReporteComprobantesSri`, formato TSV real confirmado en §2.5 — ya
  no hace falta portar `ComprobanteE`/`XmlReader` de `ImportXmlLib` para
  armar Set A, el reporte trae todo sin necesidad de tocar XML). Ambos toman/
  devuelven DTOs, 100% testeables con el archivo de muestra real de CPTDC
  (§9) guardado como fixture de test. `ComprobanteE`/`ImportXmlLib` quedan en
  reserva para cuando haga falta el XML completo de un comprobante puntual
  (backlog, no bloquea el MVP — ver hidratación abajo).
- **Con ODBC** (dentro del mismo proyecto, igual que `PsaWeb.Ats/Compras/`):
  **decisión #4 revisada y afinada (2026-09-15)**, tras leer el código real
  en vez de asumir que "los lectores de compras del ATS" son una sola pieza
  reusable en bruto — no lo son, hay que separar dos cosas distintas:
  - `LectorAuxiliarComprasAts.NumeroAutorizacionAsync` (+ sus 4 helpers
    internos: `PostOrderPorReferenciaAsync`, `PostOrderCompraOriginalAsync`,
    `NumeroCompletoDeCompraOriginalAsync`, `PostOrderOrdenDeCompraVinculadaAsync`)
    **no tiene ninguna dependencia del esquema del ATS** — verificado
    (`using System.Data.Odbc; using PsaWeb.Comprobantes.Sri;`, nada de
    `PsaWeb.Ats.Esquema`). Es 100 % reusable tal cual. **Se mueve** (mover
    archivo, no reescribir lógica) a `PsaWeb.Comprobantes/Compras/` — el
    único residente genuinamente ATS-específico de ese archivo,
    `CodigosSustentoAts` (códigos de la Tabla 5 del ATS), se queda en
    `PsaWeb.Ats` (o se separa a su propio archivo ahí). `PsaWeb.Ats` pasa a
    referenciar la versión movida — mismo comportamiento, cero cambio de
    lógica, sus tests actuales no deberían notar la diferencia.
  - `LectorComprasAts.LeerAsync` **NO se puede reusar en bruto** — devuelve
    `IReadOnlyList<detalleComprasType>`, el tipo generado por `xsd.exe` del
    esquema XML del ATS (`PsaWeb.Ats.Esquema`). Está pensado para alimentar
    directamente el XML del ATS (clasificación por tipo de comprobante,
    buckets de IVA por tarifa para el Formulario 104), no para dar un total
    simple por compra. Conciliación SRI necesita algo más chico: listar las
    compras del período con proveedor + autorización + **un total por
    comprobante** (no un desglose por tarifa de IVA) — se escribe un lector
    nuevo y liviano (`LectorComprasParaConciliacion`, en
    `PsaWeb.Comprobantes/Compras/` junto al anterior) que sigue el mismo
    patrón de SQL (`JrnlHdr`+`Vendors`, mismo filtro de período/diario) pero
    devuelve un DTO neutral, no el tipo del esquema del ATS. No se toca
    `LectorComprasAts` ni su comportamiento — es una lectura nueva al lado,
    no un reemplazo. **Addendum al diseñar F3 en detalle (§13.1)**: para el
    "total" específicamente, ni siquiera hace falta escribir una consulta
    `SUM` nueva (riesgoso en Sage con partida doble) — se reusa
    `LectorDetalleComprasAts.LeerImponiblesAsync`/`BucketsComprasAts`
    (tampoco acoplados al esquema del ATS, y ya validados contra datos
    reales de CPTDC), movidos junto con `LectorAuxiliarComprasAts`.
  - Con esto, mover código es de bajo riesgo (una clase sin acoplamiento se
    reubica) y no hace falta que `PsaWeb.Conciliacion` dependa de
    `PsaWeb.Ats` para nada — ambos (`PsaWeb.Ats` y el nuevo módulo) terminan
    apoyándose en `PsaWeb.Comprobantes`, que es justamente el lugar ya
    establecido para piezas compartidas entre módulos de comprobantes.
- **Con EF** (staging): un lector de Set A sobre `ComprobantesSriDescargados`
  — vive en `PsaWeb.Conciliacion` o en un pequeño `PsaWeb.Conciliacion.Datos`
  si se quiere separar del todo la lógica pura del acceso a la BD propia (a
  decidir según cuánto crezca; para el tamaño de este módulo probablemente
  no hace falta el split).

**Ya NO hay hidratación obligatoria** (cambio de diseño en F0, §2.5): la
primera versión de este plan asumía que el reporte solo traía metadata y que
había que llamar al WS SOAP por cada clave nueva para tener tipo/proveedor/
montos. El reporte real ya trae todo eso — Set A queda completo apenas se
sube el reporte, sin ningún worker de por medio. El único dato que el
reporte NO trae es el **estado** (vigente/anulado) — para eso sí hace falta
el WS, pero **bajo demanda**, no como paso obligatorio del alta:
- Botón "Verificar con el SRI" en la UI (§7): llama a `ConsultaComprobante`
  (§4.5) para una clave puntual, guarda `Estado`+`FechaVerificacionEstado`.
- El motor (abajo), antes de mostrar una fila en las salidas 3/4/5, dispara
  esa misma verificación si `FechaVerificacionEstado` es null o es vieja —
  así la salida 5 nunca depende de un dato que ya pudo quedar desactualizado.
- Para el alta del reporte en sí (F2, este mismo punto) no hace falta ningún
  `IHostedService`/worker — se simplifica bastante respecto a la primera
  versión del plan. **Ojo**: esto es distinto del worker de **verificación
  de estado** que sí se agrega en F3/F4 (§13.4, decidido con el usuario
  2026-09-17) — ese no hidrata nada, solo confirma si un comprobante ya
  conciliado sigue vigente en el SRI; no confundir los dos.

**Algoritmo** (tal como lo describe la tarea, sin cambios): full outer join
por clave de acceso entre Set A (SRI, del staging — ya completo desde que se
sube el reporte, §2.5) y Set B (Sage, del lector de compras), 5 salidas:

1. Solo en SRI → pendientes de registrar.
2. Solo en Sage → revisar validez (incluye el caso "clave vacía/mal
   tecleada" de §2.3 — texto de UI debe distinguirlo de "SRI no lo tiene").
   **Refinamiento post-F0**: si la compra sí tiene algo tecleado en el campo
   de autorización (no está vacío), no hace falta que la extensión haya
   bajado ese comprobante para verificarlo — se puede consultar esa clave
   directo contra `ConsultaComprobante` (§4.5), que no depende de que el
   período esté cubierto por una descarga. Distingue 3 sub-casos en la UI:
   clave vacía (nunca se tecleó nada), clave tecleada pero el WS dice que no
   existe/no es válida (error de digitación real), clave tecleada y el WS la
   confirma válida (probablemente el comprobante es de un período que la
   extensión todavía no bajó — no es un error, solo falta de cobertura).
3. En ambos, valores distintos → delta campo a campo (subtotal/IVA/total).
4. En ambos, valores iguales pero otros datos distintos (fecha, RUC emisor,
   tipo) → error de captura sin impacto en saldo.
5. Anulados en SRI pero no en Sage → el reporte no trae columna de estado en
   absoluto (§2.5), así que esta salida **siempre** depende de una consulta
   puntual al WS liviano (`ConsultaComprobante`, §4.5) por clave de acceso —
   nunca hay un `Estado` de staging que "ya venga dado" para confiar
   ciegamente, ni siquiera por un rato. Para las compras que caen en 3, 4 o
   5, se dispara esa verificación justo antes de mostrarlas como
   discrepancia — igual que pide la tarea. No se consulta para *todas* las
   compras del período (sería lento e innecesario); solo para las que el
   join ya marcó como sospechosas (más el sub-caso de la salida 2 arriba).

**Tolerancias**: comparar montos con una tolerancia de redondeo (ej.
±$0.01) antes de marcar salida 3 — a definir el umbral exacto con el usuario
al implementar (no bloquea el diseño).

### 4.5 Cliente SOAP de verificación puntual

**Resuelto en F0 (2026-09-15) sin necesitar el portal**: se bajaron los 4
WSDL reales (`ConsultaComprobante` en pruebas `celcer.sri.gob.ec` y
producción `cel.sri.gob.ec`; `AutorizacionComprobantesOffline` en ambos
también — el legado solo conocía la de producción, pero sí existe la de
pruebas). **Los dos servicios existen y responden, pero no son
intercambiables — cada uno cumple un rol distinto**:

- **`ConsultaComprobante.consultarEstadoAutorizacionComprobante`**
  (namespace `http://ec.gob.sri.ws.consultas`) — toma `claveAcceso` y
  devuelve **solo metadata de estado**: `estadoConsulta`, `mensajes[]`,
  `estadoAutorizacion` (AUTORIZADO/NO AUTORIZADO/...), `tipoComprobante`,
  `rucEmisor`, `fechaAutorizacion`. **No devuelve el XML del comprobante.**
  Es un chequeo liviano, hecho a medida para la pregunta que hace la salida
  5 ("¿este comprobante sigue autorizado de verdad?") sin traer todo el
  documento.
- **`AutorizacionComprobantesOffline.autorizacionComprobante`** (el que ya
  usa `SriQueryAuthorizationProof` en producción) — toma
  `claveAccesoComprobante` y devuelve el **XML completo** del comprobante
  (envuelto en el tag `<comprobante>`, como ya se sabía por §2.1/§2.2). Más
  pesado, pensado para cuando hace falta el documento entero, no solo su
  estado. También tiene una operación `autorizacionComprobanteLote`, pero
  **no sirve para verificar muchas claves sueltas de una sola llamada** —
  toma una `claveAccesoLote` (la clave de un envío por lote, no una lista de
  claves de comprobantes individuales), así que no aplica a este módulo.

**Decisión — se usan los dos, con roles bien acotados** (esto se afinó dos
veces: primero se pensó `AutorizacionComprobantesOffline` como plan B de
`ConsultaComprobante`; con el hallazgo de §2.4 pasó a ser la hidratación
central; con el archivo real de §2.5 — el reporte ya trae montos — vuelve a
ser secundario, ya no hay hidratación obligatoria):

- **`ConsultaComprobante.consultarEstadoAutorizacionComprobante`** — cliente
  **principal** del módulo: la re-verificación puntual de estado (§4.4,
  salidas 2/3/4/5) y el botón "Verificar con el SRI" de la UI (§7). Es todo
  lo que hace falta para el MVP, porque el reporte ya trae tipo/proveedor/
  montos — lo único que el motor necesita del WS es el estado.
- **`AutorizacionComprobantesOffline.autorizacionComprobante`** — queda en
  reserva para cuando haga falta el **XML completo** de un comprobante
  puntual (ej. un botón "Ver XML" en el popup de detalle, si se agrega más
  adelante — mismo patrón que ya tiene Retenciones/FE con sus popups de
  detalle) — no es parte del flujo obligatorio del MVP. Es el que ya está
  probado en producción por el código legado, así que la puerta queda
  abierta sin costo de mantenerla investigada.

Ambos con pruebas *y* producción confirmadas (§F0). Implementación: HTTP
crudo (no WCF), un cliente por servicio, ambos siguiendo el mismo patrón.

Port del patrón de `SriQueryAuthorizationProof.CallSriWS` (§2.1): HTTP crudo,
POST `text/xml`, sin cliente WCF generado (más simple de mantener y de
testear con `HttpClient`/`IHttpClientFactory` que un `ServiceReference`).
Vive en `PsaWeb.Conciliacion` (o `PsaWeb.Comprobantes/Sri/` si se decide que
es un helper compartido, ej. reutilizable a futuro por otros módulos que
necesiten verificar un comprobante puntual).

**Probado en vivo en F0 (2026-09-15) contra `celcer.sri.gob.ec` (pruebas),
con una clave de acceso sintética** (dato inventado con checksum módulo-11
válido — no corresponde a ningún comprobante ni cliente real; no hizo falta
login ni datos de un cliente para esto). Confirma el contrato exacto de
ambas respuestas más allá de lo que dice el WSDL:

```xml
<!-- Request a ambos servicios: mismo patrón, cuerpo con la clave -->
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                   xmlns:tns="http://ec.gob.sri.ws.consultas">
  <soapenv:Body>
    <tns:consultarEstadoAutorizacionComprobante>
      <claveAcceso>1509202601179205180000110010010000000011234567811</claveAcceso>
    </tns:consultarEstadoAutorizacionComprobante>
  </soapenv:Body>
</soapenv:Envelope>
```

```xml
<!-- Respuesta real de ConsultaComprobante (clave válida en formato, no existe): -->
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
  <ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas">
    <EstadoAutorizacionComprobante>
      <estadoConsulta>RECHAZADA</estadoConsulta>
      <claveAcceso>1509202601179205180000110010010000000011234567811</claveAcceso>
      <mensajes><mensaje>
        <identificador>99</identificador>
        <mensaje>ERROR AL CONSULTAR DATOS DEL SERVICIO WEB</mensaje>
        <informacionAdicional>No existen datos para los parámetros ingresados</informacionAdicional>
        <tipo>ERROR</tipo>
      </mensaje></mensajes>
    </EstadoAutorizacionComprobante>
  </ns2:consultarEstadoAutorizacionComprobanteResponse>
</soap:Body></soap:Envelope>
```

```xml
<!-- Respuesta real de AutorizacionComprobantesOffline (misma clave inexistente): -->
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
  <ns2:autorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.autorizacion">
    <RespuestaAutorizacionComprobante>
      <claveAccesoConsultada>1509202601179205180000110010010000000011234567811</claveAccesoConsultada>
      <numeroComprobantes>0</numeroComprobantes>
      <autorizaciones/>
    </RespuestaAutorizacionComprobante>
  </ns2:autorizacionComprobanteResponse>
</soap:Body></soap:Envelope>
```

Con una clave real (una vez que la contadora traiga una desde el portal),
`<autorizaciones>` debería traer uno o más `<autorizacion>` (histórico de
intentos: `estado`, `numeroAutorizacion`, `fechaAutorizacion`, `ambiente`,
`comprobante` con el XML escapado adentro — coincide con lo que ya asumía
`ComprobanteE`/§2.2), y `numeroComprobantes` > 0. Con una clave sin el
formato correcto (probado también, no listado arriba), `ConsultaComprobante`
devuelve el mismo `estadoConsulta=RECHAZADA` pero con
`informacionAdicional="Error en el formato de la clave acceso."` —
distingue limpio "formato inválido" de "no existe", útil para el mensaje
de la UI en la salida 2 del motor (§4.4). **Con esto, el cliente SOAP queda
completamente especificado — lo único que falta es escribirlo, no
investigarlo más.**

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

**Decidido: Impuestos.** Se evaluó contra Comprobantes Electrónicos (el
objeto central de la pantalla son comprobantes, mismo tipo de dato que
Facturas/NC/Liquidaciones/Retenciones) pero las 4 entradas actuales de esa
categoría son todas de **emisión** (generan y envían algo a Datil); este
módulo no emite nada, es de solo lectura/auditoría, y su razón de ser es
alimentar la calidad de la declaración de impuestos — más cerca en propósito
de ATS (la otra única entrada de Impuestos hoy) que de Facturas/Retenciones.
Se agrupa por *propósito*, no por *tipo de dato*.

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
- 6 secciones/pestañas — las 4 salidas "instantáneas" (1-4, §13.3) más
  **"Conciliado sin verificar"** (`CoincidePendienteDeVerificar`, §13.4) y
  **"Anulados"** (salida 5, solo se llena tras verificar) — cada una con su
  contador en el título (ej. "Solo en Sage (12)"). La de "Solo en Sage" lleva
  una nota visible aclarando que puede ser por clave no digitada, no solo
  por comprobante inválido (§2.3).
- Nota operativa visible (no solo en este doc): recordar que el listado de
  "recibidos" del portal solo cubre bien los últimos 5 días (§2.4/§4.1) —
  la pantalla puede mostrar cuándo fue la última subida por empresa para que
  la usuaria sepa si le conviene correr la extensión de nuevo antes de
  confiar en el resultado.
- Botón individual **"Verificar con el SRI"** en las filas de "Conciliado
  sin verificar"/3/4 → dispara la consulta puntual al WS (§4.5/§13.4) para
  esa fila. Botón masivo **"Verificar pendientes (N)"** arriba de la
  pestaña "Conciliado sin verificar" → dispara la verificación de todas las
  filas de esa pestaña con concurrencia acotada (§13.4) — es la única forma
  de que la salida 5 (anulados) se detecte sin revisar fila por fila a mano.
- Sin export a Excel en el primer corte (a diferencia de los demás módulos)
  — es una pantalla de trabajo/triage, no un reporte para imprimir; se
  agrega si el usuario lo pide después de probarla.
- Sin acción de "marcar como resuelto" en el primer corte — es de solo
  lectura; si hace falta llevar un registro de qué discrepancias ya se
  revisaron, es una mejora de fase posterior (tabla `CorridasConciliacion`
  de §4.3 serviría de base).

---

## 8. Fases propuestas

**F0 — CERRADA DEL TODO (2026-09-15 investigación de escritorio, 2026-09-17
sesión real).** Nada queda pendiente de investigación para arrancar F1.
Resumen de las 3 rondas:

1. **2026-09-15, escritorio**: los 4 WSDL reales bajados y comparados, y
   probados en vivo con una clave sintética (§4.5) — contrato de
   `ConsultaComprobante`/`AutorizacionComprobantesOffline` confirmado.
   Hallazgo del export masivo tipo reporte (§2.4), triangulado por fuentes
   externas (documentación de Dátil + mercado de herramientas de terceros)
   — en ese momento se asumió que el reporte no traía montos y que hacía
   falta un worker de hidratación.
2. **2026-09-15, mismo día**: decisión #4 afinada leyendo el código real de
   `PsaWeb.Ats` (§4.4) — qué lectores de compras son genuinamente reusables.
3. **2026-09-17, sesión real de CPTDC contra el portal** (§2.5) — la más
   importante: confirma host/ruta/login/mecanismo de descarga, y **corrige**
   dos supuestos de las rondas anteriores — el reporte real trae 12 columnas
   con encabezado (no 10 sin encabezado como el formato legado de 2018) y
   **ya incluye los montos**, así que la hidratación obligatoria por WS
   (diseñada el 2026-09-15) se elimina del MVP — el WS queda solo para
   estado, bajo demanda (§4.4). También reveló el encoding real (ISO-8859-1)
   y que la pantalla es JSF clásico, no una SPA con API propia.

- **F1 — extensión mínima + endpoint de subida** (detallado en §11): manifest
  v3, detección de la pantalla de comprobantes recibidos ya consultada (la
  contadora navega y filtra a mano, §11.1 — la extensión no automatiza el
  menú) + botón propio que replica el POST del formulario (`fetch` con
  `javax.faces.ViewState`) → POST del reporte al endpoint nuevo con token de
  API. `PsaWeb.Identidad`/`PlataformaDbContext`: tabla `TokensExtension` +
  generación/validación de token + pantalla `/mi-cuenta/extension` (§11.3/
  §11.5). `PsaWeb.Conciliacion`: `ConciliacionDbContext` + migración de
  `ComprobantesSriDescargados` (§4.3).
- **F2 — parseo del reporte + Set A** (más chica que en la versión anterior
  del plan — sin worker de hidratación): `LectorReporteComprobantesSri`
  (decodifica ISO-8859-1, parsea las 12 columnas, valida `CLAVE_ACCESO` de
  49 dígitos) en `PsaWeb.Conciliacion`. Lector de Set A sobre el staging.
  Tests con el archivo real de CPTDC guardado como fixture (§9) — ya
  disponible, no hace falta esperar a nada para escribir estos tests.
- **F3 — Set B + motor**: mover `LectorAuxiliarComprasAts` +
  `LectorDetalleComprasAts.LeerImponiblesAsync`/`BucketsComprasAts`/
  `ClasificadorLineasComprasAts` + `EsAutoretencion` a
  `PsaWeb.Comprobantes/Compras/` (§13.1) + escribir
  `LectorComprasParaConciliacion` como orquestación sobre esas piezas ya
  validadas. `MotorConciliacion` con las 4 clasificaciones instantáneas +
  tests (§13.3/§13.5: match exacto, solo SRI, solo Sage —con y sin clave
  tecleada—, delta de valores, delta de metadata, con tolerancia de
  redondeo).
- **F4 — clientes SOAP + worker de verificación + página**: cliente
  `ConsultaComprobante` (§4.5, para los botones "Verificar" y las salidas
  2/3/4) — es el único cliente SOAP que hace falta para el MVP.
  `VerificacionEstadoSriWorker` (§13.4, decidido 2026-09-17: automático, no
  solo botón manual — es lo que realmente entrega la salida 5). Módulo
  `PsaWeb.Modules.ConciliacionSri`, página `/conciliacion-sri` (§7) con
  botones individual/masivo, gate `quconcsri` (§6), fila en
  `AppCatalogo.Todas`, doble registro de rutas.
- **F5 — pulido de la extensión + deploy** (detallado en §15): catálogo de
  mensajes de error de la extensión, empaquetado (unpacked + modo
  desarrollador para arrancar, con ruta de escalamiento a política
  empresarial documentada); deploy del módulo — mismo sitio IIS que los
  demás, sin base nueva (`TokensExtension`+`ComprobantesSriDescargados`
  comparten la física de `PsaWebPlataforma`), con 2 migraciones nuevas que
  se aplican solas al arrancar.

Estimado grueso: **9-13 días de dev** (subió un poco desde la versión
anterior —7-11— por el worker de verificación de estado que se agregó en
F4 al detallar F3; sigue por debajo de la primera estimación —9-14— porque
el worker de hidratación completo de F2 se sigue cayendo del todo: no hay
que portar `ComprobanteE`/`XmlReader` ni un cliente SOAP que traiga XML
completo). Dominado por F1 (primera pieza de infraestructura nueva del
repo — token de API — y el content script de la extensión) y F4 (2 clientes
SOAP + un worker cross-company nuevo).

---

## 9. Fixture de prueba

Reusar **CPTDC (RUC `1792051800001`)** — ya validada para Kardex y ATS,
`lparedes` ya tiene acceso, Sage local disponible en PREDATOR.

**Reporte real ya obtenido (2026-09-17)**: agosto/2026 completo, 444
facturas recibidas, filtro "Factura" (falta correr con "Todos"/otros tipos
para tener NC/retenciones/liquidaciones de muestra — pedir en la próxima
sesión con el portal). **No se commitea al repo** — mismo criterio que el
ATS con los entregables reales de CPTDC (`docs/ESTADO-MIGRACION-WEB.md` §5.5):
son datos reales de terceros (RUC/razón social/montos de los proveedores de
CPTDC). Para los tests de F2 se arma un **archivo sintético** con el mismo
formato exacto de 12 columnas (§2.5) y unas ~10-15 filas inventadas que
cubran los casos: comprobante que va a estar bien contabilizado en Sage,
uno pendiente de registrar, uno con clave de acceso que se va a hacer
coincidir con una compra de Sage con la clave mal tecleada a propósito
(para probar la salida 2), una NC con `NUMERO_DOCUMENTO_MODIFICADO`
completo, y algún caso de tildes/Ñ para probar la decodificación ISO-8859-1.
El archivo real queda para la validación manual contra Sage al implementar
(sigue en Descargas de PREDATOR, no en el repo), igual que se hizo con el
ATS.

Falta del área: correr la extensión de punta a punta contra el portal real
en F1/F5 (ya no bloquea nada de F0-F4, que se pueden implementar y testear
con el archivo sintético); y, para la validación de F4 contra Sage,
confirmar un período con comprobantes recibidos variados en Sage también
(al menos un caso de cada salida — bien contabilizado, pendiente, clave mal
tecleada, y si es posible algún anulado real).

---

## 10. Decisiones (cerradas 2026-09-15)

1. ~~Categoría de menú~~ **Cerrada**: Impuestos (§5).
2. ~~Token de API vs. otra forma de autenticar la extensión~~ **Cerrada**
   (§4.2): token de API Bearer, generado desde `/mi-cuenta/extension`,
   mostrado una sola vez, guardado hasheado en `PsaWebPlataforma`
   (`TokensExtension`), revocable. Alternativas descartadas y por qué:
   cookie de sesión compartida (frágil, cross-context — el `fetch` de una
   extensión no es una navegación de la misma pestaña logueada al sitio
   PSA), Windows Auth (la extensión no corre en el dominio, corre en la
   máquina de la contadora contra un sitio externo), certificado cliente
   (sobre-ingeniería para 1-2 usuarias, y complica la instalación de la
   extensión). Es el primer endpoint del Host sin cookie de Identity — se
   construye con su propia policy de autenticación, aislada en
   `PsaWeb.Identidad` para no ensuciar `Program.cs`, y con tests de
   integración de los 4 casos (token válido/inválido/revocado/RUC no
   autorizado para ese usuario).
3. ~~Dónde vive el staging~~ **Cerrada**: `PsaWebPlataforma` (§4.3) — no
   `PeachEBills`, que es la base compartida con los `.exe` de escritorio y
   nunca se le crean tablas nuevas; el staging de comprobantes SRI no tiene
   ningún consumidor fuera de este módulo.
4. ~~`PsaWeb.Conciliacion` referenciando `PsaWeb.Ats` vs. extraer los lectores
   de compras a `PsaWeb.Comprobantes`~~ **Afinada en F0** (§4.4, 2026-09-15,
   tras leer el código real en vez de suponer): no es una sola decisión, son
   dos piezas distintas. `LectorAuxiliarComprasAts` (autorización + lookups
   de compra original) no tiene ninguna dependencia del esquema del ATS —
   se mueve tal cual a `PsaWeb.Comprobantes/Compras/`, mover-archivo de
   bajo riesgo. `LectorComprasAts.LeerAsync` sí está atado al esquema XML
   del ATS (devuelve `detalleComprasType`) — no se reusa; se escribe un
   lector nuevo y chico al lado, en el mismo lugar compartido, que da un
   total simple por compra en vez del desglose por tarifa de IVA que
   necesita el ATS. Sigue pendiente solo confirmar con el usuario que vale
   la pena mover `LectorAuxiliarComprasAts` ahora (en vez de cuando se
   implemente F3) — es cambiar el archivo de carpeta, no lógica, y no debería
   afectar los tests actuales del ATS.
5. ~~Estrategia de descarga de la extensión: DOM automation vs. interceptar la
   API interna del portal~~ **Resuelto en F0**, en 2 rondas: primero (§2.4,
   2026-09-15, por investigación externa) que el portal tiene un export
   masivo tipo reporte, así que ninguna de las dos hacía falta; después
   (§2.5, 2026-09-17, con sesión real de CPTDC) que además se puede
   **replicar el POST del formulario vía `fetch`** en vez de simular un
   click — ni siquiera queda DOM automation de verdad, solo lectura del
   campo `javax.faces.ViewState` antes de mandar el POST.
6. ~~Endpoint SOAP definitivo~~ **Resuelto en F0** (§4.5, 2026-09-15/17):
   los WSDL reales confirman que `ConsultaComprobante` y
   `AutorizacionComprobantesOffline` son servicios distintos y
   complementarios, no alternativas del mismo. Con el archivo real de §2.5
   (el reporte ya trae montos), el rol de cada uno se achicó respecto a la
   primera resolución: `ConsultaComprobante` queda como el **único** cliente
   SOAP necesario para el MVP (verificación de estado bajo demanda);
   `AutorizacionComprobantesOffline` (XML completo) queda en reserva para
   una mejora futura (ej. un botón "Ver XML"), no forma parte del flujo base.
7. ~~Alcance del primer corte~~ **Cerrada**: on-demand. El motor corre desde
   la página cada vez (Set A x Set B de Sage, en el momento), sin persistir
   resultados de corridas (`CorridasConciliacion` de §4.3 queda en backlog,
   no bloquea F1-F4). Es un cambio aditivo — se agrega histórico más
   adelante si hace falta, sin romper nada de lo ya construido.
8. ~~El worker de hidratación, ¿con o sin apagador?~~ **Decisión superada**
   (§2.5, 2026-09-17): no hay ningún worker de **hidratación** en el diseño
   final — el reporte real ya trae los montos, así que no hace falta
   background service para completar Set A.
   **Actualización posterior (mismo día, al detallar F3, §13.4)**: sí se
   agregó un worker distinto, de **verificación de estado**
   (`VerificacionEstadoSriWorker`) — no para completar Set A, sino porque la
   salida 5 (anulados) no se puede detectar sin preguntarle al WS, y dejarlo
   solo como botón manual corre riesgo real de omitirse. Arranca
   `Habilitado=true` por defecto (mismo razonamiento que se usaba acá: solo
   lee, no emite nada). No es una vuelta atrás de esta decisión #8 (que
   sigue siendo válida para la hidratación) — es una necesidad distinta que
   apareció al diseñar F3 en detalle.

**Con esto, las 8 decisiones originales del plan quedan cerradas** (una de
ellas, la 8, terminó no aplicando al diseño final tal como se planteó,
aunque una necesidad relacionada pero distinta apareció después en F3).
**F0 está 100% cerrada** (§2.5, §8) — no queda nada de investigación
pendiente, ni de diseño ni de portal, para arrancar F1.

---

## 11. F1 en detalle — extensión de Chrome + endpoint de subida

### 11.1 Ajuste de alcance de la extensión — DECIDIDO (2026-09-17)

**Confirmado con el usuario: la automatización queda acotada solo al último
paso** (descargar/subir el reporte), no a todo el flujo de navegación. Razón
original: automatizar la **navegación** por
el menú del portal (para llegar a "Comprobantes electrónicos recibidos")
suma una parte frágil (submenús, rutas que cambian) que en realidad no hace
falta automatizar — la contadora YA navega ahí manualmente hoy, y elegir el
período + tipo de comprobante son 3 clicks que ella ya sabe hacer sin
ayuda. **La extensión no necesita mover a la usuaria por el portal**: le
alcanza con activarse recién cuando detecta que la pestaña activa está
parada en la pantalla de comprobantes recibidos con una consulta ya hecha
(mismo patrón de detección que el login, por URL/DOM), y desde ahí ofrecer
un botón propio ("Subir a PSA") que reemplaza únicamente el último paso —
"Descargar reporte" + guardar/adjuntar el archivo a mano — por
`fetch`+POST directo al Host. Esto reduce la superficie de automatización a
su mínimo real: **la extensión automatiza un botón, no un flujo**. Encaja
mejor todavía con "semi-automática" (§1 de la tarea original) y baja aún
más el mantenimiento recurrente que ya preveía §4.1.

Flujo resultante para la contadora:
1. Login manual en `srienlinea.sri.gob.ec` (como siempre).
2. Navega manualmente a "Comprobantes electrónicos recibidos", elige
   año/mes/tipo, click en "Consultar" (como siempre — la extensión no toca
   nada de esto).
3. La extensión detecta la pantalla y muestra su propio botón flotante o
   habilita el ícono de la barra ("Subir a PSA").
4. Click en ese botón → la extensión arma y manda el POST del reporte
   (§11.2), sin que se abra ningún diálogo de guardar archivo.
5. Notificación de Chrome (`chrome.notifications`) con el resumen que
   devuelve el Host (`N nuevos, M ya existían, E errores`).

### 11.2 Estructura de la extensión (Manifest V3)

```
extension/
  manifest.json
  content-script.js   // corre en srienlinea.sri.gob.ec
  background.js       // service worker
  popup.html + popup.js  // configuración del token + estado
```

- `manifest.json`: `manifest_version: 3`, `permissions: ["storage", "notifications"]`,
  `host_permissions: ["https://srienlinea.sri.gob.ec/*", "<host del Host de PSA — configurable>"]`,
  `content_scripts` con `matches: ["https://srienlinea.sri.gob.ec/*"]`,
  `background.service_worker`, `action.default_popup`.
- **`content-script.js`** (corre en el contexto de la página del SRI, así
  que las cookies de sesión aplican solas a cualquier `fetch` que haga):
  - Detecta login (§2.5: navegación a `/sri-en-linea/contribuyente/perfil`)
    — informativo, no bloquea nada por sí solo.
  - Detecta la pantalla de comprobantes recibidos con resultados cargados
    (ruta `comprobantesRecibidos.jsf` + tabla de resultados presente en el
    DOM) → inyecta el botón "Subir a PSA".
  - Al click: lee del DOM el RUC ya pre-llenado en el formulario (§2.5 —
    nunca lo escribe la extensión, solo lo lee) + el campo oculto
    `javax.faces.ViewState` + arma el mismo `application/x-www-form-urlencoded`
    POST que dispara "Descargar reporte" (nombre exacto del parámetro del
    botón: por inspeccionar en el HAR real al escribir esto — es
    autoinspección de un archivo ya disponible, no requiere volver al
    portal).
  - `fetch(mismaUrl, {method:'POST', credentials:'include', body, headers})`
    → `await response.arrayBuffer()` (no `.text()` directo — el archivo es
    **ISO-8859-1**, §2.5; hay que decodificarlo explícitamente con
    `new TextDecoder('iso-8859-1').decode(buffer)` antes de usarlo como
    texto, si no las tildes/Ñ se corrompen en el propio navegador antes de
    llegar al servidor).
  - Manda el texto + el RUC leído al service worker vía
    `chrome.runtime.sendMessage({tipo:'reporteDescargado', ruc, contenido})`
    — el POST al Host de PSA lo hace el **service worker**, no el content
    script: evita cualquier restricción de CSP que la página del SRI le
    imponga a sus propios scripts, y separa con claridad "hablar con el
    SRI" de "hablar con PSA".
- **`background.js`** (service worker):
  - Al recibir el mensaje: lee el token guardado
    (`chrome.storage.local.get('tokenApi')`) — si no hay token, notifica
    "configurá tu token en el ícono de la extensión" y no manda nada.
  - `fetch(hostPsa + '/conciliacion-sri/api/comprobantes', {method:'POST',
    headers:{Authorization:'Bearer '+token, 'Content-Type':'application/json'},
    body: JSON.stringify({ruc, contenidoReporte: contenido})})`.
  - Muestra el resultado con `chrome.notifications.create(...)`.
- **`popup.html`/`popup.js`**: campo para pegar/actualizar el token de API,
  el host del Host de PSA (configurable — dev en PREDATOR vs.
  `http://192.168.0.11:8088` en producción, mismo espíritu que
  `appsettings.Development.json` vs. producción del resto del repo), y un
  estado de "última subida: fecha/hora, resultado".

### 11.3 Token de API — formato y validación

- Formato entregado a la usuaria: **`psaext_<prefijo8><secreto32>`**
  (patrón tipo GitHub PAT: un prefijo corto para poder buscarlo en la BD sin
  escanear todos los hashes, más un secreto largo aleatorio). Generado con
  `RandomNumberGenerator.GetHexString` o equivalente — no reusar el
  `PasswordHasher` de Identity (pensado para contraseñas de *baja* entropía
  con salt+trabajo costoso; un token ya es alta entropía por sí solo — alcanza
  y sobra con un **hash SHA-256 simple** del secreto completo, comparado con
  `CryptographicOperations.FixedTimeEquals` para evitar timing attacks).
- Tabla `TokensExtension`: `Id`, `UsuarioId` (FK a `AspNetUsers.Id`),
  `Prefijo` (8 chars, índice), `HashSecreto` (SHA-256 hex del secreto),
  `Creado`, `UltimoUso` (nullable, se actualiza en cada uso), `Revocado`
  (nullable). **Un solo token activo por usuario** — generar uno nuevo
  revoca el anterior automáticamente (más simple que administrar una lista;
  para 1-2 usuarias no hace falta más).
- **Vive en `PsaWeb.Identidad`/`PlataformaDbContext`**, no en un
  `ConciliacionDbContext` aparte (ajuste sobre §4.2/§4.3): conceptualmente es
  "cómo un usuario de Identity se autentica por un canal alternativo", el
  mismo dominio que ya cubre `PlataformaDbContext` (Identity + 2FA). El
  `ConciliacionDbContext` nuevo en `PsaWeb.Conciliacion` queda dedicado
  solo a `ComprobantesSriDescargados` (dato de negocio del módulo).
- Validación en el endpoint: separar `psaext_` + prefijo + secreto → buscar
  por `Prefijo` (no por hash completo, sería un table scan) → si existe y no
  está revocado, comparar `HashSecreto` contra `SHA256(secreto)` en tiempo
  constante → resolver `UsuarioId` → actualizar `UltimoUso`.
- **Mecanismo de enganche al pipeline**: dado que es un solo endpoint (no
  vale la pena un `AuthenticationScheme` completo todavía), se resuelve con
  un `IEndpointFilter` dedicado (`TokenExtensionEndpointFilter`) que lee el
  header `Authorization`, valida, y si es válido guarda el usuario resuelto
  en `HttpContext.Items` para que el handler del endpoint lo use; si no,
  corta con 401 antes de llegar al handler. Más simple de testear (se prueba
  el filtro aislado) y no interfiere con la cookie de Identity que ya usa
  todo el resto del Host.

### 11.4 Endpoint — forma final

```
POST /conciliacion-sri/api/comprobantes
Authorization: Bearer psaext_<prefijo><secreto>
Content-Type: application/json

{ "ruc": "1792051800001", "contenidoReporte": "RUC_EMISOR\tRAZON_SOCIAL...\n..." }
```

- El filtro resuelve `UsuarioId` → `ISecurityDirectory.EmpresasDelUsuarioAsync(usuario)`
  → 403 si el `ruc` del body no está en esa lista (mismo control de acceso
  que ya usan las páginas, §4.2).
- Handler: `LectorReporteComprobantesSri.Parsear(contenidoReporte)` (§4.4) →
  por fila, upsert en `ComprobantesSriDescargados` por `(Ruc, ClaveAcceso)`.
- Respuesta `200`: `{ total, nuevos, yaExistian, errores: [{fila, motivo}] }`.
  `400` si el contenido no tiene el encabezado esperado (portal cambió de
  formato — mensaje claro, no una excepción genérica).

### 11.5 Pantalla `/mi-cuenta/extension`

Mismo lugar que `/mi-cuenta/seguridad` (2FA, ya existe el patrón de "área de
mi cuenta"). Estados:
- Sin token: botón "Generar token para la extensión".
- Con token: "Token creado el DD/MM/AAAA · último uso: DD/MM/AAAA HH:mm (o
  'nunca usado')" + botón "Revocar" + botón "Generar uno nuevo" (revoca el
  actual). El secreto **nunca se vuelve a mostrar** después de generado —
  solo aparece una vez, con el aviso de copiarlo ya.

### 11.6 Tests de F1

- `TokenExtensionTests`: generar → validar formato correcto; validar
  correcto/incorrecto/revocado; un usuario nuevo revoca el token viejo del
  mismo usuario.
- `TokenExtensionEndpointFilterTests`: request sin header → 401; header con
  token inválido → 401; header con token de un usuario sin acceso a ese RUC
  → 403; token válido + RUC autorizado → pasa al handler.
- Test de integración del endpoint completo con `WebApplicationFactory`
  (patrón ya usado en el repo para los demás endpoints de export).

### 11.7 Qué NO entra en F1

- El parser del reporte en sí (`LectorReporteComprobantesSri`) es F2 — en F1
  el endpoint puede arrancar con un parser mínimo/de prueba y completarse en
  F2 sin tocar el contrato del endpoint.
- Nombre exacto del parámetro del botón "Descargar reporte" en el POST del
  content script — se saca inspeccionando el HAR real ya en mano (§2.5) al
  escribir el código, es transcripción, no investigación.
- Publicación/distribución de la extensión (unpacked vs. no listada en la
  Web Store) — eso es F5 (§8).

---

## 12. F2 en detalle — parseo del reporte + Set A

### 12.1 Dónde se decodifica ISO-8859-1 — decidido

§4.2 había dejado esto abierto ("a definir en F1, cualquiera de las dos
funciona"). Se decide acá: **se decodifica en el content script**, con
`new TextDecoder('iso-8859-1').decode(await response.arrayBuffer())` (§11.2)
— para cuando el texto llega al endpoint como campo de un JSON, ya es una
cadena .NET normal con los caracteres correctos. **El parser de F2 no toca
bytes ni codificaciones** — recibe un `string` ya bien formado. Más simple:
la decodificación ocurre una sola vez, lo más cerca posible de la fuente.

### 12.2 Contrato del parser

```csharp
namespace PsaWeb.Conciliacion;

public sealed record ComprobanteSriReportado(
    string RucEmisor,
    string RazonSocialEmisor,
    string TipoComprobante,        // texto tal cual del reporte: "Factura", "Nota de Crédito", ...
    string SerieComprobante,       // "001-012-024129728"
    string ClaveAcceso,            // 49 dígitos, validado
    DateTime FechaAutorizacion,    // trae hora
    DateOnly FechaEmision,
    string IdentificacionReceptor,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    string? NumeroDocumentoModificado);

public sealed record FilaConError(int NumeroFila, string Motivo);

public sealed record ResultadoParseoReporte(
    IReadOnlyList<ComprobanteSriReportado> Filas,
    IReadOnlyList<FilaConError> Errores);

public static class LectorReporteComprobantesSri
{
    private static readonly string[] EncabezadoEsperado =
    [
        "RUC_EMISOR", "RAZON_SOCIAL_EMISOR", "TIPO_COMPROBANTE", "SERIE_COMPROBANTE",
        "CLAVE_ACCESO", "FECHA_AUTORIZACION", "FECHA_EMISION", "IDENTIFICACION_RECEPTOR",
        "VALOR_SIN_IMPUESTOS", "IVA", "IMPORTE_TOTAL", "NUMERO_DOCUMENTO_MODIFICADO",
    ];

    /// <exception cref="FormatoReporteInvalidoException">
    /// El encabezado no matchea (portal cambió de formato) o el archivo está
    /// vacío — error de archivo completo, no de fila.
    /// </exception>
    public static ResultadoParseoReporte Parsear(string contenido, string rucEsperado) { /* ... */ }
}
```

- `Parsear` **no** hace I/O — toma el `string` ya decodificado, devuelve DTOs
  en memoria. 100% testeable sin servidor ni base de datos (mismo criterio
  de "lógica pura" que el resto de `PsaWeb.Conciliacion`, §4.4).
- **Encabezado**: se compara la primera línea, columna por columna, contra
  `EncabezadoEsperado` — si no matchea exacto, `FormatoReporteInvalidoException`
  con mensaje "El portal cambió el formato del reporte, avisar a soporte"
  (el endpoint la traduce a 400). Falla rápido y con un mensaje accionable en
  vez de intentar adivinar columnas corridas.
- **Archivo vacío** (solo encabezado, sin filas): válido, no es error —
  `Filas` vacío, significa "no hay comprobantes recibidos en el período/tipo
  consultado".

### 12.3 Validación fila por fila (no aborta el archivo completo)

Por cada línea de datos (separada por `\t`, se espera exactamente 12
campos):

| Campo | Regla | Si falla |
|---|---|---|
| `CLAVE_ACCESO` | regex `^\d{49}$` (mismo patrón que `SRIwebReadAccessKeys`, §2.1) | fila a `Errores`, se descarta |
| `FECHA_EMISION` | `DateOnly.ParseExact("dd/MM/yyyy", CultureInfo.InvariantCulture)` | fila a `Errores` |
| `FECHA_AUTORIZACION` | `DateTime.ParseExact("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)` (trae hora, confirmado en el archivo real §2.5) | fila a `Errores` |
| `VALOR_SIN_IMPUESTOS`/`IVA`/`IMPORTE_TOTAL` | `decimal.Parse(..., NumberStyles.AllowDecimalPoint \| NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)` — el archivo real trae valores sin cero inicial (`.29`), `InvariantCulture` ya lo resuelve sin tratamiento especial | fila a `Errores` |
| `NUMERO_DOCUMENTO_MODIFICADO` | opcional — vacío en facturas, poblado en NC (`SerieComprobante` de la factura original) | nunca falla, se guarda `null` si viene vacío |
| cualquier otro campo vacío/faltante (columnas de menos) | — | fila a `Errores`, "columna faltante" |

Una fila con error **no aborta el archivo** — se acumula en `Errores` y
sigue con la siguiente (mismo criterio que §4.2: `errores: [{fila, motivo}]`
en la respuesta). Con 400+ filas típicas de un mes (§2.5), una fila mal
formada no debe tirar todo el resto.

### 12.4 Validación de integridad a nivel de archivo — nueva, no estaba en §4.2

**Chequeo agregado en F2**: si alguna fila trae `IDENTIFICACION_RECEPTOR`
distinto del `ruc` que vino en el request del endpoint, **se rechaza el
archivo completo** (400, "el reporte no corresponde a la empresa indicada")
en vez de aceptarlo parcialmente. Motivo: `IDENTIFICACION_RECEPTOR` debería
ser siempre el RUC de la empresa logueada (así lo genera el portal), así que
un mismatch es señal de algo raro — la extensión mandó el `ruc` equivocado,
o la contadora estaba logueada en el cliente que no correspondía. Es más
seguro frenar ahí que guardar comprobantes bajo el RUC incorrecto y
descubrirlo recién al conciliar.

### 12.5 Upsert idempotente — comportamiento exacto

- Clave de unicidad: `(Ruc, ClaveAcceso)`.
- Si ya existe: **se ignora la fila entrante, no se pisa nada** (los datos
  de un comprobante autorizado por el SRI no cambian una vez emitido — no
  hay razón de negocio para sobrescribir; y así una re-subida accidental del
  mismo período nunca corrompe datos ya guardados).
- **Duplicados dentro del mismo archivo** (puede pasar — se vieron filas
  con la misma `CLAVE_ACCESO` en el reporte real, ver clientes como
  CONTECON con múltiples líneas de transporte): el upsert debe ser
  idempotente también fila-a-fila dentro de una sola subida, no solo entre
  subidas distintas — se cuenta como 1 en `nuevos`, no como error.
- El conteo `yaExistian` de la respuesta (§4.2) es simplemente
  "`ClaveAcceso` ya estaba en la tabla para ese `Ruc`" — no distingue si fue
  de una subida anterior o de una fila repetida en esta misma.

### 12.6 Lector de Set A

```csharp
public interface ILectorComprobantesSri
{
    Task<IReadOnlyList<ComprobanteSriGuardado>> ObtenerAsync(
        string ruc, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}
```

Implementación EF sobre `ConciliacionDbContext.ComprobantesSriDescargados`,
filtro `Ruc == ruc && FechaEmision >= desde && FechaEmision <= hasta`. Es la
pieza que el motor de conciliación (§4.4) consume como Set A — vive junto al
resto de `PsaWeb.Conciliacion`.

### 12.7 Fixture sintética para tests (§9) — contenido propuesto

Reemplaza al archivo real de CPTDC (que no se commitea, §9). Mismo formato
exacto, datos 100% inventados, cubre los casos de la tabla de arriba más los
de integración con Set B que se necesitarán en F3:

```
RUC_EMISOR	RAZON_SOCIAL_EMISOR	TIPO_COMPROBANTE	SERIE_COMPROBANTE	CLAVE_ACCESO	FECHA_AUTORIZACION	FECHA_EMISION	IDENTIFICACION_RECEPTOR	VALOR_SIN_IMPUESTOS	IVA	IMPORTE_TOTAL	NUMERO_DOCUMENTO_MODIFICADO
1791111111001	PROVEEDOR DEMO UNO S.A.	Factura	001-001-000000001	0109202601179111111100120010010000000011234567811	01/09/2026 09:00:00	01/09/2026	1799999999001	100	12	112	
1791111111001	PROVEEDOR DEMO UNO S.A.	Factura	001-001-000000002	0209202601179111111100120010010000000021234567812	02/09/2026 10:00:00	02/09/2026	1799999999001	50	6	56	
1791222222001	PROVEEDOR DEMO DOS CÍA. LTDA.	Nota de Crédito	001-002-000000001	0309202601179122222200120010020000000011234567813	03/09/2026 11:00:00	03/09/2026	1799999999001	10	1.2	11.2	001-001-000000001
1791333333001	SEÑORÍO Y ASOCIADOS S.A.S.	Factura	001-001-000000045	0409202601179133333300120010010000000451234567814	04/09/2026 08:30:00	04/09/2026	1799999999001	1.96	.29	2.25	
```

- Fila 1 y 2: van a "match exacto" con Sage en la fixture de Set B de F3
  (misma clave tecleada bien).
- Fila 1: además queda **sola en SRI** en otro escenario de test (si no se
  incluye la compra correspondiente en la fixture de Sage) — se reusa según
  el caso.
- Fila 3 (NC): prueba `NUMERO_DOCUMENTO_MODIFICADO` poblado, vinculado a la
  fila 1 por `SerieComprobante`.
- Fila 4: prueba decimales sin cero inicial (`.29`) y texto con tilde/Ñ.
- Se agregan además, solo como líneas de texto crudo en los tests unitarios
  (no en este archivo "limpio"): una fila con clave de 48 dígitos, una con
  letras en la clave, una con el encabezado alterado, y una con
  `IDENTIFICACION_RECEPTOR` distinto para probar el rechazo de §12.4 —
  no tiene sentido mezclarlas en la fixture "feliz" de arriba.

### 12.8 Tests de F2

- `LectorReporteComprobantesSriTests`: encabezado correcto → parsea todo;
  encabezado alterado → `FormatoReporteInvalidoException`; archivo vacío
  (solo encabezado) → 0 filas, sin error; clave de 48 dígitos → fila en
  `Errores`, resto del archivo se parsea igual; clave con letras → error;
  decimal sin cero inicial → parsea `0.29`; NC con
  `NUMERO_DOCUMENTO_MODIFICADO` → campo poblado; fila con
  `IDENTIFICACION_RECEPTOR` distinto del `rucEsperado` → todo el archivo
  rechazado; mismo `CLAVE_ACCESO` 2 veces en el archivo → 1 sola fila en el
  resultado.
- `LectorComprobantesSriTests` (EF, contra SQLite in-memory o LocalDB de
  test, patrón ya usado en el repo): filtro por RUC + rango de fechas trae
  solo lo esperado; upsert no duplica ni pisa en una segunda subida del
  mismo archivo.

---

## 13. F3 en detalle — Set B + motor de conciliación

### 13.1 Lector de Set B — no inventar una consulta de "total" nueva

Riesgo real identificado al diseñar esto: en un asiento de partida doble de
Sage, sumar `JrnlRow.Amount` de **todas** las líneas de un `PostOrder`
tendería a `0` (débitos y créditos se cancelan) — no da "el total de la
compra". Los lectores que ya existen en el repo (`LectorFacturaVenta`,
`LectorDetalleVentaAts`, etc.) lo resuelven filtrando con cuidado qué líneas
sumar (`RowType`, signo de `Amount`, categoría del ítem) — es exactamente el
tipo de detalle de Sage que ya rompió en silencio antes (`LaborCost`,
`InvariantCulture`, §3 de `ESTADO-MIGRACION-WEB.md`) y que la doctrina del
proyecto (§6) obliga a validar contra datos reales, no inventar a ciegas.

**Decisión: no escribir una consulta de total desde cero — reusar
`LectorDetalleComprasAts.LeerImponiblesAsync` del ATS**, que ya hace
exactamente este cálculo (bases imponibles + IVA de una compra por
`PostOrder`) y **ya está validado número por número contra los 2
entregables reales de la declaración de CPTDC** (`ESTADO-MIGRACION-WEB.md`
§5.5). Devuelve `BucketsComprasAts` (`BaseNoGraIva`, `BaseImponible`,
`BaseImpGrav`, `BaseImpExe`, `MontoIva`) — **sin ningún tipo del esquema del
ATS** (confirmado leyendo el archivo: la firma es
`Task<BucketsComprasAts> LeerImponiblesAsync(OdbcConnection, long postOrder, ...)`).
A partir de los buckets:

```
Subtotal = BaseNoGraIva + BaseImponible + BaseImpGrav + BaseImpExe   // = VALOR_SIN_IMPUESTOS del reporte SRI
Iva      = MontoIva                                                  // = IVA del reporte SRI
Total    = Subtotal + Iva                                            // = IMPORTE_TOTAL del reporte SRI
```

**Adición a la decisión #4** (que ya movía `LectorAuxiliarComprasAts`, §4.4):
se mueven también `LectorDetalleComprasAts.LeerImponiblesAsync` +
`BucketsComprasAts` + `ClasificadorLineasComprasAts` a
`PsaWeb.Comprobantes/Compras/` — mismo criterio (mover archivo, cero cambio
de lógica). `LeerRetencionesRentaAsync` (que sí devuelve el tipo del
esquema del ATS, `detalleAirComprasType[]`) se queda en `PsaWeb.Ats`, no
hace falta para conciliación. También se mueve `LectorComprasAts.EsAutoretencion`
(predicado de una línea, sin acceso a Sage) — Conciliación SRI necesita el
mismo filtro que el ATS: una compra marcada `ShipVia = "AUTORETENCION"`
(§5.5.1 de `ESTADO-MIGRACION-WEB.md`) no es una compra real a un tercero y
nunca va a aparecer en el reporte del SRI — sin excluirla, quedaría
mostrando "Solo en Sage" para siempre, ruido permanente para los Grandes
Contribuyentes que hacen autoretención.

Con esto, `LectorComprasParaConciliacion` (nuevo, en
`PsaWeb.Comprobantes/Compras/`) queda como una **cabecera + orquestación**,
no una consulta nueva de riesgo:

```csharp
public sealed record CompraSage(
    long PostOrder,
    string RucProveedor,
    string NombreProveedor,
    string Referencia,          // "001-012-024129728" (estab-ptoemi-secuencial)
    DateOnly Fecha,
    string Autorizacion,        // texto crudo de LectorAuxiliarCompras.NumeroAutorizacionAsync — sin normalizar
    decimal Subtotal,
    decimal Iva,
    decimal Total);

public static class LectorComprasParaConciliacion
{
    public static async Task<IReadOnlyList<CompraSage>> LeerAsync(
        OdbcConnection connection, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        // 1. Header: mismo WHERE que LectorComprasAts.Sql (JrnlHdr+Vendors,
        //    JrnlKey_Journal=Compras, Reference LIKE '___-___-%', filtro de
        //    período) menos las columnas específicas del ATS (ShipToAddress2/
        //    ShipToCity, que son de sustento tributario, no hacen falta acá).
        // 2. Filtrar autoretenciones (EsAutoretencion, movido junto).
        // 3. Por cada compra: LectorAuxiliarCompras.NumeroAutorizacionAsync
        //    (autorización) + LectorDetalleComprasAts.LeerImponiblesAsync
        //    (montos) + LectorProveedor (RUC/nombre, ya compartido).
    }
}
```

### 13.2 Normalizar la clave de autorización antes de comparar — sin "arreglarla"

El campo de autorización de Sage (§2.3) es texto libre tecleado a mano —
antes de comparar contra `ClaveAcceso` del SRI (49 dígitos limpios) se le
aplica **solo** `Trim()` (sacar espacios accidentales al principio/final).
**No** se intenta corregir nada más (no se rellena con ceros, no se
recortan caracteres) — cualquier otra discrepancia (clave incompleta, con un
dígito de más, con letras) debe caer en la salida 2 tal cual está, porque
**es justamente el error que el módulo existe para atrapar** — "arreglarla"
en el motor escondería el problema real en vez de mostrarlo.

### 13.3 El join y las salidas 1-4 (instantáneas, sin WS)

```csharp
public enum ClasificacionConciliacion
{
    SoloEnSri, SoloEnSage, ValoresDistintos, MetadataDistinta,
    CoincidePendienteDeVerificar,   // ver §13.4 — candidato a salida 5
}

public sealed record FilaConciliacion(
    string? ClaveAcceso,
    ComprobanteSriGuardado? Sri,
    CompraSage? Sage,
    ClasificacionConciliacion Clasificacion,
    IReadOnlyList<string> Diferencias);   // ej. "Total: SRI 112.00 vs Sage 110.00"

public static class MotorConciliacion
{
    public static IReadOnlyList<FilaConciliacion> Conciliar(
        IReadOnlyList<ComprobanteSriGuardado> setA,
        IReadOnlyList<CompraSage> setB,
        decimal toleranciaMontos = 0.01m)
    { /* full outer join por ClaveAcceso (Sri) vs Autorizacion.Trim() (Sage) */ }
}
```

Algoritmo: diccionario de Set B por `Autorizacion.Trim()` (solo las no
vacías participan del join — una compra con autorización vacía nunca puede
matchear, va directo a salida 2). Por cada fila de Set A: si hay match,
comparar `Subtotal`/`Iva`/`Total` con la tolerancia (`Math.Abs(diff) >
toleranciaMontos` → salida 3) y, si los montos coinciden, comparar
`FechaEmision`/`RucEmisor`/`TipoComprobante` (→ salida 4 si difieren, o
`CoincidePendienteDeVerificar` si todo coincide); si no hay match, salida 1.
Lo que sobra en Set B después de recorrer Set A: salida 2 (distinguir clave
vacía vs. clave tecleada sin match, §4.4 refinamiento).

**Tolerancia de montos: `$0.01`**, comparada por separado en `Subtotal`,
`Iva` y `Total` (no solo en el total) — para atrapar un error de digitación
en el desglose aunque el total final cierre por casualidad. Fijo por ahora,
no configurable por el usuario en el primer corte (se agrega si hace falta
después).

### 13.4 La salida 5 (anulados) — verificación automática, no solo manual

**Revisado con el usuario (2026-09-17): un botón manual no alcanza —
depender de que alguien se acuerde de apretarlo es justamente el tipo de
paso que se termina omitiendo.** La primera versión de este detalle
proponía solo un botón masivo bajo demanda; se cambia por un **worker
periódico automático** (se reintroduce lo que §2.5/§4.4 había mandado a
backlog — pero acotado únicamente a verificar estado, no a la hidratación
de montos que ya no hace falta, para no confundir con el worker que se
sacó del diseño).

**Por qué hace falta algo que dispare solo**: el reporte no trae estado
(§2.5) y Sage tampoco sabe si el SRI anuló un comprobante — la única forma
de detectar "anulado en SRI, no anulado en Sage" es preguntarle al WS. Las
filas candidatas a salida 5 son `CoincidePendienteDeVerificar` —
**exactamente las que el join ya considera "todo bien"** (no caen en 2/3/4,
que sí se verifican solas). Sin un disparador automático, un comprobante
bien tecleado y con montos exactos pero anulado en el SRI se vería para
siempre como "todo bien".

**Diseño: `VerificacionEstadoSriWorker`** (`IHostedService`/`PeriodicTimer`,
mismo patrón que `RetencionesWorker` — ver §3 de `ESTADO-MIGRACION-WEB.md`):

- Corre **cross-company** (igual que `RetencionesWorker`): por cada empresa
  activa (`EmpresasActivasAsync`, ya existe en `PsaWeb.Modules.Retenciones/
  Data/PendientesRepository` — se reusa el mismo patrón/consulta, no se
  duplica), abre la conexión Sage de esa empresa
  (`PeachConnStringResolver`+`ISageConnectionFactory.CreateConnection(str)`,
  igual que `ProcesadorRetenciones`) y corre la conciliación (§13.3) sobre
  una ventana móvil razonable — **últimos 90 días de `FechaEmision`**, no
  todo el histórico, para acotar el trabajo — para saber qué filas son
  `CoincidePendienteDeVerificar`.
- De esas, verifica solo las que tienen `FechaVerificacionEstado` nula o
  más vieja que un umbral (**7 días**, mismo criterio que ya estaba
  pensado para el botón manual) — evita re-verificar de más.
- Concurrencia acotada (`SemaphoreSlim`, ej. 5 en simultáneo) para no
  saturar el WS público del SRI; degrada por empresa (si el Sage de una
  empresa no responde, sigue con las demás — mismo patrón `Fallo(...)` de
  `ProcesadorRetenciones`).
- Guarda `Estado`+`FechaVerificacionEstado` por fila, igual que el diseño
  anterior. Las que el WS confirma "NO AUTORIZADO"/anulado pasan a
  mostrarse como salida 5 de verdad.
- **A diferencia de `RetencionesWorker`, arranca `Habilitado=true` por
  defecto** (no hace falta el mismo apagador de cautela): este worker
  nunca emite nada ni escribe en Sage/Datil, solo lee un WS público y
  actualiza 2 columnas de la BD propia del módulo — el peor caso ante un
  problema es que tarde o falle y reintente, sin nada que revertir (mismo
  razonamiento que ya se había usado para el worker de hidratación que
  se descartó, §10 decisión #8 — ahí no aplicaba porque el worker entero
  dejó de existir; acá sí aplica, para este worker nuevo).
- Config nueva: `Conciliacion:Worker:Intervalo` (default razonable, ej. 6
  u 8 horas — no hace falta más seguido, el umbral de 7 días de
  re-verificación ya limita el trabajo real por corrida),
  `Conciliacion:Worker:VentanaDias` (default 90).

**El botón individual "Verificar con el SRI" y el botón masivo "Verificar
pendientes (N)" siguen existiendo** (§7) — para cuando la contadora quiere
una confirmación inmediata sin esperar a la próxima corrida del worker
(ej. justo antes de cerrar el mes). El worker es la garantía de fondo; los
botones son para el apuro puntual — no se reemplazan entre sí.

**Aclaración importante — qué automatiza el worker y qué NO** (para no dar
una falsa sensación de "esto ya no necesita a la contadora"): son dos pasos
distintos con dependencias distintas.

1. **Traer la lista de comprobantes del SRI a Set A** — esto **siempre**
   depende de la contadora. No hay API oficial de descarga masiva (§1); el
   worker **no** navega el portal, no hace login, no descarga nada del SRI
   en nombre de nadie — solo trabaja con lo que ya está en
   `ComprobantesSriDescargados`, subido antes por la extensión (§11). Si la
   contadora deja de correr la extensión, el staging queda desactualizado y
   el worker no tiene forma de enterarse de que faltan comprobantes nuevos
   — no hay ninguna automatización posible acá, es la limitación de fondo
   del módulo (§1: "no hay API oficial de descarga masiva del SRI").
   Recordar además el límite de 5 días del propio reporte (§4.1/§4.4) — la
   contadora tiene que correr la extensión seguido durante el mes, no una
   sola vez al cierre.
2. **Verificar el estado de los comprobantes que ya están guardados** — esto
   **sí** lo hace el worker completamente solo: llama directo al web
   service público del SRI (`ConsultaComprobante`, servidor-a-servidor
   desde el Host, sin login, §4.5) con la `ClaveAcceso` que ya tiene
   guardada. Ninguna intervención de la contadora ni de la extensión hace
   falta para esta parte — es exactamente lo que resuelve el riesgo de
   omisión que se planteó.

En criollo: el worker garantiza que **lo que ya se subió** se revisa solo;
no garantiza que **se siga subiendo** — eso lo sigue haciendo la contadora.

### 13.5 Tests de F3

- `LectorComprasParaConciliacionTests`: contra Sage real de CPTDC (fixture
  §9) — validar que `Subtotal+Iva == Total` con tolerancia, y que una
  compra `ShipVia = "AUTORETENCION"` no aparece en el resultado.
- `MotorConciliacionTests` (con listas en memoria, sin Sage/BD — es lógica
  pura): match exacto → `CoincidePendienteDeVerificar`; solo en SRI; solo en
  Sage con autorización vacía; solo en Sage con autorización tecleada sin
  match; delta de un centavo → dentro de tolerancia, no marca salida 3;
  delta de un dólar → salida 3 con el detalle del campo que difiere; mismo
  monto, RUC emisor distinto → salida 4; autorización con espacios
  (`" 0108...115 "`) → matchea igual tras `Trim()`.

---

## 14. F4 en detalle — clientes SOAP + worker + página

### 14.1 Cliente `ConsultaComprobante` — forma real confirmada

**Probado en F3 (2026-09-17) contra `cel.sri.gob.ec` (producción) con una
clave de acceso real** (de un comprobante ya conocido del reporte de CPTDC —
no se pega esa clave ni el RUC/nombre reales acá, mismo criterio de no
commitear datos de terceros que ya se aplicó en §9; se muestra con una clave
de ejemplo). Hasta F0 solo se habían visto respuestas de **error** (clave
inválida / no encontrada, §4.5) — esta es la primera confirmación de la
forma de una respuesta **exitosa**, y trae una diferencia importante
respecto a lo asumido: **el campo `estadoConsulta` no aparece en absoluto
cuando el comprobante es válido** (antes se pensaba que traería algo tipo
`"EXITOSA"` — en realidad el campo directamente no está presente, solo
aparece en los casos de error):

```xml
<!-- Respuesta real (clave de ejemplo, no la real usada en la prueba) -->
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body>
  <ns2:consultarEstadoAutorizacionComprobanteResponse xmlns:ns2="http://ec.gob.sri.ws.consultas">
    <EstadoAutorizacionComprobante>
      <claveAcceso>0108202601179128754100120010120241297281526111115</claveAcceso>
      <mensajes/>
      <estadoAutorizacion>AUTORIZADO</estadoAutorizacion>
      <tipoComprobante>Factura</tipoComprobante>
      <rucEmisor>1791287541001</rucEmisor>
      <fechaAutorizacion>2026-08-01T03:24:52-05:00</fechaAutorizacion>
    </EstadoAutorizacionComprobante>
  </ns2:consultarEstadoAutorizacionComprobanteResponse>
</soap:Body></soap:Envelope>
```

Nota: `fechaAutorizacion` viene en ISO 8601 con offset (`-05:00`) en la
respuesta del WS — distinto del formato `dd/MM/yyyy HH:mm:ss` del reporte
descargado (§12.3). Se parsea con `DateTimeOffset.Parse` (o
`DateTime.Parse` con `DateTimeStyles.RoundtripKind`), no con el mismo
`ParseExact` del reporte — son dos formatos de fecha distintos, de dos
fuentes distintas.

**Contrato del cliente**:

```csharp
public enum EstadoComprobanteSri { Autorizado, NoAutorizado, FormatoInvalido, NoEncontrado, ErrorServicio }

public sealed record ResultadoVerificacionEstado(EstadoComprobanteSri Estado, string? MensajeSri);

public interface IVerificadorEstadoSri
{
    Task<ResultadoVerificacionEstado> VerificarAsync(string claveAcceso, CancellationToken ct = default);
}
```

Mapeo de la respuesta real al enum:

| Respuesta del WS | `EstadoComprobanteSri` |
|---|---|
| `estadoAutorizacion = "AUTORIZADO"` (sin `estadoConsulta`) | `Autorizado` |
| `estadoAutorizacion` presente con otro valor | `NoAutorizado` — **valor exacto para un comprobante anulado sin confirmar todavía** (no se probó un caso real anulado en F0/F3; por seguridad, cualquier valor que no sea exactamente `"AUTORIZADO"` se trata como no vigente en vez de asumir que solo existe un valor de "anulado" — se ajusta si aparece un caso real distinto al implementar) |
| `estadoConsulta = "RECHAZADA"` + mensaje "formato de la clave" | `FormatoInvalido` |
| `estadoConsulta = "RECHAZADA"` + mensaje "no existen datos" | `NoEncontrado` |
| timeout / error de transporte / XML no parseable | `ErrorServicio` (reintentable — no es una respuesta del SRI, es un problema de conectividad) |

- **Vive en `PsaWeb.Conciliacion`** (no se extrae a `PsaWeb.Comprobantes` —
  a diferencia de los lectores de compras, hoy no hay un segundo consumidor
  real para este cliente; se mueve el día que aparezca uno, mismo criterio
  que ya se usó para no over-extraer de entrada).
- HTTP crudo con `IHttpClientFactory` (`AddHttpClient<IVerificadorEstadoSri, ConsultaComprobanteClient>`),
  timeout corto (ej. 10s — es un WS público, no hay que dejar que un
  comprobante cuelgue todo el worker), sin reintentos automáticos dentro del
  cliente (el reintento lo maneja el worker en la corrida siguiente, más
  simple que reintentar en el momento).

### 14.2 `VerificacionEstadoSriWorker` — implementación

```csharp
public sealed record ResumenVerificacionEmpresa(string Ruc, int Verificados, int Anulados, int ConError);
public sealed record ResumenVerificacionCorrida(
    DateTime Fecha, IReadOnlyList<ResumenVerificacionEmpresa> PorEmpresa, int EmpresasOmitidas);
```

- `EmpresasActivasAsync` — hoy vive en
  `PsaWeb.Modules.Retenciones/Data/PendientesRepository` (§F-Shell/ESTADO-
  MIGRACION-WEB.md). **Se extrae a `PsaWeb.PeachEbills`** (no tiene sentido
  que el módulo de Conciliación dependa del módulo de Retenciones solo para
  listar empresas activas — mismo criterio de extracción que ya se aplicó
  con los lectores de compras, §13.1: cuando aparece un segundo consumidor
  real de una pieza que vive en un módulo, se sube al nivel compartido).
  `PsaWeb.Modules.Retenciones` pasa a referenciar la versión movida — cero
  cambio de comportamiento.
- Ciclo del worker (`PeriodicTimer`, intervalo `Conciliacion:Worker:Intervalo`,
  default 8h):
  1. `EmpresasActivasAsync()`.
  2. Por empresa: abrir conexión Sage (`PeachConnStringResolver` +
     `ISageConnectionFactory.CreateConnection(str)`, igual que
     `ProcesadorRetenciones`) → si falla, `EmpresasOmitidas++`, log, sigue
     con la siguiente (degrada por empresa, no aborta la corrida — mismo
     patrón `Fallo(...)` de Retenciones).
  3. Leer Set A (`ILectorComprobantesSri.ObtenerAsync`, ventana
     `Conciliacion:Worker:VentanaDias`, default 90) + Set B
     (`LectorComprasParaConciliacion.LeerAsync`, misma ventana).
  4. `MotorConciliacion.Conciliar(...)` → filtrar `CoincidePendienteDeVerificar`
     con `FechaVerificacionEstado` nula o de hace más de 7 días.
  5. Verificar esas filas con `IVerificadorEstadoSri`, concurrencia acotada
     — **`SemaphoreSlim` global para toda la corrida** (no por empresa): el
     límite es sobre cuántas llamadas simultáneas le llegan al WS del SRI en
     total, no por empresa (si hubiera 20 empresas activas a la vez, 20
     semáforos independientes de 5 igual saturarían el WS con hasta 100
     llamadas simultáneas).
  6. Guardar `Estado`+`FechaVerificacionEstado` por fila verificada.
     `EstadoComprobanteSri.NoAutorizado` → esa fila ahora clasifica como
     salida 5 real en la próxima carga de la página (el motor lee
     `Estado` guardado, no vuelve a llamar al WS para las ya verificadas
     recientemente).
  7. Acumular `ResumenVerificacionEmpresa`/`ResumenVerificacionCorrida` y
     loguearlo (mismo estilo que el log de `RetencionesWorker`: "Corrida
     automática: N empresas · M verificados · K anulados · E con error").
- **`Habilitado=true` por defecto** (§13.4, ya decidido) —
  `Conciliacion:Worker:Habilitado` existe igual como bandera de apagado de
  emergencia (ej. si el WS del SRI empieza a devolver error 500 en masa),
  pero arranca prendido.

### 14.3 Módulo, página, catálogo

- `modules/PsaWeb.Modules.ConciliacionSri` (RCL, patrón estándar del repo,
  `docs/COMO-MIGRAR-UN-APLICATIVO.md`): `ModuleInfo`, `_Imports.razor`,
  `AddConciliacionSri(config)` — registra `IVerificadorEstadoSri`,
  `VerificacionEstadoSriWorker` (`AddHostedService`), y el `EjecucionGate`
  de single-flight para el botón masivo "Verificar pendientes" (mismo
  patrón que `EjecucionRetencionesGate` — evita que el botón manual y el
  worker corran la verificación al mismo tiempo sobre las mismas filas).
- Página `Pages/ConciliacionSri.razor` (`@page "/conciliacion-sri"`):
  filtro Desde/Hasta, 6 pestañas (§7), botón "Verificar con el SRI" por
  fila + botón masivo "Verificar pendientes (N)", acotada a empresa+
  ambiente de sesión (reacciona a `EmpresaActual.Cambio`).
- `Permisos.VerConciliacionSri = "quconcsri"` (§6). `AppCatalogo.Todas`:
  nueva fila `("conciliacion-sri", "Conciliación SRI", "Concilia los
  comprobantes recibidos del SRI contra Sage 50.", "🔎", "/conciliacion-sri",
  Categorias.Impuestos, Array.Empty<string>())` — `GateProvisional`, mismo
  comentario que las demás filas provisionales (§6).
- Doble registro de rutas: `Routes.razor` (`AdditionalAssemblies`) +
  `Program.cs` (`MapRazorComponents<App>().AddAdditionalAssemblies(...)`),
  más el endpoint de subida de F1 (`/conciliacion-sri/api/comprobantes`) y
  `builder.Services.AddConciliacionSri(builder.Configuration)`.

### 14.4 Tests de F4

- `ConsultaComprobanteClientTests`: mapeo de cada fila de la tabla de §14.1
  (con respuestas XML de ejemplo grabadas, no contra el WS real en los
  tests automatizados — el WS real ya se probó a mano en esta sesión);
  timeout → `ErrorServicio`.
- `VerificacionEstadoSriWorkerTests`: con un `IVerificadorEstadoSri` fake y
  un factory de Sage que falla para una empresa — la corrida sigue con las
  demás y cuenta `EmpresasOmitidas`; una fila verificada hace menos de 7
  días no se vuelve a verificar; concurrencia respeta el límite del
  semáforo (test con un fake que cuenta llamadas simultáneas).
- Smoke test de la página en PREDATOR: login → empresa CPTDC →
  `/conciliacion-sri` → las 6 pestañas cargan, filtros funcionan, botón
  individual y masivo disparan la verificación — contra **producción**
  (`cel.sri.gob.ec`, no `celcer.sri.gob.ec`/pruebas), porque son claves de
  comprobantes reales ya conocidas del reporte de CPTDC (§9) y el ambiente
  de pruebas no tiene datos reales.

---

## 15. F5 en detalle — distribución de la extensión + deploy

### 15.1 Distribución de la extensión — empezar simple, con ruta de escalamiento

**Decisión: arrancar con unpacked + modo desarrollador**, no política
empresarial desde el día 1 — para 1-2 contadoras es la opción de menor
esfuerzo de infraestructura (cero servidor de por medio) y coherente con el
criterio ya usado en otras decisiones de este plan (ej. decisión #2, token
simple en vez de certificado — no sobre-diseñar para la escala actual):

- Empaquetado: **no hace falta build step** — la extensión es HTML/JS/CSS
  vanilla (§11.2, sin frameworks), así que "empaquetar" es literalmente
  zippear la carpeta `extension/`.
- Instalación: `chrome://extensions` → activar "Modo de desarrollador" →
  "Cargar descomprimida" → apuntar a la carpeta. Una sola vez por PC.
- Actualización: cuando cambie el content script (ej. el portal movió el
  botón), se reemplaza el contenido de la carpeta y se aprieta "Actualizar"
  en `chrome://extensions` — no hace falta reinstalar.

**Molestia conocida de este modo, a anticipar**: Chrome deshabilita
extensiones "sin empaquetar" cada vez que se reinicia si el modo
desarrollador está apagado, y muestra un aviso de seguridad recurrente. Es
molesto pero no bloqueante para 1-2 PCs ya identificadas.

**Ruta de escalamiento** (no se implementa ahora, se deja documentada para
cuando haga falta — más contadoras, más PCs, o la molestia del aviso se
vuelve un problema real): distribución vía **política de Chrome
(`ExtensionInstallForcelist`/`ExtensionSettings`)** apuntando a un
`update.xml` + el `.crx` firmado, ambos hosteados en el mismo Host de PSA
(ej. `/extension/update.xml`). Con esto Chrome instala y **actualiza solo**
la extensión, sin modo desarrollador ni intervención manual — encaja bien
con que el content script "se va a romper sin aviso" (§4.1): un fix se
publica y llega solo, sin depender de que cada contadora reinstale a mano.
Requiere generar una clave `.pem` una sola vez (la identidad/ID de la
extensión depende de esa clave — perderla obliga a reinstalar en cada PC).

### 15.2 Manejo de errores de la extensión — catálogo de mensajes

Mensajes concretos para cada falla (§4.1 ya pedía esto en general; acá se
fijan los textos):

| Situación | Mensaje (notificación de Chrome) |
|---|---|
| No hay token guardado | "Configurá tu token de PSA en el ícono de la extensión." |
| El portal cambió (no se encuentra el botón/`ViewState`) | "El portal del SRI cambió de formato — avisar a soporte." |
| El Host de PSA no responde (red/servidor caído) | "No se pudo conectar con PSA. Reintentá en unos minutos." |
| Token inválido o revocado (401 del endpoint) | "Tu token ya no es válido — generá uno nuevo en Mi cuenta." |
| RUC no autorizado para ese usuario (403) | "Esta empresa no está habilitada para tu usuario en PSA." |
| Todo OK | "Subido: N nuevos, M ya existían" (§4.2) |

Todo esto envuelto en `try/catch` en el content script — nunca una
excepción sin manejar que deje a la usuaria sin ninguna explicación.

### 15.3 Deploy del módulo — qué es nuevo respecto a los deploys anteriores

A diferencia de ATS/FE (que no necesitaron nada nuevo de base de datos,
`docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md` §3), Conciliación SRI sí trae
esquema nuevo:

- **Decisión: `TokensExtension` y `ComprobantesSriDescargados` viven en la
  misma base física `PsaWebPlataforma`** (mismo `Plataforma__ConnectionString`
  que ya tiene el sitio) — no se provisiona una tercera base para esto. Son
  dos `DbContext` distintos (`PlataformaDbContext` para Identity+tokens,
  `ConciliacionDbContext` nuevo para el staging) apuntando a la misma BD:
  EF Core no exige que una base tenga un solo `DbContext` dueño, y mantener
  una sola base física es menos para respaldar/monitorear en el server que
  provisionar una nueva. `ConciliacionDbContext` necesita su propia
  `ConciliacionDbContextFactory` de diseño (mismo patrón que
  `PlataformaDbContextFactory`, §F-Shell-0 de `ESTADO-MIGRACION-WEB.md`).
- Migraciones nuevas a aplicar: la de `TokensExtension` (sobre
  `PlataformaDbContext`) y la de `ComprobantesSriDescargados` (sobre
  `ConciliacionDbContext`) — ambas corren solas al arrancar en Producción
  (mismo mecanismo ya existente, `Plataforma:MigrarAlArrancar`, §F-Shell-4
  deploy-prep de `ESTADO-MIGRACION-WEB.md`) — no hace falta un paso manual
  de SQL en el runbook, a diferencia de cuando se creó `PsaWebPlataforma`
  la primera vez.
- **Nada de IIS nuevo**: mismo sitio `CierreDeCaja`, mismo puerto 8088,
  mismo app pool — es un redeploy más, patrón idéntico a
  `docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md` (parar el **pool**, no solo
  el sitio — el gotcha de `Always Running` documentado en
  `ESTADO-MIGRACION-WEB.md` §7).
- Sin variables de entorno nuevas obligatorias — el worker usa sus propios
  defaults (`Habilitado=true`, `Intervalo=8h`, `VentanaDias=90`); solo hace
  falta setear algo en `web.config` si se quiere cambiar esos valores o
  apagar el worker de emergencia.
- Runbook nuevo a escribir al implementar:
  `docs/DESPLEGAR-APP4-CONCILIACION-SRI-EN-SERWEBPSA01.md` (mismo patrón
  delta que los anteriores; entrega como siempre: un zip + un solo script
  de PowerShell listo para correr, sin explicación de más).

### 15.4 Pendiente del área (no bloquea F5)

- Cargar `quconcsri` en `allowAction`/`adrAllowRol` cuando corresponda —
  mismo estado provisional que nació `quKardex`/`qupurchliq`/`quats` (§6).
- Confirmar con la contadora el PC(s) donde se va a instalar la extensión
  antes de armar el paquete de F5.

### 15.5 Checklist de F5

- [ ] Extensión empaquetada (zip de la carpeta, sin build).
- [ ] Instalada y probada en al menos un PC real, contra el portal en vivo,
      con una empresa real (CPTDC).
- [ ] Los 6 mensajes de error de §15.2 provocados a propósito una vez cada
      uno (token faltante, portal cambiado simulado, Host caído, token
      revocado, RUC no autorizado, caso feliz).
- [ ] Migraciones de `PsaWebPlataforma`/`ConciliacionDbContext` aplicadas en
      producción sin intervención manual.
- [ ] Worker corriendo en producción (`Habilitado=true`), log confirmando
      al menos una corrida.
- [ ] Runbook de deploy escrito y ejecutado en `SERWEBPSA01`.
- [ ] Smoke test final: subir un reporte real → aparece en Set A → concilia
      contra Sage real de CPTDC → botones de verificación funcionan.

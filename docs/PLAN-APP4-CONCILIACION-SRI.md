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
[Contadora] --login manual--> [Portal SRI en Línea]
     |                              |
     | (extensión Chrome activa    | (detecta login, navega a
     |  tras detectar login)       |  "Comprobantes recibidos",
     |                              |  filtra fechas, 1 click en
     |                              |  "Descargar reporte" — §2.4)
     v                              v
[Extensión] --POST reporte(.txt)+RUC--> [Host: endpoint de subida]
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
                                       v (solo para las filas
                                       sospechosas o el botón manual)
              [Re-verificación puntual: WS SOAP ConsultaComprobante /
               AutorizacionComprobantesOffline por CLAVE_ACCESO — §4.5]
                                       |
                                       v
                        [/conciliacion-sri — página del módulo]
```

La extensión ya no descarga ni sube XML individuales — solo **un archivo de
reporte por corrida** (§2.4/§2.5), que además ya trae los montos. El WS SOAP
ya no es un paso obligatorio del alta de cada comprobante — queda reservado
para lo único que el reporte no trae (estado/anulado), consultado bajo
demanda, sin depender del navegador de la contadora ni de que el portal se
preste a scraping fila por fila.

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
    acá. Se llega navegando por el link del menú del shell (no armando la
    URL directo), porque el shell le pasa a esa pantalla un token de sesión
    de un solo uso por query string — igual que le pasa a cualquier usuaria.
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
- **Filtro de tipo de comprobante**: el reporte de prueba se bajó con el
  filtro en "Factura" únicamente (no "Todos") — para cubrir NC/retenciones/
  liquidaciones hace falta correr con "Todos" (si el portal lo soporta en un
  solo archivo) o repetir la descarga por tipo. Se confirma al automatizar
  el selector en F1, no cambia el diseño.
- **Filtro de fechas**: rango configurable en la UI de la extensión (popup),
  default = mes en curso.
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
    no un reemplazo.
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
- No hace falta ningún `IHostedService`/worker de background para el MVP —
  se simplifica bastante F2 respecto a la primera versión del plan. Un
  worker que reverifique estados periódicamente (para detectar anulaciones
  sin que nadie abra la pantalla) queda en backlog, igual que
  `CorridasConciliacion` (§4.3).

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
- 5 secciones/pestañas, una por salida de la clasificación, cada una con su
  propio contador en el título (ej. "Solo en Sage (12)"). La de "Solo en
  Sage" lleva una nota visible aclarando que puede ser por clave no
  digitada, no solo por comprobante inválido (§2.3).
- Contador aparte, fuera de las 5 salidas, para filas del staging
  **todavía sin hidratar** (§4.4 — recién subidas por la extensión, el
  worker no llegó a traer su XML): "N comprobantes esperando datos del SRI"
  — no cuentan como discrepancia de ningún tipo hasta que se hidraten.
- Nota operativa visible (no solo en este doc): recordar que el listado de
  "recibidos" del portal solo cubre bien los últimos 5 días (§2.4/§4.1) —
  la pantalla puede mostrar cuándo fue la última subida por empresa para que
  la usuaria sepa si le conviene correr la extensión de nuevo antes de
  confiar en el resultado.
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

- **F1 — extensión mínima + endpoint de subida**: manifest v3 + detección de
  login (navegación a `/sri-en-linea/contribuyente/perfil`, §2.5) + navegar
  a "Comprobantes electrónicos recibidos" por el link del menú + replicar el
  POST del formulario (`fetch` con `javax.faces.ViewState`, §4.1) con un
  rango de fechas fijo (sin UI pulida) → POST del reporte al endpoint nuevo
  con token de API. `PsaWeb.Identidad`: generación/validación de token,
  pantalla `/mi-cuenta/extension`. `ConciliacionDbContext` + migración
  (`ComprobantesSriDescargados`, `TokensExtension`).
- **F2 — parseo del reporte + Set A** (más chica que en la versión anterior
  del plan — sin worker de hidratación): `LectorReporteComprobantesSri`
  (decodifica ISO-8859-1, parsea las 12 columnas, valida `CLAVE_ACCESO` de
  49 dígitos) en `PsaWeb.Conciliacion`. Lector de Set A sobre el staging.
  Tests con el archivo real de CPTDC guardado como fixture (§9) — ya
  disponible, no hace falta esperar a nada para escribir estos tests.
- **F3 — Set B + motor**: mover `LectorAuxiliarComprasAts` a
  `PsaWeb.Comprobantes/Compras/` + escribir `LectorComprasParaConciliacion`
  (§4.4). `MotorConciliacion` con las 5 clasificaciones + tests (casos:
  match exacto, solo SRI, solo Sage —con y sin clave tecleada—, delta de
  valores, delta de metadata, con tolerancia de redondeo).
- **F4 — cliente SOAP de re-verificación + página**: cliente
  `ConsultaComprobante` (§4.5, para el botón "Verificar con el SRI" y las
  salidas 2/3/4/5) — es el único cliente SOAP que hace falta para el MVP.
  Módulo `PsaWeb.Modules.ConciliacionSri`, página `/conciliacion-sri` (§7),
  gate `quconcsri` (§6), fila en `AppCatalogo.Todas`, doble registro de
  rutas.
- **F5 — pulido de la extensión + deploy**: manejo de errores visible en la
  extensión (§4.1), distribución interna, empaquetado; deploy del módulo
  siguiendo el patrón delta de `docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md`
  (redeploy del mismo sitio IIS, sin BD/IIS nuevos salvo la migración de
  `PsaWebPlataforma`).

Estimado grueso: **7-11 días de dev** (bajó de la estimación anterior —
9-14 días — al caerse el worker de hidratación completo de F2: ya no hay
que portar `ComprobanteE`/`XmlReader`, ni escribir un cliente SOAP que traiga
XML completo, ni el `IHostedService` que lo orquestaba). Sigue dominado por
F1 (primera pieza de infraestructura nueva del repo — token de API — y el
content script de la extensión).

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
   (§2.5, 2026-09-17): no hay ningún worker de hidratación en el diseño
   final — el reporte real ya trae los montos, así que no hace falta
   background service para completar Set A. La única llamada al WS
   (`ConsultaComprobante`, verificación de estado) es bajo demanda desde la
   página o el botón "Verificar con el SRI" — no hay nada que encender/
   apagar. Se guarda esta entrada en el historial del plan para que quede
   registrado por qué el diseño cambió, no como una decisión pendiente.

**Con esto, las 8 decisiones originales del plan quedan cerradas** (una de
ellas, la 8, terminó no aplicando al diseño final). **F0 está 100% cerrada**
(§2.5, §8) — no queda nada de investigación pendiente, ni de diseño ni de
portal, para arrancar F1.

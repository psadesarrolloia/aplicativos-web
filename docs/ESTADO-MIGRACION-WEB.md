# Estado de la migración web PSA (al 2026-09-15; §5.6 y §9 al 2026-09-21)

Documento de referencia único para arrancar cualquier trabajo nuevo sobre este
repo sin tener que releer todo el historial. Complementa (no reemplaza) a
`docs/COMO-MIGRAR-UN-APLICATIVO.md` (la receta paso a paso para portar un
módulo nuevo) y a los planes/runbooks individuales listados en §8.

**Terminología**: cada aplicativo de escritorio que se porta a la web se
llama **módulo** (no "aplicación" ni "app") — así se lo nombra en el menú, el
dashboard y cualquier texto de cara al usuario. "Aplicativo" queda reservado
para los 26 programas de escritorio originales (el punto de partida de cada
migración).

## 1. Qué es esto

PSA tiene 26 aplicativos de escritorio (.NET Framework, WinForms) que leen y
escriben contra **Sage 50 US** (Peachtree, motor Pervasive/Actian Zen vía
ODBC) y, algunos, contra una base SQL Server compartida **`PeachEBills`**.
Este repo (`aplicativos-web`, https://github.com/psadesarrolloia/aplicativos-web)
es la reescritura de esos aplicativos como **un solo sitio web** (Blazor
Server, .NET 9) con un **módulo por aplicativo**, empezando por los de solo
lectura.

Proyecto **separado** del re-theme de WinForms (`sage50Apps-master`, repo
`Interfaz Gráfica Aplicativos PSA` — memoria `sage50-redesign-status`): ese es
un lavado de cara visual del `.exe` existente, este repo es una reescritura
completa de la capa de datos y UI.

## 2. Dónde está todo

- **Repo**: `C:\PROYECTOS IA\Aplicativos web` en PREDATOR (esta máquina), clon
  de `https://github.com/psadesarrolloia/aplicativos-web`, rama `main`.
- **Fuente de los `.exe` originales** (solo para leer, no se edita):
  `C:\PROYECTOS IA\Interfaz Gráfica Aplicativos PSA\Código fuente Aplicativos\sage50Apps-master`.
- **PREDATOR** (Windows 10 Home, subred Wi-Fi `192.168.100.x`) es la máquina de
  desarrollo. **No tiene ruta a `192.168.0.x`** → no puede llegar al server de
  producción ni a su SQL Server. Todo el desarrollo/test corre contra bases
  **locales** (Sage 50 de empresas instaladas en PREDATOR + una copia
  restaurada de `PeachEBills` en `PREDATOR\SQLEXPRESS`).
- **`SERWEBPSA01`** (`192.168.0.11`) es el server de producción: corre el
  sitio IIS del web (`http://192.168.0.11:8088/`) y el `PeachEBills` real
  (SQL Server, login `Ebillsddladmin`). El deploy se arma en PREDATOR (zip) y
  se copia a mano (USB / recurso compartido) a `C:\Deploy` del server, donde
  se corre un script PowerShell — ver §7.
- El driver Pervasive ODBC es de **32 bits**: todo lo que hable ODBC con Sage
  tiene que correr x86. Por eso `PsaWeb.Host` fuerza `<PlatformTarget>x86</PlatformTarget>`.

## 3. Arquitectura

- **ASP.NET Core 9 + Blazor Server**, un solo Host (`src/PsaWeb.Host`), varios
  **módulos RCL** (`modules/PsaWeb.Modules.*`) — uno por módulo migrado —
  más librerías compartidas en `src/`:
  - `PsaWeb.Sage50` — `ISageConnectionFactory`/`OdbcSageConnectionFactory`,
    `IResolverEmpresaSage` (RUC de sesión → cadena ODBC; `SinShellResolverEmpresaSage`
    por defecto, `HostResolverEmpresaSage` cuando hay shell).
  - `PsaWeb.PeachEbills` — `PeachEbillsContext` (EF Core 9, scaffold de tablas
    reales de `PeachEBills`, sin migraciones — la BD ya existe), `DbSecret`
    (descifra contraseñas TripleDES de `PeachConnString`), `PeachConnStringResolver`
    (RUC → cadena ODBC de Sage de esa empresa, multi-compañía).
  - `PsaWeb.Datil` — cliente REST propio del API de Datil (facturación
    electrónica / retenciones), `DatilOptions.DryRun` (default `true`: arma y
    valida, no envía — **nada de emisión real hasta terminar de migrar los 26
    aplicativos**, decisión 2026-09-07).
  - `PsaWeb.Comprobantes` — piezas compartidas entre módulos de comprobantes
    (proveedor/cliente Sage, validadores de número de documento SRI,
    constructor de retenciones, lectores de factura/NC/liquidación).
  - `PsaWeb.Seguridad` — el shell (ver §4).
  - `PsaWeb.Identidad` — ASP.NET Core Identity local (BD dedicada
    `PsaWebPlataforma`).
  - `PsaWeb.Notificaciones` — SMTP (`Correo:*`), hoy solo para "Solicitar
    anulación de retención".
  - `PsaWeb.Shared` — componentes Blazor (`PsaButton`/`PsaCard`/`PsaModal`/
    `PsaPageHeader`/`PsaDateRange`/`PsaAutocomplete`/`EmpresaSwitcher`/...) +
    `wwwroot/css/psa-theme.css` (paleta PSA: navy `#163154` + magenta
    `#B40046`).
  - `PsaWeb.Ats` — lógica pura del ATS (ver §5.5), sin ODBC/EF, referenciada
    tanto por el módulo como por el endpoint de export en el Host.
- **Patrón por módulo** (`docs/COMO-MIGRAR-UN-APLICATIVO.md`): interfaz
  `I<Nombre>Repository` + `Odbc<Nombre>Repository` (real) + `Sample<Nombre>Repository`
  (datos de muestra) + `<Nombre>Module.Add<Nombre>(config)` que elige real vs.
  muestra según `Sage50:ConnectionString`/`UseSampleData`. Página Blazor
  acotada a la empresa+ambiente de sesión. Export (Excel/XML/PDF) vía un
  endpoint `MapGet` en `Program.cs`, no vía `Process.Start` ni descarga JS.
- **Descubrimiento de rutas de un módulo nuevo**: hace falta declararlo en
  **2 lugares** — `Routes.razor` (`AdditionalAssemblies`) Y
  `Program.cs` (`MapRazorComponents<App>().AddAdditionalAssemblies(...)`).
  Si falta uno de los dos, la ruta 404 en silencio.
- **Datos ODBC**: siempre `OdbcParameter` posicionales (`?`), nunca
  interpolar. `OdbcType.Int` (no `.Integer`, no existe). Sintaxis de outer
  join que entiende el driver Pervasive:
  `FROM { oj TablaA LEFT OUTER JOIN TablaB ON ... }, TablaC WHERE ...`
  (se puede mezclar con joins por coma). **`CultureInfo.InvariantCulture`
  obligatorio** en cualquier conversión número↔texto que toque datos de Sage
  — un server con configuración regional Ecuador (coma decimal) rompe en
  silencio cualquier comparación de texto contra literales con punto (pasó
  de verdad con `LaborCost` en compras del ATS).

## 4. El shell (plataforma)

Construido en fases **F-Shell-0 a F-Shell-4** (F-Shell-5 = Keycloak, no
arrancada). Vive en `src/PsaWeb.Seguridad` + `src/PsaWeb.Identidad` +
pantallas en `src/PsaWeb.Host`.

- **Auth**: ASP.NET Core Identity **local** (no SSO de Windows), cookie de 8h
  sliding, **2FA TOTP** disponible desde `/mi-cuenta/seguridad`. Pantallas:
  `/bienvenida` → `/ingresar` (+2FA) → `/seleccionar-empresa` (auto si hay una
  sola) → `/` (dashboard).
- **Autorización**: reutiliza las tablas de `PeachEBills` que ya usan los
  `.exe` — `users`/`UserTransmitter` (usuario→empresas)/`roles`/
  `udrUserRolesTr` (rol por empresa)/`allowAction`+`adrAllowRol` (permisos por
  rol). `ISecurityDirectory` (`EmpresasDelUsuarioAsync`/`PermisosAsync`/
  `TienePermisoAsync`). Módulo nuevo → agregar código a `Permisos.cs` +
  eventualmente filas en `allowAction`/`adrAllowRol` (el área las carga).
- **`GateProvisional`**: patrón para lanzar un módulo cuyo permiso todavía no
  está asignado a ningún rol — la entrada en `AppCatalogo` queda con lista de
  permisos **vacía** (visible para cualquier empresa) hasta que el área
  asigne el código real. Ya se usó para `quKardex`, `qupurchliq`, `mkpurchliq`,
  y `quats` (ATS). Revertir es 1 línea en `AppCatalogo.cs`.
- **Empresa + ambiente de sesión**: `EmpresaActualService` (scoped),
  `EmpresaSwitcher` en la barra superior (cambio sin re-login), persistido en
  `ProtectedLocalStorage`. Todos los módulos de solo lectura reaccionan a
  `IResolverEmpresaSage.Cambio` / `EmpresaActual.Cambio`.
- **Dashboard + menú dinámicos**: `AppCatalogo.Habilitadas(ctx)` arma la
  grilla de íconos del dashboard y (desde 2026-09-15) el menú superior
  (`NavMenu`) agrupado por **categoría** — módulo nuevo es solo agregar una
  fila a `AppCatalogo.Todas` (`Id`/`Nombre`/`Descripcion`/`Icono`/`Ruta`/
  **`Categoria`**/`Permisos`), sin tocar `NavMenu.razor` ni `Home.razor`.
  `Categorias.Orden` fija las categorías y su orden en la barra — hoy
  `Caja` → `Cartera` → `Bancos` → `Impuestos` → `Comprobantes Electrónicos` → `Inventario`; agregar
  una categoría nueva es agregarla ahí, en la posición donde deba aparecer.
  Una categoría con un solo módulo sigue rindiéndose como grupo desplegable
  (no como link directo) — así no hay que tocar nada cuando un segundo módulo
  se suma a esa categoría más adelante.
- **Admin** (`/admin/usuarios`, `/admin/auditoria`): alta/baja de login local,
  reseteo de clave, quitar 2FA, auditoría de eventos de auth. Gate:
  `Plataforma:Admins` (config), no un permiso de `PeachEBills`.
- Usuario de prueba en dev: `lparedes` / `Psa.Web.Dev.2026!`
  (`Plataforma:UsuarioDev`/`ClaveDev` en `appsettings.Development.json`).

## 5. Módulos migrados (estado al 2026-09-15)

Todos viven en el mismo sitio IIS (`CierreDeCaja` en `SERWEBPSA01`,
`http://192.168.0.11:8088/`) — es un solo Host con módulos, no sitios
separados. Los docs de plan/runbook de cada uno usan la numeración histórica
"app #N de la Ola 1" (`docs/PLAN-APP2-*.md`, `docs/DESPLEGAR-APP3-*.md`) —
esa numeración no tiene relación con las categorías del menú (§4).

### 5.1 Cierre de Caja — categoría **Caja** (piloto, `ReceiptsReportRollerD`)
Solo lectura, `/cierre-de-caja` + export Excel. Primer módulo migrado (probó
todo el patrón). **Desplegado.**

### 5.2 Kardex — categoría **Inventario**
No es un `.exe` standalone — es la pantalla "Reporte de Stock" que vive
dentro del monolito `Sage50usIntegration`, levantada sola. `/kardex` + export
Excel (`ClosedXML`, reproduce fórmulas de saldo corrido del original).
Gate `quKardex` (`GateProvisional`). **Desplegado y validado** (2026-09-12)
contra datos reales de CPTDC — fila a fila idéntico contra una
reimplementación independiente de verificación.

### 5.3 Retenciones — categoría **Comprobantes Electrónicos** (app #1, `AutomaticTwhSender`)
Lee compras pendientes de retención de Sage, arma y emite retenciones vía
Datil, guarda en `PeachEBills`. `/retenciones`, acotado a empresa de sesión +
toggle "Ver todas las empresas" (si el usuario tiene el permiso de lote).
Worker en background (`RetencionesWorker`, deshabilitado por defecto) +
`EjecucionRetencionesGate` (candado single-flight compartido con el botón
manual). Popup de detalle con enlace a PDF/XML de Datil. **Desplegado.**
`Datil:DryRun=true` — nada de emisión real todavía.

### 5.4 Facturas de venta / Notas de crédito / Liquidaciones de compra — categoría **Comprobantes Electrónicos** (app #2, `Sage50FacturacionElectronica`)
3 módulos separados salidos del mismo `.exe` (las retenciones de compra del
mismo `.exe` ya las cubre el módulo de Retenciones, no se duplican). Páginas
`/fe/facturas`, `/fe/notas-credito`, `/fe/liquidaciones`, mismo patrón de
tablero que Retenciones (guardados + pendientes + lote + popup).
Liquidaciones con `GateProvisional` (`qupurchliq`/`mkpurchliq` todavía no
cargados en `allowAction`). "Solicitar anulación de retención" (SMTP,
remitente fijo `anulaciones@paredes.com.ec`) vive en el módulo de Facturas
aunque el botón esté en `/retenciones`. **Desplegados y validados**
(2026-09-10).

### 5.5 ATS — categoría **Impuestos** (app #3, `ATSfromPeach`)
Genera el XML del Anexo Transaccional Simplificado del SRI + valida + genera
el Talón Resumen en PDF. La más grande e intrincada de las 3 (~2.300 LOC
originales de lógica Sage sin documentar). **Desplegada y validada**
(2026-09-14/15) contra CPTDC real.

- `src/PsaWeb.Ats` (librería pura, sin ODBC/EF salvo los lectores):
  `Esquema/` (regenerado con `xsd.exe` desde el XSD vigente del SRI, no
  copiado del esquema desactualizado del `.exe`), `EscritorXmlAts`,
  `Ventas/`, `Compras/`, `Anulados/`, `ArmadorAts` (arma el `ivaType`
  completo), `Validacion/ValidadorAts` ("Revisar ATS"), `TalonResumen/`
  (`ArmadorTalonResumenAts` + `TalonResumenPdfBuilder` con **QuestPDF**,
  licencia Community — se prefirió sobre PuppeteerSharp para no meter un
  binario de Chromium en el server).
- `modules/PsaWeb.Modules.Ats`: página `/ats` (año+mes → 7 pestañas de solo
  lectura + card "Revisar ATS" + 2 descargas: XML y Talón PDF).
- Gate `quats` (`GateProvisional`, igual estado que `quKardex` al nacer: fila
  en `allowAction` pero 0 en `adrAllowRol`).
- **Doctrina "port fiel"** (ver §6) aplicada a fondo: 3 bugs reales del
  `.exe` documentados y preservados (`Bug B1`/`B2`/`B3`), 1 bug real corregido
  porque contradice la normativa vigente (fix de `numEstabRuc`/
  `ventasEstablecimiento` — usa `Establishments` activos, no "quién facturó
  el mes"), y **2 reglas nuevas de negocio agregadas a pedido del usuario**
  que el `.exe` original no tenía forma de expresar (§5.5.1).
- Validado número por número contra los 2 entregables reales de la
  declaración de CPTDC julio/2026 (`C:\SRI-DIMM\Documentacion\ATS CPTDC
  JULIO  2026.xml` y `TRSMN-ATS-07-2026-CPTDC.pdf`) — no se commitean al
  repo por ser datos reales de un cliente.
- ~418 tests en la solución completa al cierre de esta fase.

#### 5.5.1 Convenciones de datos en Sage 50 específicas del ATS

Sage 50 no tiene campos nativos para estos 2 conceptos — se resuelven
reusando campos existentes de la ficha del cliente/proveedor/documento, por
convención acordada con el usuario. **Importante si otro módulo toca las mismas
tablas**: no reusar estos mismos campos para otra cosa sin coordinarlo.

- **"Parte relacionada" (ventas)**: se marca asignándole al **cliente** un
  **Sales Rep** (`Customers.EmpRecordNumber` → tabla `Employee`) cuyo
  `EmployeeID` o `EmployeeName` sea exactamente **"SI RELACIONADO"**. *(Se
  intentó primero con `Customers.AccountNumber` y luego con `CustomField5` —
  ambos ya estaban en uso por el port para otra cosa — Sales Rep fue la
  tercera opción, la que no colisiona con nada.)* Cuando aplica, el port
  además **reclasifica todo el gravado bajo `baseImponible`** (no solo dejar
  de pisarlo a 0) — regla general confirmada con el usuario, no un ajuste
  puntual.
- **Autoretenciones de Grandes Contribuyentes (compras)**: se excluyen del
  ATS marcando `JrnlHdr.ShipVia = "AUTORETENCION"` en el comprobante — no son
  compras reales a terceros.
- **Pendiente de higiene de datos en Sage** (no es código): al menos un
  cliente en CPTDC quedó con `CustomField5` (Pasaporte) = `"SI"`, resabio de
  cuando se evaluó (y se descartó) esa alternativa — corromper ese campo
  rompe la identificación del cliente en el ATS. Limpiarlo si no se hizo.

### 5.6 Reportes de Access: PWC, Comisiones y Cheques — categorías **Cartera** y **Bancos** (agregado 2026-09-21)
Tres reportes hechos en Microsoft Access (`ReportesEmpresas.mdb` / `ReportesEgresosDemoR.mdb`), migrados como **un
módulo con 3 páginas** (`modules/PsaWeb.Modules.Reportes`), solo lectura, sobre la empresa de sesión. Plan, hallazgos,
catálogo de bugs heredados y decisiones: `docs/PLAN-REPORTES-ACCESS.md`. **En `main`, sin push ni deploy.**

- **`/cartera/pwc` — PWC (cuentas por cobrar).** Facturas de venta con saldo + subtotal, IVA, retenciones IR/IVA
  (porcentaje leído de la descripción) y monto a cobrar. Filtros (emisión, cobranza, cliente, factura, anunciante, ciudad),
  encabezado / cobrador / columnas configurables por empresa, resumen de retenciones dinámico y por ciudad. Excel ClosedXML
  con fechas reales.
- **`/cartera/comisiones` — Comisiones por recibos.** Facturas cobradas por recibos, agrupadas por cliente, filtro por
  rango de recibos o de fechas del recibo. **Port fiel con dos bugs heredados marcados en pantalla y con opción de
  corrección:** C1 (abono = total pagado de la factura, no lo aplicado por el recibo) y C2 (rango de recibos comparado
  como texto: entran el 513 y el 514 en «5122–5146»).
- **`/bancos/cheques` — Cheques y comprobantes de egreso.** Lista de pagos (prefijo de la referencia = tipo de pago),
  vista previa y **PDF A4 con coordenadas en mm** (medidas del reporte, fuente monoespaciada, corrección X/Y de
  calibración) + hoja de prueba de impresión. Monto en letras con relleno a 90 (bug Q1 con opción de corrección).
  Empresa, logo, ciudad y firmas configurables (nada fijo a Efemedio). **Falta la prueba con la matricial real** (D3/D4).
- **Configuración por empresa** en la tabla `ConfiguracionesReporte` de `PsaWebPlataforma` (migración automática al arrancar).
- **Permisos** `quRptPwc` / `quRptComis` / `quRptChq` con `GateProvisional`; SQL en `docs/sql/permisos-reportes-access.sql`
  (no ejecutado).
- **Validado** contra el Sage local de Efemedio y los Excel del usuario (PWC 80/82, Comisiones 41/41, Cheques contra las
  consultas verbatim de Access). Ver `docs/PLAN-REPORTES-ACCESS.md` §9.1 para repetirlo.
- **Convenciones nuevas que sirven a otros módulos:** el proyecto de tests puede correr a **x86** con pruebas opcionales contra
  Sage local (`PSAWEB_TEST_SAGE_EFEMEDIO`); las páginas se prueban con `HtmlRenderer` sin login; el PDF se verifica con
  PdfPig (posiciones en mm).
- **Ojo:** el Host completo ya no arranca sin `Plataforma:ConnectionString` (los endpoints de Conciliación SRI y Cierre de
  Caja piden servicios que sólo existen con shell; verificado el 2026-09-21): el modo «standalone» sin plataforma ya no arranca.

## 6. Doctrina "port fiel"

Regla central de todo el proyecto, repetida en cada plan: **replicar lo que
hace el `.exe`, incluidos sus bugs conocidos**, documentándolos como
`// Bug Bn` en el código y en tests dedicados — no "mejorar" silenciosamente
la lógica de negocio. Excepciones explícitas:

1. **Accidentes de implementación que revientan** (no reglas de negocio):
   agregar guardas defensivas está bien (ej. un `string.Length` corto que
   tiraría `ArgumentOutOfRangeException` en el original).
2. **Contradicciones confirmadas contra la normativa vigente** (no contra el
   criterio de alguien): ahí sí se corrige, documentando el hallazgo y el
   porqué (ejemplo: el fix de establecimientos del ATS, verificado contra la
   Ficha Técnica del SRI + el validador real del DIMM).
3. **Decisiones de negocio explícitas del usuario**, aunque diverjan de la
   normativa "ideal" (ejemplo: dejar la forma de pago fija en `"20"` en vez
   de condicionarla por fecha/monto — menos reglas = menos riesgo de error
   humano, decisión tomada a sabiendas).

Todo cambio de comportamiento heredado del `.exe` se **valida contra datos
reales de Sage** (scripts descartables en el scratchpad de la sesión, nunca
commiteados) antes de darse por bueno — no alcanza con que compile y los
tests pasen con datos inventados.

## 7. Despliegue

Un solo sitio IIS: **`CierreDeCaja`** en `SERWEBPSA01`
(`C:\inetpub\CierreDeCaja`, `http://192.168.0.11:8088/`, puerto 8088). Auth
anónima ON / Windows Auth OFF (el login lo maneja el shell de Identity, no
IIS). App pool 32-bit, **"Always Running" + `idleTimeout=0`** para que no se
duerma.

**Patrón de redeploy** (cada módulo nuevo es un delta sobre esto, no un deploy
desde cero — ver `docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md` como ejemplo
más reciente):

```powershell
# En PREDATOR
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish
Compress-Archive -Path publish\* -DestinationPath PsaWeb.Host-publish.zip -Force
# copiar el zip a mano (USB / recurso compartido) a C:\Deploy en el server
```

```powershell
# En SERWEBPSA01 (como Administrador)
$SiteName = "CierreDeCaja"; $Dst = "C:\inetpub\CierreDeCaja"; $Zip = "C:\Deploy\PsaWeb.Host-publish.zip"
$WebConfigBak = "C:\Deploy\web.config.bak-$(Get-Date -f yyyyMMdd-HHmm)"
$FolderBak = "$Dst.bak-$(Get-Date -f yyyyMMdd-HHmm)"

Stop-WebAppPool -Name $SiteName   # NO alcanza con Stop-WebSite, ver nota abajo
Start-Sleep -Seconds 5
Copy-Item "$Dst\web.config" $WebConfigBak -Force
Copy-Item $Dst $FolderBak -Recurse
Get-ChildItem $Dst -Recurse | Remove-Item -Recurse -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($Zip, $Dst)
Copy-Item $WebConfigBak "$Dst\web.config" -Force
New-Item -ItemType Directory -Force "$Dst\logs" | Out-Null

Start-WebAppPool -Name $SiteName
Start-WebSite $SiteName
```

**Gotcha confirmado 2026-09-15**: como el app pool está en "Always Running",
`Stop-WebSite` por sí solo **no** libera `aspnetcorev2_inprocess.dll`
(sigue cargado por el worker process del pool) → `Remove-Item`/
`ExtractToDirectory` fallan a mitad de camino. Hay que parar el **pool**
(`Stop-WebAppPool`), no solo el sitio.

El `web.config` del zip es genérico — **siempre** hay que preservar/restaurar
el real del sitio (tiene las env vars: `Plataforma__ConnectionString`,
`PeachEbills__ConnectionString`, `Sage50__ConnectionString`,
`Datil__DryRun=true`, `Retenciones__Worker__Habilitado`, etc.). El zip y los
scripts de deploy **no se commitean al repo** (`.gitignore`: `publish/`,
`*.zip`) — se entregan directo al usuario cada vez.

No hay HTTPS interno (se intentó y se revirtió 2026-09-07: Kaspersky con
inspección TLS aborta el handshake contra un cert de CA interna, y las PCs no
usan el DNS interno). Queda para cuando la plataforma sea pública (cert
público, sin tocar nada de esto) — decisión explícita, no pendiente urgente.

## 8. Índice de documentación del repo

- `docs/COMO-MIGRAR-UN-APLICATIVO.md` — la receta para portar un módulo nuevo.
- `docs/PLAN-SHELL-PLATAFORMA.md` — diseño del shell (F-Shell-0..5).
- `docs/PLAN-KARDEX-INVENTARIOS.md`, `docs/PLAN-APP2-FACTURACION-ELECTRONICA.md`,
  `docs/PLAN-APP3-ATS.md` — planes de cada módulo (investigación + decisiones).
- `docs/APPS-SDK-VS-SOLO-LECTURA.md` — clasificación de los 26 aplicativos
  (cuáles son solo lectura vs. cuáles necesitan el SDK/Sage Bridge).
- `docs/DESPLEGAR-SHELL-EN-SERWEBPSA01.md` — runbook del deploy fundacional
  del shell (referencia; ya ejecutado, no repetir).
- `docs/DESPLEGAR-APP2-FE-EN-SERWEBPSA01.md`,
  `docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md` — runbooks delta de cada módulo
  (patrón a copiar para la próxima).
- `docs/DESPLEGAR-EN-PSACONTABILIDAD2.md` — runbook del modelo standalone
  viejo (pre-shell), solo como referencia histórica.
- `docs/VALIDAR-CONTRA-SAGE50.md` — cómo apuntar el dev en PREDATOR contra
  una Sage real en vez de datos de muestra.
- `docs/PLAN-REPORTES-ACCESS.md` — plan, validación y decisiones de los reportes de Access (PWC, Comisiones, Cheques);
  `docs/sql/permisos-reportes-access.sql` — permisos `quRpt*` (pendiente de aplicar).

## 9. Pendiente / backlog global

- **Wave 1 (aplicativos de solo lectura) — quedan sin portar**: revisar
  `docs/APPS-SDK-VS-SOLO-LECTURA.md` para la lista completa de los 26; de los
  ya identificados, el pool de standalone read-only está casi agotado
  (`PaymentsMailing` = obsoleto; `Sage50IntegrationConfig`/`Sage50MetaData` se
  absorben, no se portan aparte). Falta relevar qué sigue.
- **Permisos `GateProvisional` pendientes de asignar a roles reales** (el
  área tiene que cargar filas en `adrAllowRol`): `quKardex`, `qupurchliq`/
  `mkpurchliq`, `quats`, `quconcsri` y los 3 de los reportes de Access (`quRptPwc`, `quRptComis`, `quRptChq`).
- **SMTP real** para "Solicitar anulación de retención" (`Correo:*`) — hoy
  sin configurar en el server, el botón avisa "correo no configurado".
- **Emisión real** (`Datil:DryRun=false`) — decisión explícita: no antes de
  terminar de migrar los 26 aplicativos.
- **Reportes de Access — pendientes**: prueba de impresión real de los cheques (D3/D4; si el PDF no sirve en la matricial,
  fase 2 con ESC/P), decisiones sobre los bugs C1/C2/Q1 y aplicar `permisos-reportes-access.sql` antes del deploy.
- **Formularios 103/104 del ATS** — descartados de este corte (F7 del plan
  ATS), quedan si se decide retomarlos.
- **Backlog del ATS para módulos de escritura futuros**: si un cliente recibe
  una retención en un período sin comprobante de venta en ese mismo período,
  el ATS no la reporta (limitación real de qué puede saber Sage). Cuando se
  porte un módulo de **escritura** de retenciones/facturación, agregar una
  alerta + botón "Pasar a período correcto" cuando la fecha del comprobante
  de retención no coincide con el período del comprobante de venta
  relacionado.
- **F-Shell-5 (Keycloak)** — swap de `IProveedorAutenticacion`, no arrancado.
- **Opción B (HTTPS público + salir del modelo RDS)** — decisión ya tomada
  de posponerla, no una tarea abierta ahora mismo.

# Desplegar el Portal de ventas en `SERWEBPSA01` (con la escritura en Sage APAGADA)

Es un **redeploy del mismo sitio** que el shell (`C:\inetpub\CierreDeCaja`, `http://192.168.0.11:8088/`). Agrega el **Portal de ventas** (inventario y precios, prefacturas con PDF y correo a Contabilidad)
y deja **apagados** los módulos de la Ola 2 que escriben en Sage, que se desplegarán después en el «deploy único de escritura».

> Base: `docs/DESPLEGAR-SHELL-EN-SERWEBPSA01.md` + los runbooks de ATS, FE y Conciliación. Todo lo de IIS, `PsaWebPlataforma`, app pool 32-bit, anónima ON / Windows OFF y permisos SQL del app pool
> **ya está** y no cambia. Este runbook es solo el delta.

## 1. Qué incluye y qué no

**Entra** (el paquete es el sitio completo, como siempre):
- **Portal de ventas** (`/ventas/inventario`, `/ventas/prefacturas`, `/ventas/prefacturas/nueva`, `/ventas/prefacturas/{id}`, descarga `/ventas/prefacturas/{id}/pdf`) y `Configuración > Ventas` (`/admin/ventas`).
- Todos los módulos ya desplegados: Cierre de Caja, Kardex, Reportes de Access (PWC, Comisiones, Cheques), ATS, Conciliación SRI, Facturas / Notas de crédito / Liquidaciones / Retenciones (Datil, `DryRun` según tu web.config).
- Cambios compartidos sin efecto funcional en lo ya desplegado: menú «Configuración» (agrupa Usuarios, Auditoría y Ventas), correo con adjuntos (`PsaWeb.Notificaciones`), subnavegación legible (CSS), permisos nuevos en el catálogo.

**NO entra en funcionamiento** — interruptor `Escritura:Habilitada` (**apagado por defecto**, no se define en el `web.config`):

| Qué queda apagado | Cómo se nota |
|---|---|
| Compras, Facturas recibidas del SRI, Liquidación de importaciones | No salen en el menú ni en el inicio; sus rutas (`/compras…`, `/exportar/liquidacion-importacion`) responden **404** |
| Administración del Sage Bridge (`/admin/sage-bridge`) | Sin enlace en «Configuración»; si se abre la URL dice «No disponible… desactivados en este servidor» |
| «Registrar en Sage» dentro de Conciliación SRI | El botón no aparece |
| Descarga de XML recibidos en segundo plano (WS del SRI) | No arranca |
| Tablas de la Ola 2 (`TrabajosSage`, `LatidosBridge`, `EmpresasBridge`, `AuditoriaRegistrosSage`, XML recibidos) | **No se crean** |

El Sage Bridge (servicio de Windows) **no se instala** ahora.

## 2. Qué cambia en la base de datos

Solo **3 tablas nuevas en `PsaWebPlataforma`**, creadas **solas al arrancar el sitio** (migración `Prefacturas`, mismo mecanismo que Conciliación y Reportes; `Plataforma:MigrarAlArrancar` = `true` por defecto):

- `Prefacturas`, `PrefacturaLineas` y `ConfiguracionesVentas`.

Nada en `PeachEBills` ni en Sage. **Sin pasos manuales de SQL.** Los permisos nuevos (`quSalesStk`, `quSalesQte`, `mkSalesQte`, `auSalesQte`) **no** se aplican en este deploy: mientras tanto el portal usa las llaves provisionales de
facturación de venta (ver §6).

## 3. Variables de entorno del `web.config`

**Obligatorio para que llegue el correo a Contabilidad** (no hay variable nueva obligatoria para que el sitio funcione; sin SMTP las prefacturas se guardan igual y el correo queda «sin configurar», se puede reenviar después):

```xml
<environmentVariables>
  <environmentVariable name="Correo__Servidor" value="smtp.ejemplo.com" />
  <environmentVariable name="Correo__Puerto"   value="587" />
  <environmentVariable name="Correo__Usuario"  value="ventas@paredes.com.ec" />
  <environmentVariable name="Correo__Clave"    value="********" />
  <environmentVariable name="Correo__Ssl"      value="true" />
  <!-- Opcional: remitente (por defecto anulaciones@paredes.com.ec) -->
  <environmentVariable name="Correo__De"       value="ventas@paredes.com.ec" />
</environmentVariables>
```

**No agregar** `Escritura__Habilitada` (o dejarla en `false`). El script de deploy se **niega a desplegar** si el `web.config` la tiene en `true`.

### 3.1 Configurar el SMTP paso a paso

1. **Dónde:** `C:\inetpub\CierreDeCaja\web.config`, dentro de `<aspNetCore …>` → `<environmentVariables>`, junto a las variables que ya hay. El elemento es **`<environmentVariable name="…" value="…" />`** (**no** `<add>`: con `<add>` IIS responde 500.19).
   Si la clave tiene `&`, `<`, `>` o `"`, escribirlos como `&amp;`, `&lt;`, `&gt;`, `&quot;`.
2. **Valores** (preguntar a quien administre el correo): servidor, puerto, usuario, clave y si exige SSL/TLS.
   - **Puerto 587 + `Correo__Ssl=true`** (STARTTLS) es lo que soporta la aplicación. **El puerto 465 (SSL implícito) NO funciona** con este cliente.
   - Puerto 25 sin autenticación solo sirve si el servidor de correo acepta el relay desde `SERWEBPSA01`: dejar `Correo__Usuario` y `Correo__Clave` **sin definir** y `Correo__Ssl=false`.
   - **`Correo__De` debe ser un buzón que esa cuenta pueda usar como remitente** (casi todos los proveedores exigen que coincida con `Correo__Usuario`). Por defecto el remitente es `anulaciones@paredes.com.ec`.
   - Gmail: contraseña de aplicación (requiere verificación en 2 pasos), `smtp.gmail.com`, 587, SSL. Microsoft 365: `smtp.office365.com`, 587, SSL, y el buzón debe tener «SMTP autenticado» habilitado (muchos tenants lo bloquean).
3. **Guardar el archivo:** IIS reinicia solo la aplicación (si no, reciclar el app pool `CierreDeCaja`). La clave queda en texto claro: dejar `web.config` legible solo para Administradores y el usuario del app pool.
4. **Probar la red y las credenciales desde el servidor**, antes de la web (PowerShell como Administrador):

   ```powershell
   Test-NetConnection smtp.ejemplo.com -Port 587          # TcpTestSucceeded debe ser True (si no: firewall o puerto)
   $c = Get-Credential ventas@paredes.com.ec
   Send-MailMessage -SmtpServer smtp.ejemplo.com -Port 587 -UseSsl -Credential $c -From ventas@paredes.com.ec -To tu.correo@paredes.com.ec -Subject "Prueba SMTP SERWEBPSA01" -Body "ok"
   ```
5. **Probar desde el portal:** abrir una prefactura → **Reenviar correo** (o emitir una de prueba). El estado del correo y el error exacto quedan en la tarjeta «Correo a Contabilidad» de la prefactura; la lista muestra «Enviado», «Falló» o «Sin configurar».
6. **Si falla**, leer el mensaje de esa tarjeta:

   | Mensaje | Causa | Qué hacer |
   |---|---|---|
   | «El servidor de correo (SMTP) no está configurado» | Falta `Correo__Servidor` (o IIS no recargó el `web.config`) | Revisar el nombre exacto de la variable y reciclar el app pool |
   | «The SMTP server requires a secure connection» / «Must issue a STARTTLS» | El servidor exige TLS | `Correo__Ssl=true` y puerto 587 |
   | «5.7.57 / 535 / Authentication unsuccessful» | Usuario o clave mal, o SMTP AUTH deshabilitado en el buzón | Verificar la cuenta; en Gmail usar contraseña de aplicación |
   | «Mailbox unavailable / not allowed to send as» | `Correo__De` distinto de la cuenta autenticada | Poner `Correo__De` igual a `Correo__Usuario` |
   | Tarda ~40 s y falla por tiempo | Puerto bloqueado por firewall | Habilitar la salida desde `SERWEBPSA01` al servidor SMTP |
   | Falla en el «handshake» / certificado | Antivirus con inspección TLS (ya ocurrió con Kaspersky) | Excluir al servidor SMTP de la inspección |

   Los intentos también quedan en `C:\inetpub\CierreDeCaja\logs` («Correo enviado a …» o «No se pudo enviar el correo de la prefactura …»).
7. **Los 2 destinatarios** no son del `web.config`: se cargan por empresa en **Configuración → Ventas** (correo de Contabilidad y uno adicional).

### 3.2 Caso real: SMTP de HostGator (cPanel) para `paredes.com.ec` — configuración que funcionó (2026-10-05)

Buzón `portalweb@paredes.com.ec`. cPanel lista como «recomendado» el SMTP `mail.paredes.com.ec:465`, pero **desde `SERWEBPSA01` eso no sirve**; lo que funcionó:

```xml
<environmentVariable name="Correo__Servidor" value="gator4065.hostgator.com" />
<environmentVariable name="Correo__Puerto"   value="587" />
<environmentVariable name="Correo__Usuario"  value="portalweb@paredes.com.ec" />
<environmentVariable name="Correo__Clave"    value="********" />
<environmentVariable name="Correo__Ssl"      value="true" />
<environmentVariable name="Correo__De"       value="portalweb@paredes.com.ec" />
```

Lo que pasó y por qué, para no repetirlo:

| Síntoma | Causa | Solución |
|---|---|---|
| El puerto **465** no conecta (`Test-NetConnection` → `TcpTestSucceeded: False`) | Bloqueado desde el servidor. Además la aplicación no soporta SSL implícito (465) | Usar **587 + STARTTLS** (`Correo__Ssl=true`) |
| `mail.paredes.com.ec:587` abre el puerto pero **no da el saludo `220`**; la web mostraba «Syntax error, command unrecognized» y PowerShell «Unable to read data from the transport connection» | Ese nombre no atiende SMTP en el 587 desde esa red | Usar el **nombre real del servidor de HostGator** (el que sale en la URL de cPanel, `gator4065.hostgator.com`), que sí responde (`220 … bulk e-mail`) |
| `Send-MailMessage` de PowerShell: «A call to SSPI failed» / «The function requested is not supported» | PowerShell 5.1 negocia TLS 1.0 por defecto en Windows Server 2012 R2 y HostGator exige TLS 1.2. **Es un defecto de la herramienta de prueba**, no de la aplicación | Antes de probar: `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12`. La web usa .NET moderno (SChannel con los valores del sistema) y **no** lo necesita |
| Kaspersky Small Office | Se sospechó (corta SMTP en algunos equipos) y se descartó: Gmail:587 daba `220` desde el mismo servidor y apagarlo no cambió nada | — |

Diagnóstico rápido que sirve para cualquier proveedor (solo conecta y lee el saludo; en SMTP habla primero el servidor):

```powershell
function Probar-Smtp($h,$p=587){ try{ $t=New-Object Net.Sockets.TcpClient($h,$p); $s=$t.GetStream(); $s.ReadTimeout=8000; $r=New-Object IO.StreamReader($s); "$h`:$p -> " + $r.ReadLine() }catch{ "$h`:$p -> SIN SALUDO: " + $_.Exception.Message } finally { if($t){$t.Close()} } }
Probar-Smtp smtp.gmail.com                  # referencia: debe dar 220
Probar-Smtp gator4065.hostgator.com         # el servidor del proveedor
```

Notas: pegar los comandos de PowerShell **de uno en uno** (pegados juntos se concatenan y fallan); y `Invoke-WebRequest` a cPanel (`https://gator4065.hostgator.com:2083`) en PowerShell 5.1 da el error inocuo `"utf-8" is not a supported encoding name` *después* de negociar TLS, lo que confirma que el cifrado funciona.

## 4. Paquete y despliegue

El paquete ya está generado en PREDATOR (Release, `win-x86`, autocontenido; arranca en modo Producción con la escritura apagada, verificado):

- Archivo: `PsaWeb.Host-publish-ventas.zip` (61 MB) · SHA256 `4B32D99AA31FA50932A0C27671E0D4609DCE836BD324B0F612B3908DC771F1C4`
- Script: `deploy-ventas.ps1` (verifica el SHA256, que la escritura no esté encendida, respalda el sitio, copia sin tocar `web.config` ni `logs\`, reinicia y comprueba que el login responda 200).

Para regenerarlo:

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
git pull
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish-ventas
# comprimir publish-ventas\* en PsaWeb.Host-publish-ventas.zip y actualizar $HashEsperado en deploy-ventas.ps1
```

1. Copiar `PsaWeb.Host-publish-ventas.zip` y `deploy-ventas.ps1` a `C:\Deploy` en el server (USB / recurso compartido).
2. (Opcional, antes) Agregar las variables `Correo__*` al `web.config` (§3).
3. En `SERWEBPSA01`, **como Administrador**: `C:\Deploy\deploy-ventas.ps1`.

> Gotcha ya conocido: se detiene el **app pool** (`Stop-WebAppPool`), no solo el sitio — está en «Always Running» y retiene `aspnetcorev2_inprocess.dll`.

## 5. Después del deploy

1. **Log al arrancar** (carpeta `logs\`): `Ventas (prefacturas): migraciones aplicadas.` y `Módulos de escritura en Sage (Compras, Recibidos, Bridge): DESACTIVADOS`.
2. **Menú**: aparece la categoría **Ventas** (Inventario y precios, Prefacturas) y **no** aparecen Compras, Liquidación de importaciones ni Facturas recibidas.
3. **Configuración → Ventas** (solo administradores): por cada empresa, cargar los **2 correos** (Contabilidad + uno adicional), vigencia **15 días**, código de IVA **`4-15%`** y **15 %**.
4. **Prueba funcional** con una empresa (SANCEV) y un usuario con permisos de facturación electrónica de venta:
   - Inventario y precios: buscar un ítem; elegir un cliente y ver su lista.
   - Nueva prefactura: cliente → vendedor → ítem → emitir. Debe llevar a la prefactura `PF-0001`, **descargar el PDF** y llegar el **correo** con el PDF adjunto.
   - En la prefactura: **Marcar como facturada** con un número de prueba (o **Anular**) y volver a la lista.
5. **Conciliación SRI** sigue funcionando y **sin** botón «Registrar en Sage».

## 6. Permisos (provisionales hasta que el área apruebe)

Hasta aplicar `docs/sql/permisos-ventas.sql` (decisión del área, ver `docs/NOTA-AREA-PERMISOS-VENTAS.md`): **ver** lo habilita `qusaleinv` y **emitir y cerrar** `mksaleinv`. Quien ya factura electrónicamente puede probarlo; los
**vendedores** no entran hasta tener su rol («Vendedor» con `quSalesStk` + `quSalesQte` + `mkSalesQte`) y su usuario web con acceso a la empresa.

## 7. Volver atrás

```powershell
Stop-WebAppPool CierreDeCaja
robocopy "C:\Deploy\backup-CierreDeCaja-<fecha-hora>" "C:\inetpub\CierreDeCaja" /MIR /XF web.config /XD logs
Start-WebAppPool CierreDeCaja
```

Las 3 tablas nuevas pueden quedarse (no las usa la versión anterior).

## 8. Cuando se despliegue la escritura (más adelante)

Agregar `Escritura__Habilitada=true` al `web.config`, instalar el servicio **Sage Bridge** (`bridge/README.md`, autorización en Sage, cuenta de Windows dedicada, ventana de mantenimiento) y redeployar: se crean las tablas de la cola y de
documentos recibidos, reaparecen Compras / Importaciones / Facturas recibidas, el botón «Registrar en Sage» de Conciliación y `/admin/sage-bridge`. Planes: `docs/PLAN-OLA2-COMPRAS-SAGE.md` y `docs/PLAN-OLA2-LIQUIDACION-IMPORTACIONES.md`.

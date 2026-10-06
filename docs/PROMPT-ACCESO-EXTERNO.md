# Prompt para el chat de «Acceso externo a los Aplicativos Web PSA» (2026-10-05)

Se pega completo, como primer mensaje, en un chat nuevo de Claude Code abierto sobre este repositorio. Cubre: configuración técnica para exponer el sitio fuera de la red interna,
auditoría de usuarios por empresa y por usuario con sus niveles de acceso y permisos por módulo, diseño de perfiles externos y el cambio de nombre del sitio IIS de «CierreDeCaja» a «Aplicativos Web PSA».

```text
Vamos a preparar el acceso EXTERNO (fuera de la red interna) a los "Aplicativos Web PSA" que ya tenemos desplegados en el servidor, y a ordenar quién entra a qué. Trabaja en español. Primero lee y haz un inventario de SOLO LECTURA; no cambies nada del servidor ni de las bases sin que yo lo apruebe.

## 1. Contexto

Repo: C:\PROYECTOS IA\Aplicativos web (GitHub psadesarrolloia/aplicativos-web, rama main; último commit al 2026-10-05: ace2c3e). Dev en PREDATOR (Windows 10). Producción: SERWEBPSA01 (192.168.0.11), Windows Server 2012 R2 (fuera del soporte normal), al que accedo por RDP y donde hago yo mismo todo lo del servidor; tú no tienes acceso a él: me das los comandos/scripts y yo los ejecuto y te cuento el resultado.

Aplicación: un solo sitio ASP.NET Core 9 + Blazor Server (src/PsaWeb.Host) con un módulo por aplicativo (modules/PsaWeb.Modules.*), que lee/escribe contra Sage 50 US (Pervasive/Actian Zen vía ODBC de 32 bits, por eso x86) y contra la base SQL Server PeachEBills. Hoy se sirve por IIS en http://192.168.0.11:8088/ (sin HTTPS: se intentó con CA interna y Kaspersky Small Office, que inspecciona TLS, abortaba el handshake; las PCs tampoco usan el DNS interno). App pool 32-bit "Always Running" con idleTimeout=0, autenticación anónima ON / Windows OFF (el login lo maneja la propia aplicación). El deploy se hace armando un zip en PREDATOR, copiándolo a C:\Deploy del servidor y corriendo un script PowerShell como Administrador (ejemplo: deploy-ventas.ps1; runbooks en docs/DESPLEGAR-*.md). Las variables de entorno van en el web.config como <environmentVariable name="..." value="..." /> (NO <add>).

Lee primero, en este orden: docs/ESTADO-MIGRACION-WEB.md (es del 2026-09-15 y está desactualizado: hay que actualizarlo al final), docs/PLAN-SHELL-PLATAFORMA.md, docs/DESPLEGAR-SHELL-EN-SERWEBPSA01.md, docs/DESPLEGAR-VENTAS-EN-SERWEBPSA01.md, docs/NOTA-AREA-PERMISOS-VENTAS.md, docs/PLAN-PORTAL-VENTAS.md (§10 permisos), src/PsaWeb.Seguridad (Permisos.cs, AppCatalogo.cs, ReglasCompras.cs, ReglasVentas.cs, PeachEbillsSecurityDirectory.cs, EscrituraOptions.cs) y src/PsaWeb.Identidad. La memoria del proyecto tiene notas de la Ola 2.

## 2. Qué hay desplegado y qué no

Desplegado: Cierre de Caja, Kardex, Reportes de Access (PWC, Comisiones, Cheques), ATS, Conciliación SRI (con extensión de Chrome y worker), Facturas/Notas de crédito/Liquidaciones/Retenciones (Datil con DryRun, ver web.config real) y Portal de ventas (inventario y precios, prefacturas con PDF y correo; permisos nuevos quSalesStk/quSalesQte/mkSalesQte/auSalesQte aún con GateProvisional, el área no aplicó docs/sql/permisos-ventas.sql).
APAGADOS en producción por el interruptor Escritura:Habilitada (no definido en el web.config del servidor): Compras, Facturas recibidas, Liquidación de importaciones y administración del Sage Bridge. Deben seguir apagados: ningún usuario externo debe poder llegar a nada que escriba en Sage.
Correo saliente ya funciona (HostGator, gator4065.hostgator.com:587 + STARTTLS, buzón portalweb@paredes.com.ec).

## 3. Trabajo a hacer (propón el orden y espera mi aprobación por fase)

A) EXPOSICIÓN EXTERNA, parte técnica. Analiza y propón opciones con pros, contras y riesgos, y recomienda una:
   - Cómo publicar: IP pública + NAT/port-forward en el router, túnel (p. ej. Cloudflare Tunnel), reverse proxy en otra máquina o en la nube, VPN para ciertos usuarios; qué dominio/DNS y qué certificado público (Let's Encrypt/comercial); si el TLS se termina en IIS 8.5 de un Windows Server 2012 R2 o en un proxy delante; si conviene sacar el hosting de ese servidor EOL.
   - Blazor Server exige WebSockets/SignalR estable: compatibilidad con el proxy/túnel.
   - Endurecimiento: HTTPS obligatorio + HSTS, cookies Secure/SameSite, ForwardedHeaders si hay proxy, cabeceras de seguridad, rate-limiting del login (ya existe), bloqueo por intentos, 2FA TOTP (ya existe en /mi-cuenta/seguridad) OBLIGATORIO para externos, timeouts de sesión, política de contraseñas, logs/auditoría de acceso (AuditoriaAuth), WAF o allowlist de IP si aplica, límites de tamaño/tiempo de las descargas (PDF, Excel, XML).
   - /admin/* (usuarios, auditoría, configuración) debe quedar accesible SOLO desde la red interna o desde IPs permitidas.
   - Qué pasa con la extensión de Chrome de Conciliación SRI (POST /conciliacion-sri/api/comprobantes con token de API) y con los workers en segundo plano al estar expuesto.
   - Qué cambios de código harían falta (indícalos como propuesta con pruebas; no los implementes sin mi OK).

B) RENOMBRAR el aplicativo. En el servidor el sitio se llama "CierreDeCaja" (sitio IIS, app pool y carpeta C:\inetpub\CierreDeCaja), nombre heredado del primer módulo piloto. Debe llamarse "Aplicativos Web PSA". Propón un plan de cambio sin romper nada (nombre para mostrar con espacios vs. identificador sin espacios para pool/carpeta, bindings, backups, rollback) y la lista de TODO lo que lo referencia: scripts deploy-*.ps1 ($SiteName, $Dst), docs/DESPLEGAR-*.md, runbooks, rutas de logs, etc. Revisa también que el nombre visible en la aplicación (títulos, menú, correos, PDF) diga "Aplicativos Web PSA".

C) AUDITORÍA DE USUARIOS Y ACCESOS (solo lectura al inicio). Inventario completo:
   - Usuarios web (ASP.NET Identity en PsaWebPlataforma: AspNetUsers con PeachUsername, 2FA activado o no, bloqueo, último acceso) y su relación con PeachEBills (users, UserTransmitter = usuario→empresas, roles, udrUserRolesTr = rol por empresa, allowAction + adrAllowRol = permisos por rol).
   - Matriz POR EMPRESA y POR USUARIO: a qué empresas entra, con qué rol(es) y a qué módulos (según AppCatalogo y las llaves de Permisos.cs). Marca: usuarios sin 2FA, cuentas huérfanas o sin empresa, administradores (Plataforma:Admins), cuentas con demasiado alcance, usuarios con acceso a empresas que no deberían, empresas inactivas.
   - Módulos con GateProvisional (visibles para cualquier usuario con acceso a la empresa mientras el área no asigne llaves): quKardex, quats, quconcsri, quRptPwc/quRptComis/quRptChq, qupurchliq/mkpurchliq, y los de Ventas. ESTO ES UN RIESGO SI ABRIMOS A EXTERNOS: propón cerrar cada uno con llaves reales antes de exponer, con los scripts SQL en el estilo de docs/sql/*.sql (vista previa y aplicar, idempotentes).
   - Verifica que no queden credenciales de desarrollo ni variables Plataforma__AdminInicial__* en el web.config, ni appsettings.Development.json en uso en producción.
   - Verifica el modo de Datil (DryRun) y que lo apagado (Escritura) siga apagado.
   Entrega la matriz como tabla/Excel, más la lista de hallazgos por gravedad.

D) DISEÑO DE PERFILES EXTERNOS. Antes de proponer, PREGÚNTAME: quiénes serán los usuarios externos (vendedores, contadores de cada empresa, clientes de PSA, gerentes), de qué empresas, qué módulos necesita cada perfil y desde dónde se conectarán. Con eso arma perfiles de mínimo privilegio (rol por perfil y por empresa), cómo se crean/desactivan usuarios, política de altas y bajas, y quién aprueba. Los permisos de Ventas ya tienen un rol propuesto ("Vendedor": quSalesStk + quSalesQte + mkSalesQte; Contabilidad: auSalesQte).

E) DESPLIEGUE Y VERIFICACIÓN. Runbook nuevo en docs/ con backup, orden de pasos, pruebas desde fuera de la red (HTTPS, login+2FA, descarga de PDF, un módulo de solo lectura, un usuario sin permiso recibe 403/404), monitoreo y rollback. Actualiza docs/ESTADO-MIGRACION-WEB.md con el estado real.

## 4. Reglas de trabajo

- Español. Responde con recomendación y razones, no catálogos de opciones.
- Nunca me pidas ni pegues contraseñas, tokens ni cadenas de conexión en el chat. Si necesitas ver algo de configuración, dame el comando para que yo lo corra y te muestre el resultado SIN secretos.
- No escribas en Sage ni cambies datos de producción; los scripts SQL contra PeachEBills/PsaWebPlataforma llevan modo vista previa y los ejecuto yo.
- Para cambios de código: pruebas, `dotnet build PsaWeb.sln` sin errores, y haz commit/push solo cuando yo lo pida (ya tengo la regla de permiso para `git push origin main`). Los zips y scripts de deploy no se commitean; los runbooks y scripts SQL sí.
- Si tocamos web.config: elemento <environmentVariable>, y recuerda que el script de deploy preserva el web.config y logs\.
- Pega los comandos de PowerShell de uno en uno. El servidor es Windows Server 2012 R2 con PowerShell 5.1 (para pruebas de red/TLS fuerza TLS 1.2).
- Al terminar cada fase dime qué quedó hecho, qué verificaste de verdad y qué falta.

## 5. Primeras acciones

1. Lee los documentos y el código de seguridad indicados.
2. Dame un plan por fases con riesgos y las preguntas abiertas (en especial las de la sección D y: ¿hay IP fija pública?, ¿quién administra el router/firewall y el dominio paredes.com.ec?, ¿qué ISP?, ¿cuántos usuarios externos?).
3. Dame los comandos SQL de solo lectura para el inventario de la sección C (usuarios, empresas, roles y permisos) para que los corra en el servidor y te pase los resultados.
```

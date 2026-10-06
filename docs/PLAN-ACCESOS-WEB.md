# Plan: accesos web propios, login con correo y vínculo con Sage

**Fecha:** 2026-10-05 · **Estado:** aprobado. **AW-0 a AW-5 implementados y probados en dev** (2026-10-06, sin commit ni deploy;
ver §11 y §12). Falta el deploy (runbook `docs/DESPLEGAR-ACCESOS-WEB-EN-SERWEBPSA01.md`).
**Contexto:** preparación del acceso externo a «Aplicativos Web PSA» (`docs/PROMPT-ACCESO-EXTERNO.md`). Auditoría de solo lectura del
2026-10-05: `docs/sql/auditoria-accesos-inventario.sql`, `docs/sql/auditoria-accesos-peachebills.sql`. El Excel con la matriz y los
hallazgos está fuera del repo, porque tiene datos personales (carpeta `Usuarios/`, ignorada).

## 1. Por qué

Hoy la web toma empresas y permisos de las tablas del `.exe` en PeachEBills (`UserTransmitter`, `udrUserRolesTr`, `adrAllowRol`). La
auditoría mostró que así no se puede abrir a usuarios externos:

- **Los permisos de PeachEBills van por rol, no por persona.** El rol «Hacer Comprobantes Electrónicos» está asignado 89 veces, y no hay
  forma de dar un módulo a una sola persona en una sola empresa.
- **Los módulos nuevos no tienen llave.** 8 de 11 llaves web no existen en `allowAction`, y `quats` existe sin roles. Kardex, ATS,
  Conciliación SRI y Cierre de Caja están abiertos a cualquiera que tenga una empresa (`GateProvisional`).
- **Las cuentas del `.exe` son genéricas o compartidas** (`peachbAAAA`, `USUARIO2013`, `usuarioildis`), y cada persona tiene 2 a 5.
- **Cambiar PeachEBills desde la web cambiaría también el acceso al `.exe`**, y no se quiere eso.

**Decisión (2026-10-05):** la web tiene su **propia tabla de accesos** en `PsaWebPlataforma`, administrada desde un panel por los Super
Admin y la Admin. PeachEBills queda solo para el `.exe` y como fuente de la siembra inicial.

## 2. Decisiones del usuario que este plan implementa

| Tema | Decisión |
|---|---|
| Cuentas | Una cuenta **personal** por persona. No se migran cuentas genéricas. |
| Login | Con el **correo** que elija cada usuario, confirmado con un código. |
| Identificador interno | `atobar`, `lparedes`, `mmartinez`, `kpaucar`, etc. No cambia nunca. |
| Alcance | 11 empresas: SANCEV, CPTDC, DGRV, EFEMEDIO, DEMORADIO, ILDIS, ROLLERDANCE, PSA, PyP, ANDE y MONICA MARTINEZ (pruebas). |
| Perfiles | Super Admin (Alejandro Tobar, Luis Paredes), Admin (Mónica Martínez), Digitadora general (Karina Paucar), un digitador por empresa, vendedores de SANCEV (hasta ~11). |
| Quién administra | **Super Admin:** crea y desactiva usuarios, asigna accesos, aprueba altas y bajas. **Admin:** activa, desactiva y reasigna empresas y módulos de usuarios existentes; **no crea** usuarios. |
| 2FA | **Obligatorio** para Super Admin y Admin, con «confiar en este dispositivo». **Opcional** para el resto, que elige entre autenticador (TOTP) y código por correo. |
| Siembra | Digitadores: los mismos permisos que tienen hoy en el `.exe`, más Conciliación SRI. Super Admin y Admin: todos los módulos. |
| Escritura en Sage | Cada usuario web queda atado a su **usuario de Sage 50 en cada empresa**, como requisito para el deploy de escritura. |
| Extensión de Chrome | Debe funcionar desde fuera de la red (digitadores en cada oficina). |

## 3. Modelo de datos (`PsaWebPlataforma`, migración EF)

```
AspNetUsers (existente)            + Perfil (SuperAdmin | Admin | Usuario), + EmailConfirmed obligatorio para entrar,
                                     + DebeTener2fa (calculado del perfil), + UltimoAccesoUtc
AccesoEmpresa                      UsuarioId · Ruc · Activo · UsuarioSage (nvarchar 50, null) · ModificadoPor · ModificadoUtc
                                     PK (UsuarioId, Ruc)
AccesoLlave                        UsuarioId · Ruc · Llave (nvarchar 10, mismos códigos de Permisos.cs) · OtorgadoPor · OtorgadoUtc
                                     PK (UsuarioId, Ruc, Llave) · FK a AccesoEmpresa
PlantillaPerfil (en código)        Perfil → lista de llaves por defecto (ver §5)
EventosAuth (existente)            + tipos nuevos: acceso-otorgado, acceso-quitado, empresa-activada, empresa-desactivada,
                                     perfil-cambiado, correo-confirmado, 2fa-correo-ok/fail, login-ip-nueva
```

- **Por qué llaves y no "módulos":** los módulos tienen niveles (ver, emitir, lote, autorizar anulación, Contabilidad de Ventas) y la
  lógica de `ReglasVentas`/`ReglasCompras` ya trabaja con llaves. En el panel se muestran agrupadas por módulo.
- **Cierre de Caja** recibe una llave nueva, `quCierre`. Hoy no tiene ninguna.
- **`UsuarioSage` va en `AccesoEmpresa`** porque los usuarios de Sage 50 son de cada compañía y el nombre cambia entre compañías
  (`ATOBARVALENCIA`, `MONICA MARTINEZ`, `CONT-ANDE`).
- **El identificador interno (`UserName`) no cambia nunca.** Las prefacturas propias del vendedor, la auditoría y la cola del Bridge
  (`CreadoPor`) lo usan.

## 4. Código

### 4.1 Autorización
- Nueva `AccesosWebSecurityDirectory : ISecurityDirectory`, que lee `AccesoEmpresa`/`AccesoLlave`. Es el mismo punto donde hoy se enchufa
  `PeachEbillsSecurityDirectory`, así que los módulos no cambian: siguen pidiendo `PermisosAsync(usuario, ruc)`.
- Interruptor `Accesos:Fuente = PeachEBills | Web` para el corte (§7). Arranca en `PeachEBills`, que es el comportamiento actual.
- `AppCatalogo`: **se elimina el `GateProvisional`**. Cada módulo exige sus llaves y ninguno queda con la lista vacía.
  `ReglasVentas.PermisosProvisionales` y `ReglasCompras.PermisosProvisionales` pasan a `false`.
- Empresa elegida en sesión: se valida contra `AccesoEmpresa` en cada cambio y en cada endpoint de descarga, no solo en la lista del
  selector.

### 4.2 Panel `/admin/accesos`
- **Lista de usuarios:** nombre, correo, perfil, 2FA, último acceso, activo.
- **Ficha del usuario:** grilla de **empresas (filas) por módulos (columnas)**. Cada celda abre los niveles del módulo. Por empresa se
  muestra el **usuario de Sage**, elegido de una lista leída de esa compañía (§6).
- **«Aplicar plantilla»:** llena las casillas según el perfil (§5), y después se ajusta a mano.
- **Super Admin:** alta y baja de usuarios, cambio de perfil, reseteo de clave, quitar 2FA, todo el panel.
- **Admin:** solo edita accesos de usuarios existentes. No crea usuarios, no cambia perfiles y **no puede tocar las cuentas Super Admin
  ni la suya**.
- `Plataforma:Admins` del `web.config` queda solo como **respaldo de emergencia**, si la tabla queda sin ningún Super Admin activo. El
  perfil se lee de la base.
- Cada cambio queda en `EventosAuth`: quién, a quién, qué y desde qué IP. `/admin/auditoria` agrega filtros por usuario y por tipo.

### 4.3 Login con correo y 2FA
- `RequireUniqueEmail = true`. El alta la hace un Super Admin con el correo de la persona y le llega un **enlace o código para
  confirmarlo y definir su clave**. Así nadie ve la clave de otro.
- `/ingresar` pide **correo**. El `UserName` sigue aceptándose solo durante la transición.
- **2FA por correo:** proveedor `Email` de Identity, con un código de 6 dígitos que vale 10 minutos y se envía por el SMTP actual
  (`portalweb@`). En `/mi-cuenta/seguridad` cada usuario elige entre autenticador, correo o ninguno (si su perfil lo permite).
- **2FA obligatorio** (Super Admin y Admin): si no lo tiene activo, después del login se le lleva a `/mi-cuenta/seguridad` y no puede
  usar nada hasta activarlo.
- **«Confiar en este dispositivo» por 30 días:** cookie de dispositivo recordado de Identity. Se invalida al cambiar la clave o al
  quitar el 2FA.
- **Aviso por correo de ingreso desde una IP nueva**, para todos. Es la red de seguridad de quien no tiene 2FA.
- La recuperación de clave por correo queda habilitada, con rate-limit.

### 4.4 Extensión de Chrome (endpoint público)
- Tokens con **vencimiento de 90 días** y **revocables desde el panel**. El token queda atado al usuario y a sus empresas: subir a una
  empresa sin `quconcsri` da 403.
- Rate-limit por token y por IP, tope de tamaño del cuerpo y auditoría con la IP real.

## 5. Plantillas de perfil (siembra y botón «Aplicar plantilla»)

| Perfil | Llaves por defecto |
|---|---|
| Super Admin | Todas: lectura, emisión, lote, anulación, `auSalesQte`, `quCierre`, `quats`, `quKardex`, `quconcsri`, reportes. Las de escritura en Sage (`qupurchinv`, `mkpurchinv`, `quimpliq`, `mkimpliq`) se cargan, pero no hacen nada mientras `Escritura:Habilitada=false`. |
| Admin (Contadora) | Las mismas que Super Admin, con `auSalesQte` (Contabilidad de Ventas) en SANCEV. |
| Digitador/a | Las del rol del `.exe` «Hacer Comprobantes Electrónicos» (`qusaleinv mksaleinv qusalenc mksalenc qupurchliq mkpurchliq qupurchtwh mkpurchtwh quRptPwc quRptComis quRptChq`) más `quconcsri`. Si una persona hoy tiene más (lote, Supervisor), se respeta lo suyo. |
| Vendedor | `quSalesStk quSalesQte mkSalesQte`, solo en SANCEV. |
| Consulta | Ninguna por defecto: se marcan llaves `qu*` caso por caso. |

## 6. Vínculo con el usuario de Sage 50

- **Para qué sirve:** (a) solo puede escribir en Sage quien tiene usuario de Sage en esa compañía. Sin vínculo, los módulos de escritura
  quedan bloqueados para esa empresa. (b) El nombre del usuario de Sage queda en la auditoría web (`AuditoriaRegistroSage`), en la cola
  del Bridge y, donde el documento lo permita, en su memo o referencia.
- **Límite conocido:** el Sage Bridge entra a Sage con la **clave de aplicación del SDK** (`PeachtreeSession.Begin`), no con la sesión de un
  usuario de Sage. Por lo visto en el código, el SDK no permite registrar un documento "como" un usuario de Sage. Se verifica en desarrollo
  antes del deploy de escritura (§8, AW-5). Si resulta posible, se usa.
- **Lista de usuarios de Sage:** se intenta leerla por ODBC de cada compañía, para elegirla en el panel en vez de tipearla. Si Sage no la
  expone por ODBC, se carga a mano y se valida contra lo que informe el Bridge.

## 7. Corte sin que nadie pierda acceso

1. **AW-1/AW-2** se despliegan con `Accesos:Fuente=PeachEBills`, sin cambio de comportamiento. Las tablas nuevas se crean vacías.
2. **Siembra** con un script de vista previa y aplicar, en el estilo de `docs/sql/*.sql`, idempotente. Toma las filas aprobadas de la hoja
   «Propuesta web» del Excel de la auditoría: personas, correos, perfiles, empresas, llaves y usuario de Sage. Crea las cuentas **sin clave**
   y cada persona la define al confirmar su correo.
3. Prueba con `atobar` y un vendedor de prueba, en la red interna.
4. `Accesos:Fuente=Web` y `Restart-WebAppPool`. **Reversa:** volver a `PeachEBills` y reiniciar el pool. No se borra nada.
5. Se desactiva `pruebas1` y se quita `Plataforma:Admins` de la ruta normal (queda solo como respaldo).

## 8. Fases (cada una se aprueba por separado)

| Fase | Contenido | Toca el servidor |
|---|---|---|
| **AW-0** | Higiene inmediata: desactivar `pruebas1`; borrar `appsettings.Development.json` del servidor y excluirlo del publish (`.csproj`). | Sí (comandos tuyos) |
| **AW-1** | Modelo (§3) + `AccesosWebSecurityDirectory` + interruptor `Accesos:Fuente` + quitar `GateProvisional` + llave `quCierre`. Pruebas unitarias de la resolución de permisos y del catálogo. | Deploy con `Fuente=PeachEBills` |
| **AW-2** | Login con correo, confirmación de correo, 2FA por correo, 2FA obligatorio por perfil, dispositivo de confianza, aviso de IP nueva. | Deploy |
| **AW-3** | Panel `/admin/accesos` (Super Admin / Admin) + auditoría ampliada + tokens de la extensión con vencimiento y revocación. | Deploy |
| **AW-4** | Script de siembra (vista previa y aplicar) + corte a `Fuente=Web` (§7). | SQL tuyo + config |
| **AW-5** | Vínculo con Sage: lectura de usuarios de Sage por compañía + verificación en desarrollo de qué puede registrar el SDK por usuario. Requisito para el deploy de escritura. | No (desarrollo) |
| Después | Exposición externa: endurecimiento (ForwardedHeaders, cookie Secure, cabeceras, `/admin` solo desde IPs internas) y Cloudflare Tunnel en `webapp.paredes.com.ec`. Plan aparte. | Sí |

En cada fase de código: pruebas, `dotnet build PsaWeb.sln` sin errores, y commit o push solo cuando lo pida el usuario.

## 9. Riesgos

- **Bloquear a todos en el corte.** Se mitiga con el interruptor `Accesos:Fuente` (reversa en un minuto) y el respaldo `Plataforma:Admins`.
- **Correo como única llave de recuperación.** Si el buzón de alguien se compromete, se compromete su cuenta web. Por eso el 2FA es
  obligatorio en los perfiles con panel y existe el aviso de IP nueva.
- **Envío de códigos 2FA por HostGator.** Si el SMTP falla, quien eligió 2FA por correo no puede entrar. Los códigos de recuperación
  siguen sirviendo, y un Super Admin puede quitar el 2FA (queda auditado).
- **Dos fuentes de verdad.** PeachEBills (`.exe`) y la web pueden quedar desalineadas. Es una decisión explícita: las altas y bajas de la
  web se aprueban en la web.
- **Blazor Server en el móvil.** Las reconexiones en redes móviles son más frecuentes. Antes de abrir a vendedores se revisan Ventas e
  Ingresar en pantallas de teléfono.

## 10. Datos pendientes del usuario (para AW-4)

- **Correo de login** de cada persona.
- **Digitador/a de cada empresa**, con nombre y correo. Candidatos según la auditoría: CPTDC `pseuser01` (Jessica Velastegui), DGRV
  `peachb2026` (L. Bermúdez), EFEMEDIO y DEMORADIO `USUARIO2013` (cuenta compartida), ILDIS `usuarioildis` (cuenta compartida). Sin
  candidato: SANCEV, ROLLERDANCE, PSA, PyP, ANDE.
- **Lista final de vendedores de SANCEV** (la nota del área nombra 8 y la matriz del usuario dice 11 de consulta/vendedor).
- **Usuario de Sage 50 de cada persona en cada empresa.**
- **Si la empresa «MONICA MARTINEZ (Pruebas)» necesita digitador.**

## 11. Estado de la implementación (2026-10-05)

**AW-0 a AW-3 hechos en dev**, compilando (`dotnet build PsaWeb.sln` sin errores) y con toda la solución en verde (0 fallos). Runbook:
`docs/DESPLEGAR-ACCESOS-WEB-EN-SERWEBPSA01.md`.

Hallazgos que aparecieron al implementar y quedaron corregidos:

- **Los módulos no validaban el permiso**: el menú solo los escondía. Escribir `/kardex` a mano entraba igual. Ahora hay una guardia
  central (`GuardiaModulo` en el layout + `ExigirModulo(...)` en las 11 descargas) que usa la misma regla que el menú
  (`AppCatalogo.VisiblePara`, ruta → módulo con `AppCatalogo.ModuloDeRuta`).
- **Descargas sin empresa**: sin `?ruc=` exportaban la empresa fija de `Sage50:ConnectionString`. Ahora responden 400.
- **Concurrencia de `UserManager` en Blazor Server**: el panel lo usa por operación en un scope propio (`ServicioAccesos`).

Decisiones de implementación:

- Con `Accesos:Fuente=PeachEBills` (lo que queda desplegado hasta AW-4) los módulos con `GateProvisional` siguen abiertos como hoy
  (`AppCatalogo.ModoProvisional`); con `Web` se cierran todos salvo llave marcada. Cierre de Caja tiene llave nueva `quCierre` (solo en
  la tabla web).
- El correo es único con un índice filtrado (las cuentas viejas sin correo siguen valiendo); no se usa `RequireUniqueEmail` de Identity
  porque invalidaría cualquier actualización de esas cuentas.
- Perfil efectivo: el guardado en `AspNetUsers.Perfil`, salvo `Plataforma:Admins` (respaldo de emergencia) = Super Admin.
- Las sesiones se re-validan cada 5 min (cookie y circuitos de Blazor): deshabilitar una cuenta o cambiarle el perfil la saca en ≤ 5 min.
- **Scripts SQL sobre `AspNetUsers` (siembra de AW-4)**: el índice único filtrado del correo exige `QUOTED_IDENTIFIER ON`; `sqlcmd`
  lo trae en OFF por defecto → correrlos con `sqlcmd ... -I` (o `SET QUOTED_IDENTIFIER ON;` al inicio). Sin eso el `UPDATE` falla con
  el error 1934 (verificado en PREDATOR). La app no se ve afectada (EF lo usa en ON).
- Pendiente para la fase de exposición: detrás de un proxy/túnel la IP de la auditoría y del rate-limit será la del proxy hasta
  configurar `ForwardedHeaders`.

## 12. AW-4 y AW-5 (2026-10-06)

**Siembra (AW-4).** `docs/sql/accesos-web-siembra.sql` (vista previa / aplicar, idempotente, solo agrega) + los datos en
`accesos-web-datos.sql`, generados por `docs/sql/generar-datos-siembra.ps1` desde `Usuarios\Matriz usuarios WebApps PSA.xlsx` (hoja con
NOMBRE / USUARIO WEB / CORREO / USUARIO SAGE / USUARIO RDP WINSERVER / EMPRESA / PERFIL; las columnas de claves se ignoran). Los datos
personales nunca entran al repo (una prueba verifica que el SQL versionado no tenga correos). Mapeo de la matriz:

| PERFIL en la matriz | Perfil web | Llaves |
|---|---|---|
| SUPERADMIN | Super Admin | todas (12 empresas si dice TODAS) |
| ADMIN | Admin | todas |
| DIGITADOR / DIGITADOR PSA | Usuario | las de hoy en el .exe del «USUARIO RDP WINSERVER» (= usuario de PeachEBills) + Conciliación SRI; si en una empresa el .exe no le da nada, plantilla Digitador/a |
| VENDEDOR | Usuario | inventario + prefacturas propias + emitir |

Matriz al 2026-10-06: 11 cuentas (Luis Paredes y Alejandro Tobar Super Admin; Mónica Martínez Admin; Carina Paucar digitadora en las 12;
7 digitadores por empresa), 56 asignaciones. **Faltan los vendedores de SANCEV** (se agregan a la matriz y se vuelve a correr: es
idempotente). Avisos de la vista previa a decidir: el .exe hoy da **autorizar anulaciones (Supervisor)** a Carina Paucar (casi todas las
empresas) y a Verónica Paredes (PyP) → se copian igual («los mismos de hoy»); quitarlos en el panel si no corresponde. Ricardo Sandoval no
tiene usuario de Sage ni del .exe → plantilla Digitador/a y no podrá registrar en Sage.

Corte: `activar-accesos-web.ps1` (agrega `Accesos__Fuente=Web` al web.config con respaldo, exige al menos un Super Admin con empresas,
`-Revertir` lo quita). Invitaciones: `Configuración → Usuarios → Enviar invitaciones pendientes` (después del corte).

**Usuario de Sage (AW-5).**

- Sage 50 **no expone su tabla de usuarios** por ODBC (solo `Roles` = roles y aplicaciones del SDK, y `X$User` = usuarios del motor
  Pervasive). Sí está `UserPreference.UserID`: cada usuario que entró alguna vez a la compañía — coincide con la matriz (ASISTENTE-1,
  ATOBARVALENCIA, CONT-ANDE, GERENCIA-1, LUISPAREDES, MONICA MARTINEZ…; verificado en el SANCEV de PREDATOR). Lo lee
  `LectorUsuariosSageOdbc` (caché 10 min) para el desplegable y el «✔ / ⚠» de la ficha («Validar usuarios de Sage»). Un usuario que nunca
  entró a esa compañía sale «⚠ no aparece» pero se puede guardar igual.
- `ISecurityDirectory.VinculoSageAsync`: con la fuente PeachEBills no se exige (como hoy); con Web, Compras / Facturas recibidas /
  Liquidación de importaciones solo registran si la cuenta tiene usuario de Sage en esa empresa. Se **revalida en el servidor** al
  guardar/encolar (antes solo la página lo ocultaba) y el usuario de Sage queda en `AuditoriaRegistroSage.Usuario` («mmartinez (Sage:
  MONICA MARTINEZ)»).
- Límite confirmado: el Bridge sigue escribiendo con la clave de aplicación del SDK; el documento en Sage no queda «a nombre» del usuario
  de Sage. El vínculo es el candado y la trazabilidad.

Probado en PREDATOR con la fuente Web: siembra aplicada dos veces (sin duplicar datos ni auditoría), ingreso por correo de un Super Admin
(2FA obligatorio) y de un digitador (solo su empresa y sus 5 módulos; Kardex y /admin «Sin acceso»), «Validar usuarios de Sage»
(ASISTENTE-1 ✔ en SANCEV, un usuario inventado ⚠).

**Textos en tuteo** (2026-10-06, pedido del usuario): todo el sitio, los mensajes de validación, el Bridge y la extensión de Chrome
(voseo y usted → tú).

**Perfil Supervisor (decisión del usuario 2026-10-06).** Nivel entre Digitador/a y Admin: lo que le da hoy el .exe + Conciliación SRI +
**autorizar anulaciones** (`auCanceInv/Nc/Liq/Twh`) en todas sus empresas. En la matriz: PERFIL `SUPERVISOR` o `DIGITADOR PSA` (Carina
Paucar). Sin panel ni 2FA obligatorio (perfil web «Usuario»). Verónica Paredes queda Digitadora y conserva las anulaciones que le da el .exe
en PyP. Plantilla «Supervisor» en el panel (llega con el próximo deploy del sitio; la siembra no lo necesita). Recordatorio: el usuario de
Sage solo se exige para registrar en Sage; los vendedores (inventario + prefacturas) no lo necesitan.

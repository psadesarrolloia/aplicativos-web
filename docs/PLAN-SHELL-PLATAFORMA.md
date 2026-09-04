# Plan — Shell de la plataforma web

El shell es la cáscara común por la que se entra a **todos** los aplicativos web:
bienvenida → login → selección de empresa → inicio con las apps habilitadas.
Reemplaza la autenticación Windows/RDS del piloto y da a cada app el contexto de
**empresa + ambiente** que hoy los aplicativos de escritorio piden al arrancar
(`FrmEmpresaSeleccionar`).

## Decisiones tomadas (2026-09-03)

| Tema | Decisión |
|---|---|
| Autenticación | **ASP.NET Core Identity local**, detrás de `IProveedorAutenticacion`. Lockout + rate-limiting + **MFA TOTP** activados desde el día uno. Keycloak queda como fase futura (swap de la implementación de la interfaz). |
| Login | Bienvenida → *Ingresar* → formulario usuario/clave (+ TOTP). Sin SSO de Windows. |
| Autorización (empresas + apps por usuario) | **Reutilizar las tablas de `PeachEBills`** (`users`, `UserTransmitter`, `roles`, `udrUserRolesTr`, `adrAllowRol`, `allowAction`) detrás de `ISecurityDirectory`. Para apps nuevas se **agregan códigos** a `allowAction`, no se cambia el esquema. |
| Clave de usuario | El `UserName` del login local == `users.username` de PeachEBills (sAMAccountName pelado). Así los lookups de `ISecurityDirectory` no cambian. |
| Cambio de empresa | Selector en la barra superior, sin volver a loguear. |
| Ambiente (Pruebas/Producción) | Toggle por sesión junto a la empresa, visible/cambiable en la barra superior. |
| Store de Identity | BD dedicada **`PsaWebPlataforma`** (misma instancia SQL de `SERWEBPSA01`), separada de `PeachEBills`. |

## Contexto de infraestructura

- `SERWEBPSA01` (192.168.0.11): IIS (piloto en :8088) + SQL Server (`PeachEBills`).
  El shell y su BD `PsaWebPlataforma` viven acá.
- Auth 100% dentro de la app ASP.NET (sin servicio externo por ahora).
- Keycloak (fase futura): servicio Windows en SERWEBPSA01/Peach2015 + BD propia en
  el mismo SQL + IIS ARR como reverse proxy. Licencia $0. Ver comparativa en el
  historial de decisiones.

## Fases

### F-Shell-0 — Fundaciones (no toca el piloto)
- BD `PsaWebPlataforma`: tablas de ASP.NET Core Identity + `AppRegistry`
  (id, nombre, icono, ruta, códigos de permiso requeridos) + `AuthEvent` (auditoría).
- `src/PsaWeb.Identidad`: setup de Identity, `IProveedorAutenticacion` +
  `IdentityLocalProveedor`. Política de claves, `LockoutOptions`, `AddRateLimiter`
  en los endpoints de login, 2FA TOTP.
- `src/PsaWeb.Seguridad`: `ISecurityDirectory`
  (`EmpresasDelUsuarioAsync`, `TieneAccesoAsync(user, ruc, código)`,
  `AppsHabilitadasAsync(user, ruc)`) + `PeachEbillsSecurityDirectory` (lee
  `UserTransmitter` / `udrUserRolesTr` / `adrAllowRol`).
- `EmpresaActualService` (estado del circuito Blazor): RUC + ambiente
  seleccionados, con evento de cambio.

### F-Shell-1 — Pantallas
- `/bienvenida` — landing pública (logo, mensaje).
- `/ingresar` — formulario de login + challenge TOTP + mensajes de lockout.
  Anti-forgery, rate-limited.
- `/seleccionar-empresa` — tras login, lista de `EmpresasDelUsuarioAsync` + radio
  de ambiente. Si el usuario tiene 1 sola empresa, se auto-selecciona y se saltea.
- `/` (Inicio) — dashboard: grilla de íconos de `AppsHabilitadasAsync(user, empresa)`.
- Guard: usuario autenticado sin empresa elegida → redirige a `/seleccionar-empresa`.

### F-Shell-2 — Layout y navegación dinámica
- Barra superior: marca, nav dinámico (apps habilitadas), switcher de
  empresa + ambiente, menú de usuario (configurar 2FA, salir).
- `NavMenu` pasa a leer de `AppRegistry` filtrado por `AppsHabilitadasAsync`.
- Badge de ambiente (Pruebas en color de aviso).

### F-Shell-3 — Re-cablear piloto y Retenciones
- **Cierre de Caja**: resuelve la conexión ODBC de Sage desde el RUC de
  `EmpresaActualService` vía `PeachConnStringResolver`, en vez de la cadena fija
  de config. Fallback a config para dev sin shell.
- **Retenciones**: la página se acota a la empresa de sesión (su fila del
  panorama + pendientes). «Ejecutar ahora» → `ProcesadorRetenciones.ProcesarEmpresaAsync`
  solo de esa empresa. El worker cross-company sigue igual. Vista "todas las
  empresas" separada, detrás de un código de permiso.
- Registrar ambas apps en `AppRegistry` con sus códigos.

### F-Shell-4 — Administración mínima + auditoría
- `/admin/usuarios` (con permiso): crear login local, vincular al `users.username`
  de PeachEBills, resetear clave, habilitar/deshabilitar, forzar 2FA.
- Registro de `AuthEvent` (login ok/fallido, lockout, cambio de empresa).
- La asignación de empresas/roles sigue en la administración existente de
  PeachEBills — acá solo se gestiona la **credencial de acceso web**.

### F-Shell-5 (futuro) — Keycloak
- Cambiar la implementación de `IProveedorAutenticacion` a OIDC/Keycloak.
- Levantar Keycloak (servicio Windows + BD + ARR). Migrar el enrolamiento de 2FA.

## Riesgos / notas

- **SPOF de login**: con auth local en la app no hay servicio externo que falle;
  las sesiones activas sobreviven a un reinicio del host.
- **Hardening no negociable en F-Shell-0**: lockout, rate-limiting y TOTP van
  desde el inicio, no "después".
- **Match de identidad**: si algún `users.username` no es el sAMAccountName pelado,
  hace falta una columna de mapeo en `PsaWebPlataforma`.
- **Orden**: hacer el shell **antes** de portar `Sage50FacturacionElectronica`
  (Ola 1 #2), que es justamente la app que se entra por selección de empresa.

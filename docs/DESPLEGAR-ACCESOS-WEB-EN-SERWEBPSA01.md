# Desplegar accesos web (AW-0 a AW-5) en `SERWEBPSA01`

Plan: `docs/PLAN-ACCESOS-WEB.md`. Redeploy del **mismo sitio** (`CierreDeCaja`, `http://192.168.0.11:8088/`) y después el corte a la
tabla de accesos web. La escritura en Sage sigue **apagada** en todo momento.

## Qué trae

| Cambio | Efecto |
|---|---|
| **Guardia central de módulos** (páginas y descargas) | Escribir la ruta a mano (`/kardex`) o pedir una descarga ya no se saltea el permiso; las descargas sin `?ruc=` responden 400 (antes exportaban la empresa fija del web.config). |
| **Login con correo** | La pantalla pide «Correo» (el usuario interno sigue sirviendo). «¿Olvidaste tu contraseña?» manda un enlace. |
| **2FA** | Por app (TOTP) o por código al correo; acepta códigos de recuperación. **Obligatorio para Super Admin y Admin**. «Confiar en este dispositivo» 30 días. Aviso por correo al ingresar desde una IP nueva. |
| **Panel** | `Configuración → Usuarios` (Super Admin): alta sin clave + invitación, «Enviar invitaciones pendientes», perfil, correo, habilitar/deshabilitar, quitar 2FA, clave temporal. `Configuración → Accesos` (Super Admin y Admin): empresas × módulos, plantillas, «Copiar del .exe», **usuario de Sage por empresa con «Validar usuarios de Sage»**, token de la extensión. `Auditoría` con filtros. |
| **Usuario de Sage (AW-5)** | Con la fuente Web, nadie registra en Sage en una empresa donde su cuenta no tiene usuario de Sage (se revalida en el servidor) y el usuario de Sage queda en la auditoría de lo que se manda al Bridge. La lista de usuarios de Sage sale de `UserPreference` de cada compañía (usuarios que entraron alguna vez). |
| **Extensión de Chrome** | Token vence a los 90 días, exige la llave de Conciliación en la empresa, 30 subidas/min por IP, cuerpo hasta 10 MB. Textos en tuteo: `PSA-ConciliacionSri-Extension.zip` versión 0.2.1 (reinstalarla es opcional, solo cambian textos). |
| **Textos** | Todo el sitio en tuteo (antes voseo / usted). |
| **Base `PsaWebPlataforma`** | Migración `AccesosWeb` (se aplica sola al arrancar): `Perfil`, `MetodoSegundoFactor`, `UltimoAccesoUtc` en `AspNetUsers`; tablas `AccesosEmpresa` y `AccesosLlave`; índice único del correo. |
| **AW-0** | `appsettings.Development.json` ya no viaja en el paquete; el `robocopy /MIR` lo borra del sitio. |

## Archivos a copiar a `C:\Deploy`

| Archivo | De dónde (PREDATOR) | Se commitea |
|---|---|---|
| `PsaWeb.Host-publish-accesos.zip` (61 MB) | raíz del repo | no |
| `deploy-accesos-web.ps1` | raíz del repo | no |
| `activar-accesos-web.ps1` | raíz del repo | no |
| `accesos-web-siembra.sql` | `docs\sql\` | sí |
| `accesos-web-datos.sql` | `Usuarios\` (generado con `docs\sql\generar-datos-siembra.ps1` desde la matriz; **datos personales, sin claves**) | **no** |

Si la matriz cambia (p. ej. se agregan los vendedores de SANCEV), regenerar los datos en PREDATOR antes de copiar:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\PROYECTOS IA\Aplicativos web\docs\sql\generar-datos-siembra.ps1"
```

## Pasos en el servidor (PowerShell como Administrador, uno por uno)

**1. Deploy** (backup de `PsaWebPlataforma` + sitio; deja la fuente en PeachEBills, sin cambio para nadie):

```powershell
powershell -ExecutionPolicy Bypass -File C:\Deploy\deploy-accesos-web.ps1
```

**2. Siembra en vista previa** (no cambia nada; revisar las 3 tablas: cuentas, empresas/llaves, avisos):

```powershell
cd C:\Deploy
```
```powershell
sqlcmd -S localhost -d PsaWebPlataforma -E -C -I -f 65001 -W -s "|" -v Aplicar=0 -i accesos-web-siembra.sql -o siembra-vista-previa.txt
```

**3. Siembra aplicada** (cuando la vista previa esté bien):

```powershell
sqlcmd -S localhost -d PsaWebPlataforma -E -C -I -f 65001 -W -s "|" -v Aplicar=1 -i accesos-web-siembra.sql -o siembra-aplicada.txt
```

`-I` es obligatorio (sin él falla con el error 1934 por el índice filtrado del correo). Es idempotente: se puede volver a correr; solo
agrega, nunca quita lo que se haya dado desde el panel.

**4. Corte a la tabla web** (respalda el web.config, verifica que haya un Super Admin con empresas y reinicia el pool):

```powershell
powershell -ExecutionPolicy Bypass -File C:\Deploy\activar-accesos-web.ps1
```

**5. Verificar con `lparedes`** (desde un PC de la oficina): entrar con **su correo** + su clave + el código de su app →
selector con las 12 empresas → módulos completos. `Configuración → Accesos → Luis Paredes → Validar usuarios de Sage`: `LUISPAREDES`
debería salir «✔ existe en esta compañía» donde haya entrado alguna vez.

**6. Invitaciones**: `Configuración → Usuarios → Enviar invitaciones pendientes`. A cada persona le llega el enlace (24 h) para definir su
clave; Alejandro y Mónica tendrán que activar el 2FA al entrar.

## Pruebas después del corte

1. Un digitador (p. ej. Daniel Chiriboga) entra con su correo, ve solo SANCEV y sus módulos; `/kardex` escrito a mano muestra «Sin acceso».
2. `/admin/accesos` con ese digitador: «Sin acceso».
3. Descargar un Excel de un módulo permitido: funciona. La misma URL con el `ruc` de otra empresa: 403.
4. `Configuración → Auditoría`: aparecen la siembra (`siembra-AW4`), las invitaciones y los ingresos con su IP.

## Volver atrás

- **Solo el corte** (en un minuto, sin perder nada): `powershell -ExecutionPolicy Bypass -File C:\Deploy\activar-accesos-web.ps1 -Revertir`.
- **Sitio**: `Stop-WebAppPool CierreDeCaja; robocopy "C:\Deploy\backup-CierreDeCaja-<fecha>" "C:\inetpub\CierreDeCaja" /MIR /XF web.config /XD logs; Start-WebAppPool CierreDeCaja`.
- **Base** (normalmente no hace falta: las tablas nuevas no molestan al código viejo): `RESTORE DATABASE [PsaWebPlataforma] FROM DISK = N'PsaWebPlataforma-antes-de-AccesosWeb-<fecha>.bak' WITH REPLACE` con el sitio detenido.

## Log esperado al arrancar

```
PsaWebPlataforma: migraciones aplicadas.
Accesos (empresas y permisos): fuente PeachEBills (tablas del .exe, con GateProvisional).     <- después del paso 1
Accesos (empresas y permisos): fuente Web (tabla web, sin GateProvisional).                  <- después del paso 4
```

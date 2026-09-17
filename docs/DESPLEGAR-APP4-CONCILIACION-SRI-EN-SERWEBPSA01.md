# F5 — Desplegar Conciliación SRI (app #4) en `SERWEBPSA01`

Es un **redeploy del mismo sitio** que el shell (`C:\inetpub\CierreDeCaja`,
`http://192.168.0.11:8088/`). Este build agrega el módulo **Conciliación
SRI**: página `/conciliacion-sri`, el endpoint de subida del reporte
(`POST /conciliacion-sri/api/comprobantes`), la pantalla `/mi-cuenta/extension`
y el worker de verificación de estado.

> Base: `docs/DESPLEGAR-SHELL-EN-SERWEBPSA01.md` + `docs/DESPLEGAR-APP3-ATS-EN-SERWEBPSA01.md`.
> Todo lo de IIS, `PsaWebPlataforma`, app pool 32-bit, anónima ON / Windows
> OFF, permisos SQL del app pool sobre `PeachEBills` **ya está** y no cambia.
> Este runbook es solo el delta.

---

## 1. Qué es nuevo respecto a los deploys anteriores

A diferencia de ATS/FE (que no necesitaron nada nuevo de base de datos), este
trae **2 tablas nuevas** — pero **sin ningún paso manual de SQL**: se crean
solas al arrancar el sitio.

- `TokensExtension` (en `PsaWebPlataforma`, junto a las tablas de Identity).
- `ComprobantesSriDescargados` (en la misma base física `PsaWebPlataforma`,
  `DbContext` propio) — el staging de comprobantes que sube la extensión de
  Chrome.

Las migraciones de ambas se aplican automáticamente al arrancar (mismo
mecanismo que ya usa `PsaWebPlataforma` desde el deploy del shell,
`Plataforma:MigrarAlArrancar`, default `true`) — no hace falta correr nada a
mano en SQL para esto.

**Sin variables de entorno nuevas obligatorias.** El worker de verificación
arranca solo (`Habilitado=true` por defecto) con sus propios valores por
defecto (intervalo 8h, ventana 90 días). Si hiciera falta ajustar algo o
apagarlo de emergencia, agregar al `web.config` del sitio:

```xml
<environmentVariables>
  <add name="Conciliacion__Worker__Habilitado" value="false" />
  <!-- o para ajustar el intervalo: -->
  <add name="Conciliacion__Worker__Intervalo" value="04:00:00" />
</environmentVariables>
```

## 2. Paquete de publicación

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
git pull                     # trae F1..F5 de Conciliación SRI
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish
Compress-Archive -Path publish\* -DestinationPath PsaWeb.Host-publish.zip -Force
```

Copiar `PsaWeb.Host-publish.zip` y `deploy-app4-conciliacion-sri.ps1` a
`C:\Deploy` en el server (USB / recurso compartido).

En `SERWEBPSA01`, correr `C:\Deploy\deploy-app4-conciliacion-sri.ps1` (hace
todo el §3 solo).

---

## 3. Qué hace el script (`deploy-app4-conciliacion-sri.ps1`)

1. Extrae el zip a una carpeta de staging aparte (`C:\Deploy\staging-conciliacion-sri`).
2. Para el **app pool** `CierreDeCaja` (no alcanza con parar solo el sitio —
   con "Always Running" el pool mantiene `aspnetcorev2_inprocess.dll` en uso
   y `Remove-Item`/`ExtractToDirectory` fallarían a mitad de camino, gotcha
   ya documentado del deploy de ATS).
3. `robocopy /MIR /XF web.config /XD logs` del staging al sitio real — el
   `web.config` con las variables de entorno reales y la carpeta `logs\` del
   sitio **nunca se tocan**, no hace falta un paso manual de "acordate de
   restaurar el web.config".
4. Prende el app pool y el sitio de nuevo.

---

## 4. Permiso `quconcsri` — igual que nació `quKardex`/`quats`

`GateProvisional`: la fila está en `AppCatalogo.Todas` con lista de permisos
vacía (visible para cualquier empresa) hasta que el área cargue el código en
`allowAction`. Si el área quiere gatearlo de verdad más adelante:

```sql
USE [PeachEBills];
-- 1. Verificar si ya existe (no debería, es la primera vez):
SELECT * FROM allowAction WHERE allowAction = 'quconcsri';

-- 2. Si no existe, insertarlo (ajustar aid al siguiente disponible):
-- INSERT INTO allowAction (aid, allowAction, allowName) VALUES (<siguiente_aid>, 'quconcsri', 'Ver Conciliación SRI');

-- 3. Asignarlo a los roles que correspondan (ejemplo, ajustar rol real):
-- INSERT INTO adrAllowRol (aid, rid) VALUES (<aid_de_quconcsri>, <rid_del_rol>);
```

Después de cargar las filas, revertir en `AppCatalogo.cs`:
`Array.Empty<string>()` → `new[] { Permisos.VerConciliacionSri }` para la
entrada `conciliacion-sri`.

## 5. Extensión de Chrome — instalación aparte, no es parte de este deploy

La extensión (`extension/` en el repo) se instala directo en el/los PC de
la(s) contadora(s), no en el servidor:

1. Copiar la carpeta `extension/` al PC.
2. `chrome://extensions` → activar "Modo de desarrollador" → "Cargar
   descomprimida" → apuntar a la carpeta.
3. Click en el ícono de la extensión → pegar el host de PSA
   (`http://192.168.0.11:8088`) y el token generado en
   `/mi-cuenta/extension` (ver §15.1 del plan para la ruta de escalamiento a
   distribución por política de Chrome si hace falta más adelante).

Ver `extension/README.md` para el detalle paso a paso.

## 6. Smoke test

- `http://192.168.0.11:8088/` → login → empresa → **Conciliación SRI**
  (categoría Impuestos) → la página carga, el filtro de período funciona.
- `/mi-cuenta/extension` → genera un token, lo muestra una sola vez.
- Con la extensión configurada: subir un reporte real de prueba → aparece el
  resumen (`N nuevos, M ya existían`) → en la página, las filas nuevas
  aparecen en alguna de las 5 categorías.
- Log del sitio: confirmar las líneas "Conciliación SRI: módulo ACTIVO" y
  "Worker de verificación de estado SRI ACTIVO".

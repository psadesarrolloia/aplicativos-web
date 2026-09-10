# F5 — Desplegar Facturación Electrónica (app #2) en `SERWEBPSA01`

Es un **redeploy del mismo sitio** que el shell (`C:\inetpub\CierreDeCaja`,
`http://192.168.0.11:8088/`). Este build agrega el módulo **Facturación
Electrónica** (Facturas de venta / Notas de crédito / Liquidaciones de compra) +
`PsaWeb.Notificaciones` (SMTP para "Solicitar anulación"). Incluye además el fix
del ícono de Retenciones en el dashboard (`f4fd679`) que quedó pendiente del
deploy anterior.

> Base: `docs/DESPLEGAR-SHELL-EN-SERWEBPSA01.md`. Todo lo de IIS, `PsaWebPlataforma`,
> app pool 32-bit, anónima ON / Windows OFF, permisos SQL del app pool sobre
> `PeachEBills` (`db_datareader` **y `db_datawriter`**) **ya está** y no cambia.
> Este runbook es solo el delta.

---

## 1. Paquete de publicación (en PREDATOR)

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
git checkout main            # ya trae F1..F4b (13 commits sobre f4fd679)
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish
Compress-Archive -Path publish\* -DestinationPath PsaWeb.Host-publish.zip -Force
```

`PsaWeb.Host-publish.zip` ≈ 49 MB. Copiar al server (USB / recurso compartido).

En `SERWEBPSA01`:

```powershell
Stop-WebSite CierreDeCaja        # o Stop-WebAppPool CierreDeCaja
$dst = "C:\inetpub\CierreDeCaja"
Copy-Item $dst "$dst.bak-$(Get-Date -f yyyyMMdd-HHmm)" -Recurse
# PS del server es 4.0 (sin Expand-Archive): usar .NET
Add-Type -AssemblyName System.IO.Compression.FileSystem
Get-ChildItem $dst -Recurse | Remove-Item -Recurse -Force
[IO.Compression.ZipFile]::ExtractToDirectory("C:\Deploy\PsaWeb.Host-publish.zip", $dst)
New-Item -ItemType Directory -Force "$dst\logs" | Out-Null
```

**Conservar el `web.config` viejo** (tiene las env vars del shell): antes de borrar
la carpeta, copiar `web.config` aparte y volver a ponerlo (o re-agregar las
variables — ver §3). El zip trae un `web.config` genérico sin las env vars del sitio.

---

## 2. Base de datos — nada nuevo que crear

Las tablas que usa FE (`Facturas`, `Details`, `NCdetail`, `Payments`,
`PaymentTypes`, `FacturaPropiedadExterna`, `InvoiceConfigAditionalInfo`,
`dicTaxRate`) **ya existen** en el `PeachEBills` real (los `.exe` de escritorio las
usan). El build web NO las migra. El app pool ya tiene `db_datawriter` en
`PeachEBills` (lo agregó el deploy de Retenciones) → FE escribe en las mismas
tablas, cubierto.

### 2.1 Acceso de `lparedes` a las empresas de prueba (opcional)

Si querés probar con SANCEV / DGRV / Roller Dance y `lparedes` no las tiene
asignadas en el server:

```powershell
sqlcmd -S localhost -d PeachEBills -E -i "C:\Deploy\lparedes-empresas.sql"
```

(idempotente; el `.sql` está en `docs/sql/lparedes-empresas.sql`).

### 2.2 Permisos de liquidaciones (opcional, para más adelante)

`/fe/liquidaciones` hoy se ve **sin control de permiso** (código `GateProvisional`).
Para gatearla de verdad hay que crear los códigos y asignarlos a los roles:

```sql
USE [PeachEBills];
IF NOT EXISTS (SELECT 1 FROM allowAction WHERE allowCode='qupurchliq')
  INSERT INTO allowAction (allowName, allowCode) VALUES (N'Ver Liquidaciones de compra', 'qupurchliq');
IF NOT EXISTS (SELECT 1 FROM allowAction WHERE allowCode='mkpurchliq')
  INSERT INTO allowAction (allowName, allowCode) VALUES (N'Hacer Liquidacion de compra', 'mkpurchliq');
-- luego, por cada rol que deba tenerlos (ej. rol 6 "Hacer Comprobantes Electronicos"):
-- INSERT INTO adrAllowRol (adrRol, adraid, adrAllowCode, adrActive)
--   SELECT 6, aid, allowCode, 1 FROM allowAction WHERE allowCode IN ('qupurchliq','mkpurchliq');
```

Después de eso, quitar `GateProvisional="true"` de `Pages/Liquidaciones.razor` y
volver a la lista de permisos real en `AppCatalogo` (fe-liquidaciones), redeploy.

---

## 3. Variables de entorno del sitio (delta)

En `web.config` del sitio, dentro de `<aspNetCore><environmentVariables>`, dejar
**todas las del shell** y agregar (todas **opcionales**):

```xml
<!-- FE: código de forma de pago para el pago al contado (default "20"). Solo si difiere. -->
<!-- <environmentVariable name="FacturacionElectronica__FormaPagoContado" value="20" /> -->

<!-- SMTP para "Solicitar anulación de retención". Sin esto, el botón avisa
     "correo no configurado" y no envía. Remitente fijo: anulaciones@paredes.com.ec -->
<!-- <environmentVariable name="Correo__Servidor" value="smtp.paredes.com.ec" /> -->
<!-- <environmentVariable name="Correo__Puerto"   value="587" /> -->
<!-- <environmentVariable name="Correo__Usuario"  value="anulaciones@paredes.com.ec" /> -->
<!-- <environmentVariable name="Correo__Clave"    value="<clave>" /> -->
<!-- <environmentVariable name="Correo__Ssl"      value="false" /> -->
```

**NO** poner `PeachEbills__SageServerNameOverride` en el server (Sage se alcanza
directo desde ahí; ese override es solo para dev en PREDATOR).

`Datil__DryRun` sigue en `true` (ya está). **Nada de emisión real hasta migrar
los 26 aplicativos.**

---

## 4. Arranque y smoke test

```powershell
Start-WebSite CierreDeCaja      # + Restart-WebAppPool CierreDeCaja
```

Log esperado (además del de Retenciones): sin errores de arranque; el módulo FE se
registra junto con `PeachEbills`. No hay línea propia de log — se ve porque las
rutas `/fe/*` resuelven.

Desde un PC de la red (`http://192.168.0.11:8088/`), con `lparedes` + 2FA:

1. Dashboard: aparecen **FE · Facturas**, **FE · Notas de crédito**,
   **FE · Liquidaciones** (según permisos del usuario en la empresa; liquidaciones
   siempre visible por ahora). El ícono de Retenciones ya es 📄 (no 🧾 roto).
2. Elegir una empresa real (ambiente **Pruebas**). Entrar a **FE · Facturas**:
   - Banner **DRY-RUN activo**.
   - "Facturas de venta": la lista de **guardados** carga (lee `Facturas` real).
   - Rango de fechas + tildar **"Incluir pendientes de emitir"** → lista los
     pendientes de Sage 50 (acá sí funciona, el server ve las bases Zen).
   - Clic en una fila guardada → **popup** con detalle + "Ver PDF" / "Ver XML".
   - "Generar" en un pendiente → **"dry-run OK"** (no envía, no guarda).
   - "Procesar lote" → resumen con todo en dry-run.
3. Repetir en **Notas de crédito** y **Liquidaciones**.
4. **Retenciones**: en el popup de una retención emitida, el botón nuevo
   **"Solicitar anulación"** — si no hay SMTP configurado, avisa
   "correo no configurado" (esperado).

---

## 5. Rollback

```powershell
Stop-WebSite CierreDeCaja
$dst = "C:\inetpub\CierreDeCaja"
Get-ChildItem $dst -Recurse | Remove-Item -Recurse -Force
Copy-Item "C:\inetpub\CierreDeCaja.bak-<fecha>\*" $dst -Recurse
Start-WebSite CierreDeCaja
```

La BD no cambia en este deploy, así que el rollback es solo de archivos.

---

## 6. Pendiente tras el deploy

- Borrar el `.bak-*` cuando haya confianza.
- Puesta en marcha real de FE (`Datil__DryRun=false`) — **no** hasta terminar la
  migración de los 26 apps (decisión 2026-09-07).
- Si se quiere gatear liquidaciones: §2.2 + quitar `GateProvisional`.
- SMTP para la solicitud de anulación (§3) cuando el área lo defina.

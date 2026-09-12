# F6 — Desplegar ATS (app #3) en `SERWEBPSA01`

Es un **redeploy del mismo sitio** que el shell (`C:\inetpub\CierreDeCaja`,
`http://192.168.0.11:8088/`). Este build agrega el módulo **ATS** (Anexo
Transaccional Simplificado): página `/ats` con las 7 pestañas de solo lectura,
"Revisar ATS" (validaciones), exportación del XML y del Talón Resumen en PDF.

> Base: `docs/DESPLEGAR-SHELL-EN-SERWEBPSA01.md` +
> `docs/DESPLEGAR-APP2-FE-EN-SERWEBPSA01.md`. Todo lo de IIS, `PsaWebPlataforma`,
> app pool 32-bit, anónima ON / Windows OFF, permisos SQL del app pool sobre
> `PeachEBills` **ya está** y no cambia. Este runbook es solo el delta.

---

## 1. Paquete de publicación (ya generado en PREDATOR)

```powershell
cd "C:\PROYECTOS IA\Aplicativos web"
git checkout main            # trae F1..F6 de ATS (commit a176401)
dotnet publish src/PsaWeb.Host -c Release -r win-x86 --self-contained true -o publish
Compress-Archive -Path publish\* -DestinationPath PsaWeb.Host-publish.zip -Force
```

`PsaWeb.Host-publish.zip` ≈ 60 MB (subió por QuestPDF, del Talón Resumen).
Copiar `PsaWeb.Host-publish.zip` y `deploy-app3-ats.ps1` a `C:\Deploy` en el
server (USB / recurso compartido).

En `SERWEBPSA01`, correr `C:\Deploy\deploy-app3-ats.ps1` (hace todo el §2 solo).

---

## 2. Qué hace el script (`deploy-app3-ats.ps1`)

1. `Stop-WebSite CierreDeCaja`.
2. Respalda el `web.config` actual (tiene las env vars del sitio) y toda la
   carpeta (`C:\inetpub\CierreDeCaja.bak-<fecha>`).
3. Vacía `C:\inetpub\CierreDeCaja` y extrae el zip nuevo ahí (PS del server es
   4.0, sin `Expand-Archive` — usa `[IO.Compression.ZipFile]`).
4. Restaura el `web.config` respaldado (el del zip es genérico, sin las env
   vars reales del sitio).
5. Recrea `logs\`.
6. `Start-WebSite CierreDeCaja`.

**No pide nada nuevo**: ATS reutiliza `PeachEbills__ConnectionString` y
`Sage50__ConnectionString`, que el sitio ya tiene desde el deploy del shell.
`dicIdentityTypeATS` (tabla que usa el lector de compras) ya existe en el
`PeachEBills` real — la usa el `.exe` de escritorio, no hay que crearla.

---

## 3. Base de datos y permisos — nada nuevo que crear

Igual que FE: sin migraciones, sin tablas nuevas, sin permisos SQL nuevos para
el app pool. El código de permiso `quats` ("Ver ATS") ya existe en
`allowAction` (verificado 2026-09-12) pero sin filas en `adrAllowRol` — por
eso `AppCatalogo` lo tiene con `GateProvisional` (visible para cualquier
empresa, como Kardex). Si el área quiere gatearlo de verdad más adelante:

```sql
USE [PeachEBills];
-- INSERT INTO adrAllowRol (adrRol, adraid, adrAllowCode, adrActive)
--   SELECT 6, aid, allowCode, 1 FROM allowAction WHERE allowCode = 'quats';
```

y quitar `GateProvisional` de `AppCatalogo.cs` (entrada `"ats"`), redeploy.

---

## 4. Arranque y smoke test

Desde un PC de la red (`http://192.168.0.11:8088/`), con `lparedes` + 2FA:

1. Dashboard: aparece **ATS** (ícono 📑).
2. Elegir una empresa real con datos de ATS (p. ej. CPTDC, ambiente
   **Producción** — el server SÍ alcanza la Sage real, a diferencia de
   PREDATOR).
3. Entrar a **ATS**: año + mes → "Cargar".
   - Card **"Revisar ATS"**: hallazgos (bloqueantes/advertencias).
   - 7 pestañas: General, Compras, NC Compras, Ventas, NC Ventas, Anulados
     Detalle, Resumen ATS — todas con datos.
   - Clic en "Retenciones" de una compra → modal con el detalle AIR.
   - **"Generar ATS (XML)"** → descarga `ATS_<ruc>_<aaaammm>.xml`.
   - **"Descargar Talón Resumen (PDF)"** → descarga `TRSMN-ATS-mm-aaaa-<ruc>.pdf`.
4. **Validación de paridad** (la que de verdad importa): para CPTDC,
   período 07/2026, contrastar los 2 archivos generados contra los reales en
   `C:\SRI-DIMM\Documentacion\` (`ATS CPTDC JULIO  2026.xml` y
   `TRSMN-ATS-07-2026-CPTDC.pdf`) — ya validado número por número en PREDATOR
   contra la Sage real (ver memoria del proyecto), pero repetirlo desde el
   server cierra el círculo end-to-end.
5. Opcional: cargar el XML generado en el DIMM real (`C:\SRI-DIMM\`) para que
   corra su propio validador de negocio + XSD.

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
- Si el área asigna `quats` a roles: §3 + quitar `GateProvisional`.
- F5.5/F5.6 quedan sin gate propio de "declarar" (el botón de exportar XML/PDF
  no exige pasar "Revisar ATS" sin bloqueantes) — decisión ya tomada de no
  bloquear, solo advertir.
- Formularios 103/104 quedan fuera de este corte (decisión 1/3 del plan).

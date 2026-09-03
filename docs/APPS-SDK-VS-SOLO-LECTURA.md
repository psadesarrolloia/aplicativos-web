# Aplicativos: escritura (SDK) vs solo lectura (ODBC)

Clasificación del código fuente de `sage50Apps-master/` para planear la migración.

## Categorías

| Categoría | Aplicativos | Acceso a Sage 50 |
|---|---|---|
| **Escriben en Sage (SDK)** | `Sage50usIntegration` + `Sage50usIntegrationLib` (**un** aplicativo, MDI, 43 formularios — ya retemizado en el proyecto WinForms) | `Sage.Peachtree.API` — `.Save()`, `.Delete()`, `new Bill`, `PeachtreeSession`, `RequestAccess` |
| **Solo lectura (ODBC / Pervasive)** — patrón del piloto | `ReceiptsReportRollerD` (✅ hecho), `Sage50FacturacionElectronica`, `PaymentsMailing`, `AutomaticTwhSender`, `ATSfromPeach`, `Sage50IntegrationConfig`, `Sage50MetaData` | `OdbcConnection` — solo `SELECT` |
| **No tocan Sage** (QuickBooks XML, Datil, SQL Server propio) | `QuickBooksImportXml`, `ImportXmlLib`, `DatilClientLibrary`, `PeachEBillsDB`, `ConsoleQuickbooksXMLImport`, `SageSyncPOtoPI`, `ErrorSage50Proyect` | — |

## La restricción del SDK

El SDK de Sage 50 US (`Sage.Peachtree.API`):

- **.NET Framework 4.x, 32 bits.** No corre en .NET Core / .NET 9.
- Dependencia dura de **`System.Windows.Forms`** (el diálogo "Allow Access" es WinForms).
- Autorización **por empresa, una vez**: `RequestAccess()` → alguien hace clic en
  "Permitir" en Sage 50 → queda en `APIACCSS.DAT` de la carpeta de la empresa.
  Después funciona headless. *`rollerda` ya tiene `APIACCSS.DAT`.*
- No thread-safe, necesita hilo **STA**, una sesión/empresa a la vez.
- **No exige que la app sea WinForms.** Recomendación oficial de Sage: hacerlo
  desde una app .NET Framework aparte (servicio o consola); el host web sigue en .NET.

## Alternativas para escribir en Sage 50

| Vía | ¿Sirve? |
|---|---|
| ODBC / Pervasive directo | ❌ Solo lectura por diseño; escribir corrompe la contabilidad. |
| Import Wizard de Sage 50 (CSV/TXT) | ✅ Sin SDK. La web genera el archivo; el import lo corre alguien/tarea programada. Lotes, no tiempo real. |
| SDK | ✅ Único camino soportado para escritura transaccional en vivo. |
| API REST / nube | ❌ No existe para Sage 50 US (la REST de Sage es de *Sage Accounting*, otro producto). |
| Middleware comercial (Zynk, etc.) | ✅ Envuelve el SDK, expone REST. Costo de licencia + proveedor. |

## Arquitectura propuesta para escritura: "Sage Bridge"

Web (.NET 9, multiusuario) para todo lo de lectura. Para escritura, un **servicio
de Windows .NET Framework 4.8 / 32 bits en el servidor de Sage** (`Peach2015`)
que es el único que carga el SDK, procesa una **cola de trabajos** y devuelve el
resultado. La app web nunca enlaza el SDK: encola y muestra estado.

```
Navegador → App web .NET 9        → (lectura) ODBC directo → Sage 50
                 │
                 └→ cola → Sage Bridge (.NET FW 4.8, STA) → (escritura) SDK → Sage 50
```

Contra: las escrituras son asíncronas (encolar → procesar → resultado).

## Roadmap

- **Ola 1 — solo lectura (~5–7 aplicativos):** cada uno ≈ F3–F4 del piloto
  (3–5 días) + despliegue. Patrón probado.
- **Ola 2 — `Sage50usIntegration` (escritura):** construir el Sage Bridge una vez
  (~1–2 semanas) y portar cada módulo encima. Casi todo el esfuerzo y el riesgo.
- **Alternativa pragmática:** dejar `Sage50usIntegration` como WinForms (ya
  retemizada) por RemoteApp/RDS y web-migrar solo lo de lectura.

## Fuentes

- <https://communityhub.sage.com/us/sage50_us/f/software-development-kit-sdk/126610/net-core-compatibility-with-sage-50-us-sdk-api>
- <http://sagecity.na.sage.com/support_communities/sage50_accounting_us/f/sage-50-u-s-software-development-kit-sdk/69471/sage-50-2014-sdk---how-to-trigger-the-allow-access-dialog>
- <https://gb-kb.sage.com/portal/app/portlets/results/viewsolution.jsp?solutionid=200427112155826>
- <https://help-sage50.na.sage.com/en-ca/AcctEd/2025/Content/Import_Export/HowToImportGeneralJournalEntries.htm>

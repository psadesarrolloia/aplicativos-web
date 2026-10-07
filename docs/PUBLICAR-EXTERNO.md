# Publicación externa de Aplicativos Web PSA (webapp.paredes.com.ec)

Estado: **E1 implementado (2026-10-06), pendiente de deploy**. E2 casi completo (2026-10-07): DNS en Cloudflare y túnel con conector
funcionando, sin ruta pública todavía. E3–E4 pendientes.

## Arquitectura elegida

```
Usuario (oficina, casa, celular)
   └─ https://webapp.paredes.com.ec → Cloudflare (certificado público, HTTPS, DDoS)
        └─ túnel saliente (sin abrir puertos en el router)
             └─ cloudflared en una PC de la red 192.168.0.x (Windows 10/11 o Linux, siempre prendida)
                  └─ http://192.168.0.11:8088 (SERWEBPSA01, IIS)
```

- **El conector NO va en SERWEBPSA01**: las versiones actuales de cloudflared están compiladas con Go ≥ 1.21, que exige Windows 10 /
  Server 2016; en 2012 R2 habría que usar una versión vieja sin parches.
- Blazor Server usa WebSockets: Cloudflare Tunnel los pasa sin configuración extra.

## E1 — Endurecimiento del sitio (código)

Todo vive en `src/PsaWeb.Host/Auth/Publicacion.cs` y se configura en el web.config del servidor (sección `Publico`). Con
`Publico__Habilitado` ausente o en `false` el sitio se comporta como hasta ahora para el HTTP interno.

| Qué | Cómo |
|---|---|
| IP real del cliente | `UseForwardedHeaders` lee `CF-Connecting-IP` y `X-Forwarded-Proto` **solo** si la conexión viene de `Publico:ProxiesConfiables` (el conector). Sin esto el límite del login, la auditoría y el aviso de IP nueva verían a todos con la IP del conector. Desde cualquier otra IP los encabezados se ignoran (nadie puede hacerse pasar por la oficina). |
| Cookies | Con `Publico:Habilitado=true`, todas las cookies (sesión, 2FA, dispositivo de confianza, antiforgery) van **siempre `Secure`**, y HSTS 180 días (sin subdominios). |
| Cabeceras | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, sin `X-Powered-By`. Las páginas llevan **CSP** (`script-src 'self'`, `frame-ancestors 'none'`, …); los PDF y Excel no (el visor de PDF del navegador no la necesita). Se quitó el `<ImportMap />` (no se usan módulos JS) para no tener scripts en línea. |
| `/admin` por red | `Publico:AdminIpsPermitidas` (IPs o CIDR). Fuera de esas redes: `/admin` responde 403 y, dentro del circuito de Blazor, la cuenta no tiene panel (no ve «Configuración»). Vacía = sin restricción. |
| Límite general | `Publico:PeticionesPorMinutoPorIp` (1200 por defecto; 0 = sin límite). Holgado porque en la oficina todos salen por la misma IP pública. |
| Login | El límite del login subió de 10 a **30 POST por IP cada 5 min** por la misma razón; la defensa principal sigue siendo el bloqueo por cuenta (5 fallos → 15 min). |
| `AllowedHosts` | Se fija en el web.config; si la publicación está habilitada con `AllowedHosts=*`, el log lo advierte al arrancar. |

### Variables del web.config (se cargan en E3, con el túnel funcionando)

```xml
<environmentVariable name="Publico__Habilitado" value="true" />
<environmentVariable name="Publico__ProxiesConfiables__0" value="192.168.0.X" />        <!-- IP de la PC con cloudflared -->
<environmentVariable name="Publico__AdminIpsPermitidas__0" value="IP.PUBLICA.OFICINA" /> <!-- si es fija -->
<environmentVariable name="Publico__AdminIpsPermitidas__1" value="192.168.0.0/24" />    <!-- red interna -->
<environmentVariable name="AllowedHosts" value="webapp.paredes.com.ec;192.168.0.11;localhost" />
```

Las IPv4 se validan al arrancar (4 números): `IPAddress.Parse` acepta formas viejas como `200.1.2` = `200.1.0.2`, y una IP mal tipeada
habilitaría otra dirección en silencio; ahora el sitio no arranca y el log dice cuál.

Log esperado: `Publicación externa: HABILITADA (cookies Secure, HSTS); proxies confiables 192.168.0.X; /admin solo desde …`.

**Pruebas** (`tests/PsaWeb.Host.Tests/PublicacionTests.cs`, pipeline real con TestServer): IP real solo desde el conector, encabezados
ignorados desde otra IP, `/admin` desde celular 403 / desde la oficina y la red interna OK, cabeceras y CSP en páginas y no en PDF, cookies
`Secure` aunque otro código diga `SameAsRequest`, deshabilitado = comportamiento anterior, validación de IPs. Verificado en el navegador
que Blazor (WebSocket) funciona con la CSP.

## E2 — Cloudflare (en curso)

### Datos recolectados (2026-10-07, diagnóstico `diagnostico-minilenovo/` corrido en el conector)

| Dato | Valor |
|---|---|
| Conector | Mini PC Lenovo (i5-4570T, 4 GB), formateada y dedicada; nombre **LENOVOTINY**, Windows 11 Pro 25H2 (build 26200) |
| Red del conector | Ethernet, **192.168.0.199 fija puesta a mano en la PC** (puerta 192.168.0.1, DNS 1.1.1.1 / 8.8.8.8); Wi-Fi desconectado |
| Salidas | 192.168.0.11:8088 ✔, Cloudflare 7844 ✔ y 443 ✔ |
| IP pública de la oficina | la misma de `psacontabilidad2` (falta confirmar con el administrador de redes si es fija) |
| DNS de paredes.com.ec | Nameservers `hgns1/hgns2.hostgator.com` (cPanel de HostGator); MX `mail.paredes.com.ec`; SPF `v=spf1 a mx include:websitewelcome.com ~all`; TXT de verificación de Google y Microsoft |

### Cómo apuntar `webapp.paredes.com.ec` al túnel

En el plan **gratuito** de Cloudflare el túnel necesita que la zona `paredes.com.ec` esté **completa** en Cloudflare (nameservers de
Cloudflare). El CNAME desde otro DNS (*partial setup*) es solo de los planes Business/Enterprise y la delegación de un subdominio solo de
Enterprise. Por lo tanto:

1. Exportar la zona completa desde el cPanel de HostGator (Zone Editor) — incluye el DKIM (`default._domainkey`) y lo que el escaneo
   automático de Cloudflare podría no ver.
2. Agregar `paredes.com.ec` a Cloudflare (Free), importar el archivo y comparar registro por registro. Todo lo existente (web, `mail`,
   MX, SPF, DKIM, verificaciones) en **DNS only (nube gris)** para no cambiar nada del sitio ni del correo.
3. Cambiar los nameservers donde está registrado el dominio (registrador del .ec) por los dos que asigne Cloudflare.
   **Hecho 2026-10-07:** zona creada en Cloudflare (cuenta «PSA Desarrollo IA», Free). El escaneo trajo 24 de 25: faltaba
   `psacontabilidad2` A (IP pública de la oficina; acceso a los aplicativos viejos), agregado a mano; los 13 A/CNAME que el escaneo puso con proxy
   se pasaron a «Solo DNS». Nameservers asignados: `shane.ns.cloudflare.com`, `venus.ns.cloudflare.com`. Comparación registro por
   registro `hgns1.hostgator.com` vs `shane.ns.cloudflare.com` (mayúsculas incluidas, DKIM completo): 23/23 consultas iguales. DNSSEC
   no está activo (sin DS en com.ec).
   Nameservers cambiados en dominiosecuador.ec (registrador del .ec) a las 14:17; a las 15:25 `n2.nic.ec` ya delega a Cloudflare y
   1.1.1.1 / 8.8.8.8 resuelven MX, web y `psacontabilidad2` igual que antes.
   Túnel **`psa-webapp`** creado (Zero Trust Free); cloudflared 2026.10.0 instalado como servicio en LENOVOTINY, 1 réplica, «Buen
   estado», **sin rutas** (la ruta pública se agrega recién después del deploy con E1 y las variables `Publico__*`).
4. Con la zona activa: crear el túnel (Zero Trust → Networks → Tunnels), instalar cloudflared como servicio en el conector con el token
   (lo pega el usuario en el conector, nunca en el chat) y la ruta `webapp.paredes.com.ec` → `http://192.168.0.11:8088`.

## E3 — Corte y pruebas desde fuera (pendiente)

Variables de arriba en el web.config, prueba desde un celular con datos móviles (HTTPS, login + 2FA, PDF, módulo de solo lectura, usuario
sin permiso), y cerrar el puerto 8088 del servidor para todo menos el conector (firewall de Windows).

## E4 — Runbook final y ESTADO-MIGRACION-WEB.md (pendiente)

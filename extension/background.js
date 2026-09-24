// PSA - Conciliación SRI — service worker.
//
// Hace el POST al Host de PSA, no el content script: así el request nunca
// queda sujeto a ninguna política de CSP que la página del SRI le imponga a
// sus propios scripts, y separa con claridad "hablar con el SRI" (content
// script, mismo origen, cookies de sesión del portal) de "hablar con PSA"
// (acá, con el token de API).

const ENDPOINT = "/conciliacion-sri/api/comprobantes";

// El ícono ya no tiene un popup transitorio (se cerraba solo al cambiar de
// pestaña para copiar el token o la URL de PSA, antes de terminar de pegar
// las dos cosas) — abre la configuración como una pestaña normal en su lugar.
chrome.action.onClicked.addListener(() => chrome.runtime.openOptionsPage());

async function obtenerConfiguracion() {
  const { tokenApi, hostPsa } = await chrome.storage.local.get(["tokenApi", "hostPsa"]);
  return { tokenApi, hostPsa };
}

function notificar(titulo, mensaje) {
  chrome.notifications.create({
    type: "basic",
    iconUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=",
    title: titulo,
    message: mensaje,
  });
}

async function subirReporte(ruc, contenido) {
  const { tokenApi, hostPsa } = await obtenerConfiguracion();

  if (!tokenApi) {
    return { ok: false, mensaje: "Configurá tu token de PSA en el ícono de la extensión." };
  }
  if (!hostPsa) {
    return { ok: false, mensaje: "Configurá la dirección del sitio de PSA en el ícono de la extensión." };
  }

  let respuesta;
  try {
    respuesta = await fetch(`${hostPsa}${ENDPOINT}`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${tokenApi}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ ruc, contenidoReporte: contenido }),
    });
  } catch {
    return { ok: false, mensaje: "No se pudo conectar con PSA. Reintentá en unos minutos." };
  }

  if (respuesta.status === 401) {
    return { ok: false, mensaje: "Tu token ya no es válido — generá uno nuevo en Mi cuenta." };
  }
  if (respuesta.status === 403) {
    return { ok: false, mensaje: "Esta empresa no está habilitada para tu usuario en PSA." };
  }
  if (respuesta.status === 400) {
    const texto = await respuesta.text();
    return { ok: false, mensaje: texto || "El reporte no tiene el formato esperado." };
  }
  if (!respuesta.ok) {
    return { ok: false, mensaje: "No se pudo conectar con PSA. Reintentá en unos minutos." };
  }

  const resultado = await respuesta.json();
  const partes = [`${resultado.nuevos} nuevos`, `${resultado.yaExistian} ya existían`];
  if (resultado.errores?.length > 0) {
    partes.push(`${resultado.errores.length} con error`);
  }
  return { ok: true, mensaje: `Subido: ${partes.join(", ")}.` };
}

chrome.runtime.onMessage.addListener((mensaje, _sender, sendResponse) => {
  if (mensaje?.tipo !== "reporteDescargado") {
    return false;
  }

  subirReporte(mensaje.ruc, mensaje.contenido).then((resultado) => {
    notificar("PSA - Conciliación con SRI - Docs Recibidos", resultado.mensaje);
    sendResponse(resultado);
  });

  return true; // respuesta asíncrona
});

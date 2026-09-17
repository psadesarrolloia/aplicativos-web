// PSA - Conciliación SRI
//
// Alcance acotado a propósito (decidido con el usuario, plan §11.1): esta
// extensión NO navega el portal ni elige los filtros — eso lo sigue haciendo
// la contadora, como siempre. Solo se activa cuando detecta que la pantalla
// de "Comprobantes electrónicos recibidos" ya tiene una consulta hecha, y
// desde ahí reemplaza el paso de "Descargar reporte" (que abriría un diálogo
// de guardar archivo) por un botón propio que sube el reporte directo a PSA.
//
// Campos y ruta del formulario confirmados con una sesión real (2026-09-17,
// HAR grabado con DevTools) — no son una suposición:
//   - Formulario: frmPrincipal
//   - Campo de identificación (RUC, pre-llenado y deshabilitado — no viaja
//     en el POST nativo): frmPrincipal:txtParametro
//   - Link que dispara la descarga: frmPrincipal:lnkTxtlistado (JSF
//     commandLink — el POST nativo del navegador no lo incluye solo, hay que
//     agregarlo a mano al cuerpo del request)
//   - javax.faces.ViewState: obligatorio en cualquier postback JSF.
// Si el portal cambia de estructura, este script deja de encontrar el
// formulario/botón y avisa con el mensaje de "portal cambió" en vez de fallar
// en silencio — ver mostrarEstado().

const NOMBRE_PANTALLA = "comprobantesRecibidos.jsf";
const ID_FORMULARIO = "frmPrincipal";
const ID_CAMPO_RUC = "frmPrincipal:txtParametro";
const ID_LINK_DESCARGA = "frmPrincipal:lnkTxtlistado";
const ID_BOTON_PSA = "psa-conciliacion-boton-subir";

function estaEnPantallaDeResultados() {
  if (!location.pathname.endsWith(NOMBRE_PANTALLA)) {
    return false;
  }
  const form = document.forms[ID_FORMULARIO];
  if (!form) {
    return false;
  }
  // Hay resultados cuando existe la tabla de la lista (panelListaComprobantes
  // se renderiza siempre que se hizo al menos una consulta).
  return document.getElementById("frmPrincipal:panelListaComprobantes") !== null;
}

function crearBotonSiHaceFalta() {
  if (document.getElementById(ID_BOTON_PSA)) {
    return;
  }

  const contenedor = document.createElement("div");
  contenedor.id = ID_BOTON_PSA;
  contenedor.style.cssText =
    "position:fixed;bottom:20px;right:20px;z-index:999999;font-family:sans-serif;";

  const boton = document.createElement("button");
  boton.textContent = "Subir a PSA";
  boton.style.cssText =
    "background:#B40046;color:#fff;border:none;border-radius:6px;padding:10px 16px;" +
    "font-size:14px;cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,.3);";
  boton.addEventListener("click", subirReporte);

  const estado = document.createElement("div");
  estado.id = "psa-conciliacion-estado";
  estado.style.cssText =
    "margin-top:6px;padding:6px 10px;border-radius:6px;font-size:13px;max-width:320px;display:none;";

  contenedor.appendChild(boton);
  contenedor.appendChild(estado);
  document.body.appendChild(contenedor);
}

function mostrarEstado(mensaje, esError) {
  const estado = document.getElementById("psa-conciliacion-estado");
  if (!estado) return;
  estado.style.display = "block";
  estado.style.background = esError ? "#fdecea" : "#e8f5e9";
  estado.style.color = esError ? "#611a15" : "#1b5e20";
  estado.textContent = mensaje;
}

async function subirReporte() {
  try {
    const form = document.forms[ID_FORMULARIO];
    const campoViewState = document.querySelector('input[name="javax.faces.ViewState"]');
    const campoRuc = document.getElementById(ID_CAMPO_RUC);

    if (!form || !campoViewState || !campoRuc) {
      mostrarEstado("El portal del SRI cambió de formato — avisar a soporte.", true);
      return;
    }

    const ruc = campoRuc.value?.trim();
    if (!ruc) {
      mostrarEstado("El portal del SRI cambió de formato — avisar a soporte.", true);
      return;
    }

    mostrarEstado("Descargando el reporte del SRI…", false);

    // Réplica del POST nativo del link "Descargar reporte" (no un click
    // simulado): FormData ya toma los valores actuales del formulario (año/
    // mes/día/tipo de comprobante elegidos por la contadora), y se agrega a
    // mano el campo del link — PrimeFaces lo agrega vía JS al hacer click,
    // un fetch no dispara ese JS.
    const datos = new FormData(form);
    datos.set(ID_LINK_DESCARGA, ID_LINK_DESCARGA);

    const respuestaSri = await fetch(form.action || location.href, {
      method: "POST",
      credentials: "include",
      body: new URLSearchParams(datos),
    });

    if (!respuestaSri.ok) {
      mostrarEstado("No se pudo conectar con el SRI. Reintentá en unos minutos.", true);
      return;
    }

    // El reporte no es UTF-8 (confirmado: ISO-8859-1) — si se decodifica mal acá,
    // los nombres con Ñ/tildes quedan corrompidos para siempre en PSA.
    const buffer = await respuestaSri.arrayBuffer();
    const contenido = new TextDecoder("iso-8859-1").decode(buffer);

    mostrarEstado("Subiendo a PSA…", false);
    chrome.runtime.sendMessage({ tipo: "reporteDescargado", ruc, contenido }, (respuesta) => {
      if (chrome.runtime.lastError) {
        mostrarEstado("No se pudo conectar con PSA. Reintentá en unos minutos.", true);
        return;
      }
      if (!respuesta?.ok) {
        mostrarEstado(respuesta?.mensaje ?? "No se pudo subir el reporte.", true);
        return;
      }
      mostrarEstado(respuesta.mensaje, false);
    });
  } catch (error) {
    mostrarEstado("El portal del SRI cambió de formato — avisar a soporte.", true);
    console.error("[PSA Conciliación SRI]", error);
  }
}

function revisarPantalla() {
  if (estaEnPantallaDeResultados()) {
    crearBotonSiHaceFalta();
  } else {
    document.getElementById(ID_BOTON_PSA)?.remove();
  }
}

revisarPantalla();
// La pantalla de resultados se arma vía AJAX de PrimeFaces (no hay recarga
// completa de página al apretar "Consultar") — un MutationObserver detecta
// cuándo aparece la tabla sin depender de un evento de navegación.
new MutationObserver(revisarPantalla).observe(document.body, { childList: true, subtree: true });

const campoHost = document.getElementById("hostPsa");
const campoToken = document.getElementById("tokenApi");
const boton = document.getElementById("guardar");
const estado = document.getElementById("estado");

// Misma regla que background.js: vacío o la dirección interna vieja → la dirección pública.
const HOST_PUBLICO = "https://webapp.paredes.com.ec";
const HOST_VIEJO = /^http:\/\/192\.168\.0\.11(:8088)?$/;

async function cargar() {
  const { hostPsa, tokenApi } = await chrome.storage.local.get(["hostPsa", "tokenApi"]);
  campoHost.value = !hostPsa || HOST_VIEJO.test(hostPsa) ? HOST_PUBLICO : hostPsa;
  if (tokenApi) campoToken.value = tokenApi;
}

async function guardar() {
  const hostPsa = campoHost.value.trim().replace(/\/$/, ""); // sin barra final
  const tokenApi = campoToken.value.trim();

  await chrome.storage.local.set({ hostPsa, tokenApi });

  estado.textContent = "Guardado.";
  estado.style.display = "block";
  setTimeout(() => (estado.style.display = "none"), 2000);
}

boton.addEventListener("click", guardar);
cargar();

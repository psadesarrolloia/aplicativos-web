# PSA - Conciliación SRI (extensión de Chrome)

Sube el reporte de "Comprobantes electrónicos recibidos" del portal SRI en
Línea a PSA, para conciliarlo contra Sage 50. No navega el portal ni elige
los filtros — eso lo hace la contadora como siempre; la extensión solo
reemplaza el paso de "Descargar reporte" por un botón que lo sube directo,
sin pasar por la carpeta de Descargas.

## Instalación (una vez por PC)

1. Copiar esta carpeta (`extension/`) al PC donde va a usarse.
2. En Chrome, ir a `chrome://extensions`.
3. Activar **"Modo de desarrollador"** (interruptor arriba a la derecha).
4. Click en **"Cargar descomprimida"** y elegir esta carpeta.
5. Click en el ícono de la extensión (barra de herramientas de Chrome, puede
   estar escondido bajo el ícono de rompecabezas) — abre una **pestaña normal**
   de configuración (no un cuadro chico que se cierra solo), así que podés ir
   y volver de otra pestaña sin perder lo que ya escribiste → completar:
   - **Sitio de PSA**: `http://192.168.0.11:8088` (o la dirección que
     corresponda).
   - **Token de API**: generado en PSA, en `/mi-cuenta/extension` (logueada
     con tu usuario) → "Generar token para la extensión" → copiarlo
     **inmediatamente** (no se vuelve a mostrar).
6. Click en "Guardar".

## Uso

1. Login manual en `srienlinea.sri.gob.ec` con las credenciales del cliente.
2. Navegar a "Comprobantes electrónicos recibidos", elegir año/mes/tipo de
   comprobante (**"Todos"** si se quiere cubrir NC/retenciones/liquidaciones
   además de facturas) y presionar "Consultar", como siempre.
3. Cuando la lista de resultados carga, aparece un botón flotante
   **"Subir a PSA"** abajo a la derecha de la pantalla — apretarlo.
4. Un mensaje confirma cuántos comprobantes se subieron (nuevos / ya
   existían) o explica qué falló.

## Importante

- **Recordatorio de frecuencia**: el listado del portal solo cubre bien los
  últimos 5 días del mes en curso — para conciliar el mes completo hay que
  correr esto seguido durante el mes, no una sola vez al cierre.
- La extensión nunca guarda ni transmite la clave del SRI — solo usa la
  cookie de sesión ya abierta en el navegador para replicar la descarga del
  reporte.
- Si el botón "Subir a PSA" no aparece, o algo falla con el mensaje "el
  portal del SRI cambió de formato", avisar a soporte — el portal puede
  cambiar de estructura sin aviso y el content script necesita ajustarse.
- Actualizar la extensión: reemplazar el contenido de esta carpeta por la
  versión nueva y apretar el botón de recargar (↻) en `chrome://extensions`.

## Chrome desactiva la extensión al reiniciar

Con "Modo de desarrollador" activo esto no debería pasar, pero si Chrome
muestra un aviso de seguridad sobre extensiones sin empaquetar, hay que
volver a `chrome://extensions` y reactivarla — es una molestia conocida de
este modo de instalación para un PC o dos; no bloquea el uso.

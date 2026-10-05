# Nota para el área — Portal de ventas: permisos, roles y correos

**Fecha:** 2026-10-05 · **Empresa piloto:** SANCEV CIA. LTDA. · **Módulo:** Ventas (menú superior de la web)

## 1. Qué es y por qué necesitamos su decisión

El portal de ventas permite a los vendedores **consultar el inventario en tiempo real** (existencias y precios por lista de Sage) y **emitir una prefactura (cotización)**: se guarda,
genera un **PDF para el cliente** y envía **un correo automático a Contabilidad** con todos los datos para que **Contabilidad digite la factura a mano en Sage**.
En esta etapa **el portal no escribe nada en Sage** y no usa el número de factura del SRI; la prefactura lleva un número interno `PF-0001`, vale **15 días** y Contabilidad anota en ella el número de la factura
de Sage cuando la emite.

El código ya está listo y probado. Para usarlo hacen falta **permisos, usuarios y correos** que dependen del área.

## 2. Qué puede hacer cada persona (4 permisos nuevos)

| Permiso | Para quién | Qué permite |
|---|---|---|
| `quSalesStk` | quien solo consulta | Ver inventario y precios |
| `quSalesQte` | quien consulta prefacturas | Ver prefacturas **propias** |
| `mkSalesQte` | **Vendedores** | Emitir prefacturas, descargar el PDF y reenviar el correo **de las suyas** (incluye ver inventario y prefacturas propias) |
| `auSalesQte` | **Contabilidad** | Ver las prefacturas **de todos los vendedores**, anotar el número de la factura de Sage y anular. **No** emite |

Un vendedor **nunca** ve las prefacturas de otro vendedor. Solo Contabilidad cierra o anula.

## 3. Decisiones que necesitamos

1. **Aprobar el reparto de roles.** El script `docs/sql/permisos-ventas.sql` propone copiar los roles que hoy facturan (`qusaleinv` / `mksaleinv`) a las llaves nuevas. En la copia de desarrollo eso daría las llaves a los roles
   *HacerFactElec*, *Hacer Comprobantes Electrónicos*, *Hacer Facturas electrónicas por Lotes* y (solo lectura) *Ver Comprobantes electrónicos*. **Revisar contra producción** y quitar los que no correspondan antes de aplicar.
2. **Crear el rol «Vendedor»** con `quSalesStk` + `quSalesQte` + `mkSalesQte`, y asignarlo en SANCEV a cada vendedor. **Sin este rol los vendedores no podrán entrar**: hoy solo entran quienes tienen permisos de facturación electrónica.
3. **Crear el usuario web de cada vendedor** (Configuración → Usuarios) con acceso a SANCEV. Según las facturas del último año, los vendedores son:
   WILSON JACHO, DIEGO PAUTA, DIEGO PONCE, DANIEL TAPIA, MANOLO CHIRIBOGA, HUGO SANDOVAL, RICARDO SANDOVAL y JORDAN LOPEZ. *(Los vendedores «… TABLEROS» de Sage son un segundo vendedor de cada persona, no usuarios distintos: ¿confirman?)*
4. **Indicar quién es Contabilidad** en SANCEV (una o más personas) para darles `auSalesQte`, y **dos correos** donde llegará cada prefactura: *Contabilidad* y *uno adicional* (opcional).
5. **Correo saliente (TI).** El servidor necesita un SMTP: servidor, puerto, usuario, clave y SSL. El remitente hoy es `anulaciones@paredes.com.ec`; ¿se usa ese o se crea uno como `ventas@…`? Mientras no esté configurado,
   las prefacturas se guardan igual y el correo queda como «sin configurar» (se puede reenviar después).

## 4. Qué haremos nosotros una vez aprobado

1. Ejecutar el script en **vista previa** en producción y revisar la lista de roles que recibirían cada llave; luego aplicarlo (es idempotente y tiene instrucciones de reversa).
2. Desactivar el modo provisional del código (`ReglasVentas.PermisosProvisionales = false`) y publicar.
3. En **Configuración → Ventas** (solo administradores): cargar los 2 correos de SANCEV, la vigencia (15 días) y el IVA (`4-15%`, 15 %).
4. Prueba conjunta: un vendedor emite una prefactura → llega el correo → Contabilidad digita la factura en Sage → anota el número en la prefactura.

## 5. Mientras tanto (modo provisional)

Hasta que se aplique el script, el portal lo habilitan los permisos de facturación electrónica de venta (ver: `qusaleinv`; emitir y cerrar: `mksaleinv`). Quien ya factura puede probarlo; los vendedores sin esos permisos **no** lo ven.

## 6. Preguntas abiertas

- ¿Debe el vendedor poder **anular** sus propias prefacturas, o solo Contabilidad? (hoy: solo Contabilidad).
- ¿Quién puede ver el **costo y la utilidad** de los ítems? (hoy la pantalla no los muestra a nadie).
- ¿Se facturan desde el portal los **tableros «TE»** (ensamblados por proyecto) o solo ítems de inventario? (hoy se muestran, con casilla para ocultarlos).
- Cuando falta **existencia**, ¿se bloquea la prefactura o solo se advierte? (hoy: advierte y deja emitir).

*Detalle técnico: `docs/PLAN-PORTAL-VENTAS.md` (§9 prefacturas, §10 permisos) y `docs/sql/permisos-ventas.sql`.*

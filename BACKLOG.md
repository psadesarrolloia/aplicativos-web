# Backlog — Aplicativos web PSA

Pendientes y mejoras que no bloquean el piloto.

## Piloto (Cierre de Caja)

- [ ] **Modernizar el diseño del Excel exportado.** Hoy `CierreExcelExporter`
      reproduce el formato del `.exe` original (bordes finos, gris claro). Darle
      una vuelta: colores de marca PSA en el encabezado, tipografía, filas
      alternadas, separadores más limpios, quizá el logo. — anotado 2026-08-29.
- [ ] Validar los números contra el `.exe` con la base real (`ptautobackup16`),
      configurando la cadena en User Secrets del proyecto Host.
- [ ] `SELECT` de humo contra producción desde el proceso x86 (pendiente de F0).
- [ ] Revisión de la pantalla por un usuario contable de PSA (criterio de F3).

## Seguridad

- [ ] **Migrar el almacenamiento de secretos de `PeachEBills`.** Las contraseñas
      (ODBC de Sage por empresa en `PeachConnString.pwd`, etc.) están cifradas con
      TripleDES/ECB y clave embebida en el código (`DbSecret`, ex `PasswordSecurity`).
      Se mantiene solo para leer lo existente. Mover a un secret store / DPAPI /
      columna con `ALWAYS ENCRYPTED`.

## Plantilla / ola de 25 aplicativos

- [ ] Servidor de aplicaciones dedicado en vez de co-hospedar en el host RDS
      (se evalúa en la retro, F6).
- [ ] Autorización por rol / grupo de seguridad AD (fuera del alcance del piloto).
- [ ] Acceso remoto propio (`apps.paredes.com.ec`) si se decide no depender del RDS.
- [ ] CI/CD, contenedores, alta disponibilidad.

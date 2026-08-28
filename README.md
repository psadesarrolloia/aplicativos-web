# Aplicativos web PSA

Migración a web de los aplicativos de escritorio de PSA que integran con Sage 50.
Este repositorio arranca con el **piloto**: `Cierre de Caja` (el ex
`ReceiptsReportRollerD`), que además sirve de **plantilla** para los demás
aplicativos.

El plan de trabajo completo (fases, decisiones, estimación) está fuera del
repo, en el documento `plan-piloto-web.html` que entregó el equipo.

## Estructura

```
PsaWeb.sln
├── src/
│   ├── PsaWeb.Host      Blazor Server: arranque, DI, autenticación, layout PSA
│   ├── PsaWeb.Shared    Sistema de diseño PSA: tokens CSS + componentes Razor
│   └── PsaWeb.Sage50    Acceso a datos de Sage 50 (ODBC / Pervasive)
└── modules/
    └── PsaWeb.Modules.CierreDeCaja   El piloto. Cada aplicativo futuro = un módulo aquí.
```

## Requisitos

- .NET SDK 9 (`9.0.316` o posterior de la misma banda) — ver `global.json`.
- El runtime **x86** de .NET 9 (ASP.NET Core) instalado: el driver Pervasive
  ODBC de Sage 50 es de 32 bits, así que `PsaWeb.Host` compila y corre como
  **x86** (`<PlatformTarget>x86</PlatformTarget>`).

## Correr en desarrollo (PREDATOR)

```bash
dotnet run --project src/PsaWeb.Host
```

Luego abrir `http://localhost:5170`.

- **Autenticación**: PREDATOR no está unido al dominio, así que en `Development`
  un handler firma automáticamente como el usuario de Windows local
  (`PsaWeb.Host/Auth/DevWindowsAuthHandler.cs`). En producción se usa Windows
  Integrated Auth (Negotiate) contra Active Directory — se valida al promover a
  `psacontabilidad2` (Fase 5).
- **Datos de Sage 50**: `appsettings.Development.json` apunta al DSN de ejemplo
  `demodata`. La consulta real se cablea en la Fase 2; hoy la pantalla de
  `Cierre de Caja` muestra solo la estructura.

## Estado

**Fase 1 — andamiaje + sistema de diseño.** La solución compila y corre;
layout PSA, autenticación cableada, página de componentes y estructura de
`Cierre de Caja` listas. Sin acceso a datos todavía.

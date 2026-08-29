# Validar el Cierre de Caja contra Sage 50 real

Por defecto la app usa **datos de muestra** (la cadena de conexión viene vacía).
Para probar contra la base real de Sage 50 y comparar los números con el
ejecutable original `ReceiptsReportRollerD`:

## 1. Abrir una terminal en el proyecto Host

`C:\PROYECTOS IA\Aplicativos web\src\PsaWeb.Host`

En Visual Studio: clic derecho sobre el proyecto **PsaWeb.Host** →
*Abrir en el terminal*.

## 2. Guardar la cadena de conexión en User Secrets

**No va en git** — queda en `%APPDATA%\Microsoft\UserSecrets\` de tu usuario.

PowerShell (comillas simples para que `{ }` y `;` sean literales):

```powershell
dotnet user-secrets set "Sage50:ConnectionString" 'Driver={Pervasive ODBC Client Interface};servername=192.168.0.11.1583;DBQ=ptautobackup16;uid=Peachtree;pwd=JCV1234;'
```

Verificar:

```powershell
dotnet user-secrets list
```

## 3. Ejecutar

F5 en Visual Studio, o `dotnet run`. En el log de arranque debe decir:

```
Cierre de Caja: repositorio ODBC / Sage 50.
```

(si dice `DE MUESTRA`, la cadena no se cargó — revisar el paso 2).

## 4. Comparar

1. En `/cierre-de-caja`, elegí un rango de fechas del que ya conozcas el
   resultado del `.exe` (por ejemplo el cierre de un día concreto).
2. **Consultar** y comparar contra el `.exe` para el mismo rango:
   - Total ventas
   - Total cobros
   - Diferencia
   - El detalle por recibo
3. **Exportar a Excel** y comparar el archivo celda por celda con el que
   genera el `.exe`.

Los números deben coincidir. Si no, anotarlo — puede ser un tema de formato de
fecha o de zona horaria en la consulta.

## 5. Volver a datos de muestra

```powershell
dotnet user-secrets remove "Sage50:ConnectionString"
```

o, sin borrar la cadena:

```powershell
dotnet user-secrets set "Sage50:UseSampleData" "true"
```

## Si falla la consulta

La pantalla muestra `No se pudo consultar Sage 50: <detalle>`. Causas típicas:

| Mensaje | Causa | Qué hacer |
|---|---|---|
| `IM014 ... architecture mismatch` | El proceso no es de 32 bits | Confirmar `<PlatformTarget>x86</PlatformTarget>` en `PsaWeb.Host.csproj` y que Visual Studio no lo esté sobrescribiendo |
| `Data source name not found` / `no se encontró el nombre del origen de datos` | Nombre del driver mal escrito | Debe ser exactamente `Pervasive ODBC Client Interface` |
| Timeout / `servidor no encontrado` | No hay ruta a `192.168.0.11` ahora mismo | Verificar red / que Sage 50 esté arriba |
| Error de login | Usuario o contraseña | Revisar `uid` / `pwd` de la cadena |

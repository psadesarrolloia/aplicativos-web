# AW-4: arma accesos-web-datos.sql (los INSERT que incluye docs/sql/accesos-web-siembra.sql con :r) desde la matriz de usuarios.
# Lee la hoja que tenga los encabezados NOMBRE / USUARIO WEB / CORREO / USUARIO SAGE / USUARIO RDP WINSERVER / EMPRESA / PERFIL.
# NUNCA copia columnas de claves (CLAVE SAGE, CLAVE RDP...): solo las de arriba. El resultado tiene datos personales: queda en la
# carpeta Usuarios\ (fuera del repo) y se copia a C:\Deploy del servidor.
#
# Uso (en PREDATOR):
#   powershell -ExecutionPolicy Bypass -File docs\sql\generar-datos-siembra.ps1
#   (por defecto lee "Usuarios\Matriz usuarios WebApps PSA.xlsx" y escribe "Usuarios\accesos-web-datos.sql")
param(
    [string]$Matriz = (Join-Path $PSScriptRoot '..\..\Usuarios\Matriz usuarios WebApps PSA.xlsx'),
    [string]$Salida = (Join-Path $PSScriptRoot '..\..\Usuarios\accesos-web-datos.sql')
)
$ErrorActionPreference = 'Stop'

# Empresas de la matriz (alias -> RUC). TODAS = estas 12.
$Empresas = [ordered]@{
    'SANCEV' = '1791313747001'; 'CPTDC' = '1792051800001'; 'DGRV' = '1791033973001'; 'EFEMEDIO' = '1792187796001'
    'DEMORADIO' = '1792183340001'; 'ILDIS' = '1791709438001'; 'ROLLERDANCE' = '1793198281001'; 'PSA' = '1791300165001'
    'PYP' = '1791741951001'; 'ANDE' = '1790352897001'; 'GONZALO ROSERO' = '1000130102001'; 'MONICA MARTINEZ' = '1716565427001'
}

# Perfil de la matriz -> (Perfil web, plantilla de llaves).
function Convertir-Perfil([string]$p) {
    $p = $p.Trim().ToUpperInvariant()
    if ($p -eq 'SUPERADMIN' -or $p -eq 'SUPER ADMIN') { return @('SuperAdmin', 'TODO') }
    if ($p -eq 'ADMIN') { return @('Admin', 'TODO') }
    # Supervisor (nivel entre Digitador y Admin): lo del .exe + autorizar anulaciones en todas sus empresas. «DIGITADOR PSA» = Supervisor.
    if ($p -eq 'SUPERVISOR' -or $p -eq 'DIGITADOR PSA') { return @('Supervisor', 'SUPERVISOR') }
    if ($p.StartsWith('DIGITADOR')) { return @('Digitador', 'EXE') }   # «los mismos permisos de hoy en el .exe» (+ Conciliación)
    if ($p.StartsWith('VENDEDOR')) { return @('Vendedor', 'VENDEDOR') }
    if ($p -eq 'CONSULTA') { return @('Consulta', 'NINGUNA') }          # solo las empresas; los módulos se marcan a mano
    throw "Perfil desconocido en la matriz: '$p'"
}

function Rucs([string]$empresa) {
    $e = $empresa.Trim().ToUpperInvariant()
    if ($e -eq 'TODAS') { return @($Empresas.Values) }
    return @($e -split '\s*[,;/]\s*' | ForEach-Object {
        $alias = $_ -replace '\s*\(PRUEBAS\)\s*$', ''
        if (-not $Empresas.Contains($alias)) { throw "Empresa desconocida en la matriz: '$_' (alias válidos: $($Empresas.Keys -join ', '), TODAS)" }
        $Empresas[$alias]
    })
}

function Sql([string]$s) { if ([string]::IsNullOrWhiteSpace($s)) { 'NULL' } else { "N'" + $s.Trim().Replace("'", "''") + "'" } }

# ---- leer el .xlsx (sin Excel) ----
$tmp = Join-Path $env:TEMP ('matriz-' + [guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $Matriz -Destination "$tmp.zip"
Expand-Archive -LiteralPath "$tmp.zip" -DestinationPath $tmp
try {
    $ns = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
    [xml]$wb = Get-Content -LiteralPath "$tmp\xl\workbook.xml" -Encoding UTF8
    [xml]$rels = Get-Content -LiteralPath "$tmp\xl\_rels\workbook.xml.rels" -Encoding UTF8
    $compartidos = @()
    if (Test-Path "$tmp\xl\sharedStrings.xml") {
        [xml]$ss = Get-Content -LiteralPath "$tmp\xl\sharedStrings.xml" -Encoding UTF8
        $compartidos = @($ss.sst.si | ForEach-Object {
            if ($_.t) { if ($_.t -is [string]) { $_.t } else { $_.t.'#text' } }
            else { ($_.r | ForEach-Object { if ($_.t -is [string]) { $_.t } else { $_.t.'#text' } }) -join '' }
        })
    }
    $filas = $null
    foreach ($sh in $wb.workbook.sheets.sheet) {
        $target = ($rels.Relationships.Relationship | Where-Object { $_.Id -eq $sh.GetAttribute('id', $ns) }).Target
        [xml]$w = Get-Content -LiteralPath (Join-Path "$tmp\xl" $target) -Encoding UTF8
        $tabla = foreach ($row in $w.worksheet.sheetData.row) {
            $d = @{}
            foreach ($c in $row.c) {
                $col = ($c.r -replace '\d', '')
                $v = if ($c.t -eq 's') { $compartidos[[int]$c.v] } elseif ($c.t -eq 'inlineStr') { $c.is.t } else { $c.v }
                if ($v) { $d[$col] = [string]$v }
            }
            , $d
        }
        $enc = $tabla | Select-Object -First 1
        if ($enc -and ($enc.Values -contains 'USUARIO WEB') -and ($enc.Values -contains 'CORREO')) { $filas = $tabla; break }
    }
    if (-not $filas) { throw "No encontré en $Matriz una hoja con los encabezados USUARIO WEB y CORREO." }
} finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force; Remove-Item -LiteralPath "$tmp.zip" -Force
}

$enc = $filas[0]
$col = @{}
foreach ($k in $enc.Keys) { $col[$enc[$k].Trim().ToUpperInvariant()] = $k }
foreach ($req in 'NOMBRE', 'USUARIO WEB', 'CORREO', 'EMPRESA', 'PERFIL') { if (-not $col.ContainsKey($req)) { throw "Falta la columna $req en la matriz." } }
function Celda($fila, $nombre) { if ($col.ContainsKey($nombre)) { $fila[$col[$nombre]] } else { $null } }

# ---- armar personas y empresas (una persona puede venir en varias filas: una por empresa) ----
# (Ojo: en PowerShell $empresas y $Empresas son la MISMA variable; por eso «asignaciones».)
$personas = [ordered]@{}
$asignaciones = [ordered]@{}
foreach ($f in ($filas | Select-Object -Skip 1)) {
    $usuario = Celda $f 'USUARIO WEB'
    if ([string]::IsNullOrWhiteSpace($usuario)) { continue }
    $usuario = $usuario.Trim().ToLowerInvariant()
    $perfil, $plantilla = Convertir-Perfil (Celda $f 'PERFIL')
    $origen = Celda $f 'USUARIO RDP WINSERVER'
    $p = [ordered]@{ Nombre = (Celda $f 'NOMBRE').Trim(); Correo = (Celda $f 'CORREO').Trim(); Perfil = $perfil; Plantilla = $plantilla
                     Origen = $(if ($origen) { $origen.Trim().ToLowerInvariant() } else { $null }) }
    if ($personas.Contains($usuario)) {
        $previo = $personas[$usuario]
        if ($previo.Correo -ne $p.Correo -or $previo.Perfil -ne $p.Perfil) { throw "La cuenta $usuario aparece con distinto correo o perfil en dos filas." }
    } else { $personas[$usuario] = $p }
    foreach ($ruc in (Rucs (Celda $f 'EMPRESA'))) { $asignaciones["$usuario|$ruc"] = Celda $f 'USUARIO SAGE' }
}

# ---- escribir el SQL ----
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("-- Generado por docs\sql\generar-datos-siembra.ps1 el $(Get-Date -Format 'yyyy-MM-dd HH:mm') desde $(Split-Path $Matriz -Leaf).")
[void]$sb.AppendLine('-- DATOS PERSONALES: no subir al repo. Sin claves. Lo incluye accesos-web-siembra.sql con :r.')
[void]$sb.AppendLine('INSERT INTO #Personas (UsuarioWeb, Nombre, Correo, Perfil, Plantilla, PeachOrigen) VALUES')
$lineas = foreach ($u in $personas.Keys) { $p = $personas[$u]; " ($(Sql $u), $(Sql $p.Nombre), $(Sql $p.Correo), $(Sql $p.Perfil), $(Sql $p.Plantilla), $(Sql $p.Origen))" }
[void]$sb.AppendLine(($lineas -join ",`r`n") + ';')
[void]$sb.AppendLine('INSERT INTO #Empresas (UsuarioWeb, Ruc, UsuarioSage) VALUES')
$lineas = foreach ($k in $asignaciones.Keys) { $u, $r = $k -split '\|'; " ($(Sql $u), $(Sql $r), $(Sql $asignaciones[$k]))" }
[void]$sb.AppendLine(($lineas -join ",`r`n") + ';')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Salida), $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Listo: $($personas.Count) cuentas y $($asignaciones.Count) asignaciones de empresa en $([IO.Path]::GetFullPath($Salida))"
foreach ($u in $personas.Keys) {
    $p = $personas[$u]
    $n = @($asignaciones.Keys | Where-Object { $_.StartsWith("$u|") }).Count
    "  {0,-12} {1,-11} {2,-9} {3,2} empresa(s)  {4}" -f $u, $p.Perfil, $p.Plantilla, $n, $p.Correo
}

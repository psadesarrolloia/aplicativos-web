using System.Text.RegularExpressions;
using PsaWeb.Seguridad;

namespace PsaWeb.Seguridad.Tests;

/// <summary>
/// docs/sql/accesos-web-siembra.sql repite en T-SQL el catálogo de llaves y las plantillas Digitador/a y Vendedor de <see cref="LlavesWeb"/>.
/// Si alguien agrega una llave o cambia una plantilla en C# y no en el SQL, la siembra de AW-4 quedaría desalineada: esta prueba lo frena.
/// </summary>
public class SiembraSqlTests
{
    private static string RutaSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PsaWeb.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "docs", "sql", "accesos-web-siembra.sql");
    }

    private static List<(string Llave, bool Digitador, bool Vendedor)> LlavesDelSql()
    {
        var sql = File.ReadAllText(RutaSql());
        var bloque = sql[sql.IndexOf("INSERT INTO #Llaves", StringComparison.Ordinal)..];
        bloque = bloque[..bloque.IndexOf(';')];
        return Regex.Matches(bloque, @"\(N'(?<k>[^']+)',\s*(?<d>[01]),\s*(?<v>[01])\)")
            .Select(m => (m.Groups["k"].Value, m.Groups["d"].Value == "1", m.Groups["v"].Value == "1"))
            .ToList();
    }

    [Fact]
    public void El_SQL_tiene_exactamente_las_llaves_del_panel()
    {
        var sql = LlavesDelSql().Select(x => x.Llave).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var cs = LlavesWeb.Todas.Select(l => l.Codigo).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(cs, sql);
    }

    [Fact]
    public void Las_plantillas_Digitador_y_Vendedor_del_SQL_son_las_de_LlavesWeb()
    {
        var sql = LlavesDelSql();
        Assert.Equal(LlavesWeb.Plantillas["Digitador/a"].OrderBy(x => x, StringComparer.Ordinal),
            sql.Where(x => x.Digitador).Select(x => x.Llave).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(LlavesWeb.Plantillas["Vendedor"].OrderBy(x => x, StringComparer.Ordinal),
            sql.Where(x => x.Vendedor).Select(x => x.Llave).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Supervisor_es_Digitador_mas_autorizar_anulaciones_igual_que_en_el_SQL()
    {
        var anulaciones = LlavesDelSql().Select(x => x.Llave).Where(l => l.StartsWith("auCance", StringComparison.Ordinal));
        var esperado = LlavesWeb.Plantillas["Digitador/a"].Concat(anulaciones).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(esperado, LlavesWeb.Plantillas["Supervisor"].OrderBy(x => x, StringComparer.Ordinal));
        var sql = File.ReadAllText(RutaSql());
        Assert.Contains("p.Plantilla = N'SUPERVISOR'", sql);
        Assert.Contains("k.Llave LIKE N'auCance%'", sql);
    }

    [Fact]
    public void El_SQL_no_trae_datos_personales_y_exige_QUOTED_IDENTIFIER()
    {
        var sql = File.ReadAllText(RutaSql());
        Assert.Contains(":r accesos-web-datos.sql", sql);
        Assert.Contains("SET QUOTED_IDENTIFIER ON;", sql);
        Assert.DoesNotMatch(@"@[a-z0-9.-]+\.(com|ec|net|coop|org)", sql); // ningún correo real en el archivo versionado
    }
}

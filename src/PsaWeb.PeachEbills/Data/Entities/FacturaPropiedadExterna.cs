using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

/// <summary>
/// Propiedad extra de una factura que no cabe en la cabecera (p. ej. el número
/// de fax del cliente). Tabla <c>FacturaPropiedadExterna</c>, PK compuesta
/// <c>(Factura, Name)</c>.
/// </summary>
[PrimaryKey(nameof(Factura), nameof(Name))]
public partial class FacturaPropiedadExterna
{
    /// <summary>FK a <see cref="Facturas.FacturaId"/>.</summary>
    public int Factura { get; set; }

    [StringLength(300)]
    public string Name { get; set; } = null!;

    [StringLength(300)]
    public string ValueData { get; set; } = null!;
}

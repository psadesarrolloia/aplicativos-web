using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

// DbSet de las tablas de PeachEBills del registro de compras (Ola 2). La clave y el nombre de tabla van por
// atributos en la entidad.
public partial class PeachEbillsContext
{
    public virtual DbSet<AboutApplyTwh> AboutApplyTwh { get; set; } = null!;
    public virtual DbSet<DicImpuestosTipo> DicImpuestosTipo { get; set; } = null!;
    public virtual DbSet<IdentityType> IdentityType { get; set; } = null!;
    public virtual DbSet<VendorConfiguration> VendorConfiguration { get; set; } = null!;
}

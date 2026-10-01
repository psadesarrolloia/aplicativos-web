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

    // Liquidación de importaciones (PLAN-OLA2-LIQUIDACION-IMPORTACIONES).
    public virtual DbSet<ImportCost> ImportCost { get; set; } = null!;
    public virtual DbSet<ImportCostApportion> ImportCostApportion { get; set; } = null!;
    public virtual DbSet<ImportCostEx> ImportCostEx { get; set; } = null!;
}

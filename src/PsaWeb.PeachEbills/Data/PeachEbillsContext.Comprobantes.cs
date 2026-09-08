using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

// DbSets de las tablas de comprobantes de venta (facturas, notas de crédito,
// liquidaciones) de PeachEBills. Las claves y nombres de tabla van por
// atributos en las entidades; no hace falta configuración fluida acá.
public partial class PeachEbillsContext
{
    public virtual DbSet<Facturas> Facturas { get; set; } = null!;

    public virtual DbSet<Details> Details { get; set; } = null!;

    public virtual DbSet<NcDetail> NcDetails { get; set; } = null!;

    public virtual DbSet<Payments> Payments { get; set; } = null!;

    public virtual DbSet<PaymentTypes> PaymentTypes { get; set; } = null!;

    public virtual DbSet<FacturaPropiedadExterna> FacturaPropiedadExterna { get; set; } = null!;

    public virtual DbSet<InvoiceConfigAditionalInfo> InvoiceConfigAditionalInfo { get; set; } = null!;
}

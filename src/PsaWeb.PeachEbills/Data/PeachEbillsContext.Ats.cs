using Microsoft.EntityFrameworkCore;

namespace PsaWeb.PeachEbills.Data;

// DbSet de las tablas de PeachEBills específicas del ATS (app #3). La clave y
// el nombre de tabla van por atributos en la entidad; no hace falta
// configuración fluida acá.
public partial class PeachEbillsContext
{
    public virtual DbSet<DicIdentityTypeAts> DicIdentityTypeAts { get; set; } = null!;
}

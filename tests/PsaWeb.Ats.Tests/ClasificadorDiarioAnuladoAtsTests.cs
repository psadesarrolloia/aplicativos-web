using PsaWeb.Ats.Anulados;

namespace PsaWeb.Ats.Tests;

public class ClasificadorDiarioAnuladoAtsTests
{
    [Theory]
    [InlineData(3, DiarioAnuladoAts.Sales)]
    [InlineData(4, DiarioAnuladoAts.PurchaseReceiveInventory)]
    [InlineData(10, DiarioAnuladoAts.PurchaseOrder)]
    [InlineData(999, DiarioAnuladoAts.Sales)] // no reconocido -> cae en Sales, igual que el `.exe`.
    public void ClasificarDiario(int valor, DiarioAnuladoAts esperado)
    {
        Assert.Equal(esperado, ClasificadorDiarioAnuladoAts.ClasificarDiario(valor));
    }

    [Theory]
    [InlineData(8, SubtipoAnuladoAts.SaleInvoice)]
    [InlineData(9, SubtipoAnuladoAts.SaleCreditMemo)]
    [InlineData(11, SubtipoAnuladoAts.PurchaseReceiveInventory)]
    [InlineData(12, SubtipoAnuladoAts.PurchaseCreditMemo)]
    [InlineData(18, SubtipoAnuladoAts.PurchaseOrder)]
    [InlineData(999, SubtipoAnuladoAts.SaleInvoice)] // no reconocido -> cae en SaleInvoice.
    public void ClasificarSubtipo(int valor, SubtipoAnuladoAts esperado)
    {
        Assert.Equal(esperado, ClasificadorDiarioAnuladoAts.ClasificarSubtipo(valor));
    }
}

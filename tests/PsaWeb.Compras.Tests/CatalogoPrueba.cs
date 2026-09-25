using PsaWeb.Compras.Catalogo;

namespace PsaWeb.Compras.Tests;

/// <summary>Catálogo con la misma configuración de ítems C, IMPUESTO y retenciones que usan las empresas de PSA.</summary>
internal static class CatalogoPrueba
{
    private static ItemSage C(string id, string rf, string iva, string cf4 = "", string cf5 = "", string categoria = "") =>
        new(id, id, 0, categoria, "COMPRA", rf, iva, cf4, cf5, "60000", false);

    private static ItemSage R(string id, string categoria, string codigo, string porcentaje, string cuenta) =>
        new(id, "DESC " + id, 0, categoria, codigo, porcentaje, "", "", "", cuenta, false);

    internal static readonly ItemSage[] Items =
    [
        // Orden de ORDER BY ItemID (C1, C10, C11… C15, C2… C9): la selección toma el primero que calce.
        C("C1", "RF: SI", "IVA: SI"),
        C("C10", "RF: NO", "IVA: NO", "ImpExe", "332"),
        C("C11", "RF: NO", "IVA: NO", "NoGraIVA", "332"),
        C("C12", "RF: NO", "IVA: NO", "NoGraIVA", "332I"),
        C("C13", "RF: NO", "IVA: NO", "NoGraIVA", "332G"),
        C("C14", "RF: SI", "IVA: NO", "NoGraIVA"),
        C("C15", "RF: SI", "IVA: NO", "Imponible 0%"),
        C("C2", "RF: SI", "IVA: NO", "Imponible 0%"),
        C("C3", "RF: NO", "IVA: SI", "", "332G", "332G"),
        C("C4", "RF: NO", "IVA: NO", "Imponible 0%", "332G", "332G"),
        C("C5", "RF: NO", "IVA: NO", "NoGraIVA", "332"),
        C("C6", "RF: NO", "IVA: SI", "", "332I"),
        C("C7", "RF: NO", "IVA: NO", "Imponible 0%", "332I"),
        C("C8", "RF: NO", "IVA: SI", "", "332"),
        C("C9", "RF: NO", "IVA: NO", "Imponible 0%", "332"),
        new("15% IVA COMPRAS", "15% IVA COMPRAS", 0, "IMPUESTO", "IVA", "15%", "15%", "", "", "14233", false),
        new("AUT-SRI", "No AUTORIZACION SRI", 0, "IMPUESTO", "", "", "", "", "", "60000", false),
        R("0% 332 - Sin Ret.", "R-IRF", "332", "0%", "60000"),
        R("0% 332G - Sin Ret.TC", "R-IRF", "332G", "0%", "60000"),
        R("0% 332I - Sin Ret.DB", "R-IRF", "332I", "0%", "60000"),
        R("2% SEGUROS-322", "R-IRF", "322", "-2%", "24055"),
        R("2.75% OTROS-344", "R-IRF", "3440", "-2.75%", "24050"),
        R("3% OTROS-344", "R-IRF", "3440", "-3.00%", "24050"),
        R("30% RET IVA-725", "R-IVA", "1", "-30%", "24005"),
        R("70% RET IVA-729", "R-IVA", "2", "-70%", "24006"),
        new("MERC-1", "MERCADERIA", 1, "INVT CON", "", "", "", "", "", "14100", false),
    ];

    internal static readonly CatalogoPeachEbills PeachEbills = new(
        [
            new(1, "Tarjeta de crédito", "tarjeta_credito", false),
            new(2, "Débito bancario - Autorizado", null, false),
            new(3, "Reembolso de gastos", null, false),
            new(4, "Débito bancario - no autorizado", null, true),
            new(5, "Otros", "otros", true),
            new(6, "No sujeto a retención", null, false),
            new(7, "Retención asumida", null, true),
            new(9, "Resolución 8", null, false),
        ],
        [new("20", "otros"), new("19", "tarjeta_credito"), new("01", "efectivo"), new("16", "tarjeta_debito")],
        [new("0", "0%", 0m), new("2", "12%", 0.12m), new("4", "15%", 0.15m), new("5", "5%", 0.05m), new("6", "NO OBJETO DE IMPUESTO", 0m), new("7", "EXCENTO DE IVA", 0m)],
        new Dictionary<string, string> { ["2"] = "IVA", ["3"] = "ICE", ["5"] = "IRBPNR" });

    internal static CatalogoCompras Crear() => LectorCatalogoCompras.Armar(Items, "10000", PeachEbills);

    internal static ProveedorSage Proveedor(string cuentaGasto = "60505", string email = "compras@proveedor.test", string tipo = "04") =>
        new("PROVEEDOR PRUEBA", "123", tipo, "PROVEEDOR PRUEBA", "", "", "", "", "DIRECCION 1", "", "1799999999001",
            email, "", "OC-", cuentaGasto, false);
}

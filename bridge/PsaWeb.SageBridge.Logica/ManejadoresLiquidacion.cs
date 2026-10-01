using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sage.Peachtree.API;
using Sage.Peachtree.API.Collections.Generic;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// <see cref="TiposTrabajo.GuardarOcLiquidacion"/>: port de <c>sageImportMgm.loadingPOforImportsCost</c> (§5.1 del plan de
/// Liquidación de Importaciones). OC con cuenta por pagar = cuenta de la importación, dirección del proveedor, una línea por ítem
/// (monto = valor + prorrateo, precio = monto / cantidad, cuenta de inventario del ítem) y la línea <c>LIQUIDACION</c> por −total a la
/// cuenta de la importación (total de la OC = 0). Diferencias con el `.exe`: actualización en el lugar (como <c>GuardarOc</c>),
/// referencia repetida rechazada y <c>Validate()</c> fallido devuelto con los problemas.
/// </summary>
public sealed class ManejadorGuardarOcLiquidacion : IManejadorTrabajo
{
    public string Tipo => TiposTrabajo.GuardarOcLiquidacion;

    public string Ejecutar(ContextoLote contexto, TrabajoTomado trabajo)
    {
        if (string.IsNullOrWhiteSpace(trabajo.PayloadJson)) throw new RechazoTrabajoException("El trabajo no trae datos de la liquidación.");
        var p = Json.Leer<PayloadGuardarOcLiquidacion>(trabajo.PayloadJson!);
        if (string.IsNullOrWhiteSpace(p.Referencia) || string.IsNullOrWhiteSpace(p.ProveedorId) ||
            string.IsNullOrWhiteSpace(p.CuentaImportacion) || p.Lineas.Count == 0)
        {
            throw new RechazoTrabajoException("Faltan la referencia, el proveedor, la cuenta de la importación o los ítems.");
        }

        var f = contexto.Empresa.Factories;
        using var odbc = OdbcSage.Abrir(contexto.Configuracion, trabajo.Ruc);
        var proveedor = SageBusqueda.Uno(f.VendorFactory.List(), SageBusqueda.Igual("Vendor.ID", p.ProveedorId))
                        ?? throw new RechazoTrabajoException($"El proveedor {p.ProveedorId} no existe en Sage.");
        var cuentaImportacion = SageBusqueda.Cuenta(contexto.Empresa, p.CuentaImportacion);

        PurchaseOrder oc;
        string accion;
        if (p.PostOrder is { } po)
        {
            var existente = odbc.Oc(po) ?? throw new RechazoTrabajoException($"La OC {po} ya no existe en Sage.");
            if (odbc.FueRecibida(po)) throw new RechazoTrabajoException($"La OC {existente.Referencia} ya se convirtió en compra: no se puede actualizar.");
            if (existente.Referencia != p.Referencia.Trim() && odbc.ExisteReferenciaOc(p.Referencia.Trim()))
            {
                throw new RechazoTrabajoException($"La referencia {p.Referencia} ya existe en Sage.");
            }
            oc = SageBusqueda.Uno(f.PurchaseOrderFactory.List(), SageBusqueda.OcDe(ProveedorDe(contexto.Empresa, existente.VendorId), existente.Referencia))
                 ?? throw new RechazoTrabajoException($"No se encontró una única OC {existente.Referencia}.");
            foreach (var linea in oc.PurchaseOrderLines.ToList()) oc.RemoveLine(linea);
            accion = "Actualizada";
        }
        else
        {
            if (odbc.ExisteReferenciaOc(p.Referencia.Trim())) throw new RechazoTrabajoException($"La referencia {p.Referencia} ya existe en Sage.");
            oc = f.PurchaseOrderFactory.Create();
            accion = "Creada";
        }

        // Cabecera, en el orden del `.exe`.
        oc.ReferenceNumber = p.Referencia.Trim();
        oc.Date = DateTime.ParseExact(p.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        oc.VendorReference = proveedor.Key;
        oc.MainAddress.Name = proveedor.Name;
        oc.MainAddress.Address.Address1 = proveedor.MailToContact.Address.Address1;
        oc.MainAddress.Address.Address2 = proveedor.MailToContact.Address.Address2;
        oc.MainAddress.Address.City = proveedor.MailToContact.Address.City;
        oc.MainAddress.Address.Country = proveedor.MailToContact.Address.Country;
        oc.AccountReference = string.IsNullOrWhiteSpace(p.CuentaPorPagar) ? cuentaImportacion : SageBusqueda.Cuenta(contexto.Empresa, p.CuentaPorPagar.Trim());

        var total = 0m;
        foreach (var l in p.Lineas)
        {
            var (item, cuentaInventario) = SageBusqueda.ItemDeStock(contexto.Empresa, l.Item);
            var linea = oc.AddLine();
            linea.InventoryItemReference = item;
            linea.Description = l.Descripcion;
            linea.Quantity = l.Cantidad;
            linea.Amount = l.Monto;
            linea.UnitPrice = linea.Quantity == 0 ? 0 : linea.Amount / linea.Quantity;
            total += linea.Amount;
            linea.AccountReference = cuentaInventario;
        }
        var cierre = oc.AddLine();
        cierre.Description = "LIQUIDACION";
        cierre.AccountReference = cuentaImportacion;
        cierre.Amount = -total;

        var problemas = new ValidationProblemList();
        if (!oc.Validate(problemas))
        {
            throw new RechazoTrabajoException("Sage rechazó la OC: " + SageBusqueda.Detalle(problemas));
        }
        oc.Save();

        var guardada = odbc.OcPorReferencia(oc.ReferenceNumber);
        return Json.Escribir(new ResultadoGuardarOcLiquidacion
        {
            Accion = accion,
            Clave = oc.Key.Guid.ToString(),
            Mensaje = $"OC {oc.ReferenceNumber} {accion.ToLowerInvariant()} (total 0, {p.Lineas.Count} ítems, liquidación {total:0.00}).",
            PostOrder = guardada ?? 0,
            Referencia = oc.ReferenceNumber,
        });
    }

    private static EntityReference<Vendor> ProveedorDe(Company empresa, string vendorId) =>
        (SageBusqueda.Uno(empresa.Factories.VendorFactory.List(), SageBusqueda.Igual("Vendor.ID", vendorId))
         ?? throw new RechazoTrabajoException($"El proveedor {vendorId} no existe en Sage.")).Key;
}

/// <summary>
/// <see cref="TiposTrabajo.ConvertirLiquidacion"/> (§5.2, decisión D1): la compra que hoy registra contabilidad a mano. Aplica cada línea
/// de la OC (ítems y <c>LIQUIDACION</c>) como línea propia (<c>AddPurchasesLine</c>, C7) y cierra la OC; referencia = la de la OC con espacio (<c>LIQ IMPORT 041-2026</c>),
/// fecha y cuenta por pagar de la OC. Idempotente: si la OC ya tiene compra, no crea otra.
/// </summary>
public sealed class ManejadorConvertirLiquidacion : IManejadorTrabajo
{
    public string Tipo => TiposTrabajo.ConvertirLiquidacion;

    public string Ejecutar(ContextoLote contexto, TrabajoTomado trabajo)
    {
        if (string.IsNullOrWhiteSpace(trabajo.PayloadJson)) throw new RechazoTrabajoException("El trabajo no trae la OC a convertir.");
        var p = Json.Leer<PayloadConvertirLiquidacion>(trabajo.PayloadJson!);
        using var odbc = OdbcSage.Abrir(contexto.Configuracion, trabajo.Ruc);
        var oc = odbc.Oc(p.PostOrder) ?? throw new RechazoTrabajoException($"La OC {p.PostOrder} no existe en Sage.");
        var referencia = ReferenciasLiquidacion.DeCompra(oc.Referencia);
        var f = contexto.Empresa.Factories;
        var proveedor = SageBusqueda.Uno(f.VendorFactory.List(), SageBusqueda.Igual("Vendor.ID", oc.VendorId))
                        ?? throw new RechazoTrabajoException($"El proveedor {oc.VendorId} no existe en Sage.");

        if ((odbc.CompraDeOc(oc.Referencia, oc.VendorRecord) ?? odbc.CompraPorReferencia(referencia, oc.VendorRecord)) is { } ya)
        {
            var cerrada = Cerrar(contexto.Empresa, proveedor.Key, oc.Referencia);
            return Json.Escribir(new ResultadoConvertirLiquidacion
            {
                Mensaje = $"La OC {oc.Referencia} ya tenía compra (PostOrder {ya}){(cerrada ? "; se cerró la OC" : string.Empty)}.",
                PostOrderCompra = ya, PostOrderOc = oc.PostOrder, Referencia = referencia, YaExistia = true,
            });
        }

        var po = SageBusqueda.Uno(f.PurchaseOrderFactory.List(), SageBusqueda.OcDe(proveedor.Key, oc.Referencia))
                 ?? throw new RechazoTrabajoException($"No se encontró una única OC {oc.Referencia}.");

        // Cabecera como la compra manual (F0, §9 del plan): vencimiento y descuento = fecha, sin términos, cuenta por pagar de la
        // OC (20000, C6). Idempotente por la OC o por la referencia (la compra no queda aplicada a la OC).
        var compra = f.PurchaseInvoiceFactory.Create();
        compra.VendorReference = proveedor.Key;
        compra.ReferenceNumber = referencia;
        compra.Date = oc.Fecha;
        compra.DateDue = oc.Fecha;
        compra.DiscountDate = oc.Fecha;
        compra.TermsDescription = string.Empty;
        compra.AccountReference = SageBusqueda.Cuenta(contexto.Empresa, oc.CuentaPorPagar);
        // Cada línea va como línea propia de la compra (AddPurchasesLine), NO aplicada a la OC: aplicada (AddOrderLine) el SDK deja la
        // fila del ítem con IncludeInInvLedger = 0 y la compra no sale en los reportes de inventario de Sage (Item Costing); con línea
        // propia queda en 1 como la manual (C7, verificado en la copia 2026-10-01). La OC se cierra al final.
        foreach (var linea in po.PurchaseOrderLines)
        {
            if (linea.Quantity != 0)
            {
                var x = compra.AddPurchasesLine();
                x.InventoryItemReference = linea.InventoryItemReference;
                x.Description = linea.Description;
                x.Quantity = linea.Quantity;
                x.UnitPrice = linea.UnitPrice;
                x.Amount = linea.Amount;
                x.AccountReference = linea.AccountReference;
                continue;
            }
            // Línea LIQUIDACION (sin ítem ni cantidad).
            var c = compra.AddPurchasesLine();
            c.Description = linea.Description;
            c.AccountReference = linea.AccountReference;
            c.Amount = linea.Amount;
        }

        // Avisos esperados: fecha fuera del período actual y cuenta por pagar que no es de proveedor. Solo los errores frenan.
        var problemas = new ValidationProblemList();
        compra.Validate(problemas);
        if (problemas.Any(x => x.Severity.ToString() == "Error"))
        {
            throw new RechazoTrabajoException("Sage rechazó la compra: " + SageBusqueda.Detalle(problemas));
        }
        compra.Save();
        Cerrar(contexto.Empresa, proveedor.Key, oc.Referencia);

        var nueva = (odbc.CompraDeOc(oc.Referencia, oc.VendorRecord) ?? odbc.CompraPorReferencia(referencia, oc.VendorRecord))
                    ?? throw new InvalidOperationException("Se guardó la compra pero no se la encuentra por ODBC.");
        return Json.Escribir(new ResultadoConvertirLiquidacion
        {
            Mensaje = $"Compra {referencia} creada desde la OC {oc.Referencia}.", PostOrderCompra = nueva, PostOrderOc = oc.PostOrder,
            Referencia = referencia,
        });
    }

    /// <summary>Cierra la OC si sigue abierta (su línea LIQUIDACION no queda aplicada). Devuelve si la cerró.</summary>
    private static bool Cerrar(Company empresa, EntityReference<Vendor> proveedor, string referencia)
    {
        var po = SageBusqueda.Uno(empresa.Factories.PurchaseOrderFactory.List(), SageBusqueda.OcDe(proveedor, referencia));
        if (po is null || po.IsClosed) return false;
        po.IsClosed = true;
        po.Save();
        return true;
    }
}

/// <summary>Búsquedas por ID en el SDK compartidas por los manejadores de la liquidación.</summary>
internal static class SageBusqueda
{
    public static FilterExpression Igual(string propiedad, string valor) =>
        FilterExpression.Equal(FilterExpression.Property(propiedad), FilterExpression.Constant(valor));

    public static FilterExpression OcDe(EntityReference<Vendor> proveedor, string referencia) => FilterExpression.AndAlso(
        FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.VendorReference"), FilterExpression.Constant(proveedor)),
        FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.ReferenceNumber"), FilterExpression.Constant(referencia)));

    public static T? Uno<T>(EntityList<T> lista, FilterExpression filtro) where T : Entity
    {
        var m = LoadModifiers.Create();
        m.Filters = filtro;
        lista.Load(m);
        return lista.Count == 1 ? lista.First() : null;
    }

    /// <summary>Los problemas de <c>Validate()</c> con sus propiedades (el <c>ToString()</c> del SDK solo da el tipo).</summary>
    public static string Detalle(ValidationProblemList problemas) => string.Join(" · ", problemas.Select(x =>
        x.GetType().Name + " {" + string.Join(", ", x.GetType().GetProperties()
            .Where(pr => pr.GetIndexParameters().Length == 0)
            .Select(pr => { try { return pr.Name + "=" + pr.GetValue(x); } catch { return pr.Name + "=?"; } })) + "}"));

    public static EntityReference<Account> Cuenta(Company empresa, string id) =>
        Uno(empresa.Factories.AccountFactory.List(), Igual("Account.ID", id))?.Key
        ?? throw new RechazoTrabajoException($"No existe en Sage la cuenta {id}.");

    /// <summary>Ítem de stock y su cuenta de inventario (<c>SageContext.GetInventoryAccount</c> del `.exe`).</summary>
    public static (EntityReference Item, EntityReference<Account> CuentaInventario) ItemDeStock(Company empresa, string id)
    {
        var item = Uno(empresa.Factories.InventoryItemFactory.List(), Igual("InventoryItem.ID", id))?.Key
                   ?? throw new RechazoTrabajoException($"No existe en Sage el ítem {id}.");
        if (item is EntityReference<StockItem> stock)
        {
            return (item, empresa.Factories.StockItemFactory.Load(stock).InventoryAccountReference);
        }
        throw new RechazoTrabajoException($"El ítem {id} no es de stock (la liquidación solo admite ítems de stock).");
    }
}

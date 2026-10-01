using System;
using System.Collections.Generic;
using System.Linq;
using Sage.Peachtree.API;
using Sage.Peachtree.API.Collections.Generic;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// <see cref="TiposTrabajo.ConvertirOcs"/>: reemplazo del worker COM <c>PSComInvoiceGenerate</c> (§6.2 del plan). Mismo filtro
/// (OC con filas con ítem sin recibir, con nº de factura, no anuladas, de los últimos N meses, sin fila en
/// <c>PurchaseOrderSync</c>) y mismos campos: nº de factura = <c>ShipToAddress1</c>, fecha = la de la OC, vencimiento =
/// <c>GoodThruDate</c> (o la fecha), términos = <c>ShipToAddress1</c>, ShipVia, envío 1/2/estado, cuenta por pagar de la OC y cada
/// fila con ítem y cantidad pendiente aplicada a su línea de la OC (<c>AddOrderLine</c>: sin COM ni Sage abierto en un escritorio).
/// <list type="bullet">
///   <item>Abre la compañía del RUC del trabajo: desaparece el riesgo del worker de escribir en la empresa abierta en Sage.</item>
///   <item><c>PurchaseOrderSync</c> se anota en el mismo trabajo (el worker lo hacía en el tick siguiente); si la compra ya existía
///   (reintento o el worker ya la hizo) solo se anota.</item>
///   <item>Como el worker, las filas sin ítem (las «.» y la contrapartida de la retención asumida) no pasan a la compra.</item>
///   <item><c>DiscountDate</c> vacía y precio unitario a 5 decimales, como las del worker (F0-c, F6).</item>
///   <item>La fila 0 (cuenta por pagar) lleva el nombre completo del proveedor (el worker lo cortaba a 30).</item>
///   <item>Compra mixta: las líneas con ítem de inventario van como líneas propias (no aplicadas a la OC) para que salgan en los
///   reportes de inventario de Sage, y la OC se cierra; el resto sigue aplicado, así la compra conserva su vínculo con la OC
///   (PLAN-OLA2-COMPRAS-SAGE, punto abierto de 2026-10-01).</item>
/// </list>
/// Un error en una OC no frena a las demás: queda en <see cref="ResultadoConvertirOcs.Errores"/> y se reintenta la próxima vez.
/// </summary>
public sealed class ManejadorConvertirOcs : IManejadorTrabajo
{
    public string Tipo => TiposTrabajo.ConvertirOcs;

    public string Ejecutar(ContextoLote contexto, TrabajoTomado trabajo)
    {
        var filtro = string.IsNullOrWhiteSpace(trabajo.PayloadJson) ? null : Json.Leer<PayloadConvertirOcs>(trabajo.PayloadJson!).PostOrders;
        var cfg = contexto.Configuracion;
        var sync = new SincronizacionCompras(cfg.PeachEbillsConnectionString);
        var resultado = new ResultadoConvertirOcs();

        using var odbc = OdbcSage.Abrir(cfg, trabajo.Ruc);
        var sincronizadas = sync.OcsSincronizadas(trabajo.Ruc);
        var pendientes = odbc.OcsPendientesDeCompra(DateTime.Today.AddMonths(-cfg.ConversionMesesAtras))
            .Where(o => !sincronizadas.Contains(o.PostOrder))
            .Where(o => filtro is not { Count: > 0 } || filtro.Contains(o.PostOrder))
            .OrderBy(o => o.PostOrder)
            .ToList();
        resultado.Pendientes = pendientes.Count;

        var cuentas = new Dictionary<string, EntityReference<Account>>();
        foreach (var oc in pendientes)
        {
            try
            {
                var existente = odbc.CompraDeOc(oc.Referencia, oc.VendorRecord);
                if (existente is { } ya)
                {
                    sync.Anotar(trabajo.Ruc, oc.PostOrder, ya);
                    resultado.SoloSincronizadas++;
                    continue;
                }

                var compra = Convertir(contexto.Empresa, oc, cuentas);
                var nueva = odbc.CompraDeOc(oc.Referencia, oc.VendorRecord)
                            ?? throw new InvalidOperationException("Se guardó la compra pero no se la encuentra por ODBC.");
                sync.Anotar(trabajo.Ruc, oc.PostOrder, nueva);
                resultado.Convertidas.Add(new CompraConvertida
                {
                    Factura = compra, PostOrderCompra = nueva, PostOrderOc = oc.PostOrder, ReferenciaOc = oc.Referencia,
                });
            }
            catch (Exception ex) when (ClasificadorErroresSage.Clasificar(ex.GetType().Name, ex.Message) is AccionAnteError.Fallar)
            {
                resultado.Errores.Add($"{oc.Referencia} (PostOrder {oc.PostOrder}): {ex.Message}");
            }
        }

        return Json.Escribir(resultado);
    }

    private static string Convertir(Company empresa, OcPendiente oc, Dictionary<string, EntityReference<Account>> cuentas)
    {
        var f = empresa.Factories;
        var proveedor = Uno(f.VendorFactory.List(), Igual("Vendor.ID", oc.VendorId))
                        ?? throw new InvalidOperationException($"No existe el proveedor {oc.VendorId}.");
        var po = Uno(f.PurchaseOrderFactory.List(), FilterExpression.AndAlso(
                     FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.VendorReference"), FilterExpression.Constant(proveedor.Key)),
                     FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.ReferenceNumber"), FilterExpression.Constant(oc.Referencia))))
                 ?? throw new InvalidOperationException($"No se encontró una única OC {oc.Referencia} del proveedor {oc.VendorId}.");

        if (!cuentas.TryGetValue(oc.CuentaPorPagar, out var cxp))
        {
            cxp = Uno(f.AccountFactory.List(), Igual("Account.ID", oc.CuentaPorPagar))?.Key
                  ?? throw new InvalidOperationException($"No existe la cuenta por pagar {oc.CuentaPorPagar}.");
            cuentas[oc.CuentaPorPagar] = cxp;
        }

        var compra = f.PurchaseInvoiceFactory.Create();
        compra.VendorReference = proveedor.Key;
        compra.ReferenceNumber = oc.Direccion1;
        compra.Date = oc.Fecha;
        compra.DateDue = oc.FechaRegistro ?? oc.Fecha;
        compra.DiscountDate = null;
        compra.AccountReference = cxp;
        compra.TermsDescription = oc.Direccion1;
        compra.ShipVia = oc.ShipVia;
        compra.ShipToAddress.Name = string.Empty;
        compra.ShipToAddress.Address.Address1 = oc.Direccion1;
        compra.ShipToAddress.Address.Address2 = oc.Direccion2;
        compra.ShipToAddress.Address.State = oc.Estado;

        // Compra mixta (2026-10-01): si la OC trae ítems de inventario, esas líneas van como líneas propias de la compra
        // (AddPurchasesLine) para que Sage las marque para los reportes de inventario (aplicadas a la OC el SDK deja
        // IncludeInInvLedger = 0); el resto (IVA, retenciones, servicios) sigue aplicado a la OC, así la compra conserva su vínculo
        // (INV_POSOOrderNumber) para Retenciones y ATS. Al final se cierra la OC (las de inventario quedan sin recibir en ella).
        var mixta = false;
        var aplicadas = 0;
        foreach (var linea in po.PurchaseOrderLines)
        {
            // Filtro del worker: fila con ítem y cantidad pendiente (quedan fuera AUT-SRI, las «.» y las asumidas sin ítem).
            if (linea.InventoryItemReference is null) continue;
            if (Math.Round(linea.QuantityReceived, 2) >= Math.Round(linea.Quantity, 2)) continue;
            if (EsInventario(empresa, linea.InventoryItemReference))
            {
                var x = compra.AddPurchasesLine();
                x.InventoryItemReference = linea.InventoryItemReference;
                x.Description = linea.Description;
                x.Quantity = linea.Quantity;
                x.UnitPrice = Math.Round(linea.UnitPrice, 5, MidpointRounding.AwayFromZero);
                x.Amount = linea.Amount;
                x.AccountReference = linea.AccountReference;
                if (linea.JobReference is not null) x.JobReference = linea.JobReference;
                mixta = true;
                aplicadas++;
                continue;
            }
            var l = compra.AddOrderLine(linea);
            l.Quantity = linea.Quantity;
            // La importación COM del worker dejaba el precio unitario a 5 decimales (el monto no cambia): se replica para que
            // lo que leen Retenciones/ATS sea lo mismo que hoy.
            l.UnitPrice = Math.Round(linea.UnitPrice, 5, MidpointRounding.AwayFromZero);
            l.Amount = linea.Amount;
            l.Description = linea.Description;
            l.AccountReference = linea.AccountReference;
            aplicadas++;
        }
        if (aplicadas == 0) throw new InvalidOperationException("La OC no tiene líneas pendientes de recibir.");

        var problemas = new ValidationProblemList();
        if (!compra.Validate(problemas))
        {
            throw new InvalidOperationException("Sage rechazó la compra: " + string.Join(" · ", problemas.Select(p => p.ToString())));
        }
        compra.Save();
        if (mixta)
        {
            var abierta = Uno(f.PurchaseOrderFactory.List(), FilterExpression.AndAlso(
                FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.VendorReference"), FilterExpression.Constant(proveedor.Key)),
                FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.ReferenceNumber"), FilterExpression.Constant(oc.Referencia))));
            if (abierta is { IsClosed: false })
            {
                abierta.IsClosed = true;
                abierta.Save();
            }
        }
        return oc.Direccion1;
    }

    /// <summary>¿El ítem lleva inventario (stock, sub-ítem, serializado o ensamblado)?</summary>
    private static bool EsInventario(Company empresa, EntityReference item)
    {
        // La referencia de la línea de la OC viene sin el tipo concreto: se carga el ítem para saberlo.
        var cargado = empresa.Factories.InventoryItemFactory.Load(item);
        return cargado is StockItem or SubStockItem or SerializedStockItem or AssemblyItem or SerializedAssemblyItem;
    }

    private static FilterExpression Igual(string propiedad, string valor) =>
        FilterExpression.Equal(FilterExpression.Property(propiedad), FilterExpression.Constant(valor));

    private static T? Uno<T>(EntityList<T> lista, FilterExpression filtro) where T : Entity
    {
        var m = LoadModifiers.Create();
        m.Filters = filtro;
        lista.Load(m);
        return lista.Count == 1 ? lista.First() : null;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sage.Peachtree.API;
using Sage.Peachtree.API.Collections.Generic;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.SageBridge.Logica;

/// <summary>
/// <see cref="TiposTrabajo.GuardarOc"/>: proveedor (port de <c>Sage50us.LoadVendor</c>) + Purchase Order (port de
/// <c>Sage50us.LoadPurchaseOrder</c>) con la numeración del `.exe`. Diferencias con el `.exe` (§6.1 y §8 del plan):
/// <list type="bullet">
///   <item>C6: actualizar es en el lugar — se cargan la OC y sus líneas, se reemplazan y se guarda una sola vez. El `.exe`
///   borraba la OC antes de armar la nueva (si algo fallaba, se perdía) y le daba un nº de retención nuevo.</item>
///   <item>C7: <c>Validate()</c> fallido vuelve con los problemas (el `.exe` lo perdía en silencio).</item>
///   <item>C8: se rechaza un nº de retención ya usado por otra OC (<c>CheckForTwhNumberAlreadyUsed</c> existía y nunca se llamaba).</item>
///   <item>Una OC ya recibida (convertida en compra) no se toca (el `.exe` deshabilitaba «Guardar»: «Contabilizado»).</item>
/// </list>
/// Reintentar el trabajo es seguro: si la OC ya se guardó, la segunda vez la encuentra y la actualiza con lo mismo.
/// </summary>
public sealed class ManejadorGuardarOc : IManejadorTrabajo
{
    public string Tipo => TiposTrabajo.GuardarOc;

    public string Ejecutar(ContextoLote contexto, TrabajoTomado trabajo)
    {
        if (string.IsNullOrWhiteSpace(trabajo.PayloadJson)) throw new RechazoTrabajoException("El trabajo no trae datos de la OC.");
        var p = Json.Leer<PayloadGuardarOc>(trabajo.PayloadJson!);
        if (string.IsNullOrWhiteSpace(p.NumeroFactura) || p.Lineas.Count == 0 || string.IsNullOrWhiteSpace(p.Proveedor.Id))
        {
            throw new RechazoTrabajoException("Faltan el nº de factura, las líneas o el proveedor de la OC.");
        }

        var f = contexto.Empresa.Factories;
        var cache = new Referencias(contexto.Empresa);
        using var odbc = OdbcSage.Abrir(contexto.Configuracion, trabajo.Ruc);

        // ---- Proveedor ----
        var (proveedor, estadoProveedor) = GuardarProveedor(contexto.Empresa, cache, p.Proveedor);

        // ---- OC existente (misma factura del mismo proveedor) o nueva ----
        var existente = odbc.OcExistente(p.Proveedor.Id, p.NumeroFactura);
        PurchaseOrder oc;
        string numeroOc;
        string numeroRetencion = string.Empty;
        if (existente is { } ex)
        {
            if (odbc.FueRecibida(ex.PostOrder))
            {
                throw new RechazoTrabajoException($"La OC {ex.Referencia} ya se convirtió en compra (contabilizada): no se puede actualizar.");
            }
            oc = Uno(f.PurchaseOrderFactory.List(), FilterExpression.AndAlso(
                    FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.VendorReference"), FilterExpression.Constant(proveedor.Key)),
                    FilterExpression.Equal(FilterExpression.Property("PurchaseOrder.ReferenceNumber"), FilterExpression.Constant(ex.Referencia))))
                 ?? throw new RechazoTrabajoException($"Se encuentra más de una coincidencia con Purchase Order: {ex.Referencia}.");
            foreach (var linea in oc.PurchaseOrderLines.ToList()) oc.RemoveLine(linea);
            numeroOc = ex.Referencia;
            if (p.RequiereRetencion)
            {
                // C6: se conserva el nº de retención que ya tenía (salvo que el digitador haya escrito otro).
                numeroRetencion = !string.IsNullOrWhiteSpace(p.NumeroRetencion) ? p.NumeroRetencion!.Trim()
                    : NumeracionCompras.EsNumeroRetencion(ex.Retencion) ? ex.Retencion
                    : NumeracionCompras.SiguienteRetencion(p.SerieRetencion, odbc.NumerosRetencion(p.SerieRetencion), p.SecuencialRetencionInicial);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(p.NumeroOc))
            {
                numeroOc = p.NumeroOc!.Trim();
                if (!NumeracionCompras.EsNumeroOc(numeroOc)) throw new RechazoTrabajoException($"Nº de OC «{numeroOc}» incorrecto (xx-xxxx).");
                if (odbc.ExisteReferenciaOc(numeroOc)) throw new RechazoTrabajoException($"El nº de OC {numeroOc} ya existe en Sage.");
            }
            else
            {
                numeroOc = NumeracionCompras.SiguienteOc(p.PrefijoOc, odbc.ReferenciasOc(p.PrefijoOc));
            }
            oc = f.PurchaseOrderFactory.Create();
            if (p.RequiereRetencion)
            {
                numeroRetencion = !string.IsNullOrWhiteSpace(p.NumeroRetencion) ? p.NumeroRetencion!.Trim()
                    : NumeracionCompras.SiguienteRetencion(p.SerieRetencion, odbc.NumerosRetencion(p.SerieRetencion), p.SecuencialRetencionInicial);
            }
        }

        if (p.RequiereRetencion)
        {
            if (!NumeracionCompras.EsNumeroRetencion(numeroRetencion)) throw new RechazoTrabajoException($"Nº de retención «{numeroRetencion}» incorrecto.");
            if (odbc.OcConRetencion(numeroRetencion, existente?.PostOrder ?? -1) is { } otra)
            {
                throw new RechazoTrabajoException($"El nº de retención {numeroRetencion} ya está usado en la OC {otra}.");
            }
        }

        // ---- Cabecera (CheckPurchaseOrder + cierre de LoadPurchaseOrder) ----
        oc.ShipVia = p.ShipVia;
        oc.Date = Fecha(p.Fecha);
        oc.VendorReference = proveedor.Key;
        oc.ShipToAddress.Address.Address1 = p.NumeroFactura;
        oc.TermsDescription = p.NumeroFactura;
        oc.MainAddress.Name = p.Proveedor.Nombre;
        oc.MainAddress.Address.Address1 = p.Proveedor.Direccion1;
        oc.MainAddress.Address.Address2 = p.Proveedor.Direccion2;
        oc.MainAddress.Address.Country = p.Proveedor.Identificacion;
        oc.GoodThroughDate = Fecha(p.FechaRegistro);
        oc.ShipToAddress.Address.Address2 = numeroRetencion;
        oc.AccountReference = cache.Cuenta(p.CuentaPorPagar);
        oc.ReferenceNumber = numeroOc;
        oc.ShipToAddress.Address.State = p.EstadoSustento;
        oc.ShipToAddress.Address.Zip = p.Zip ?? string.Empty;

        // ---- Líneas, en el orden y con el orden de asignación del `.exe` ----
        foreach (var l in p.Lineas)
        {
            var linea = oc.AddLine();
            if (l.Tipo == "Relleno")
            {
                linea.Description = ".";
                continue;
            }
            if (!string.IsNullOrEmpty(l.Item)) linea.InventoryItemReference = cache.Item(l.Item!);
            linea.Description = l.Descripcion;
            switch (l.Tipo)
            {
                case "Detalle":
                case "Impuesto":
                    linea.Quantity = l.Cantidad;
                    linea.Amount = l.Monto;
                    linea.UnitPrice = l.PrecioUnitario;
                    break;
                case "Autorizacion":
                    break;
                case "Retencion":
                case "RetencionAsumida":
                    linea.Quantity = l.Cantidad;
                    linea.UnitPrice = l.PrecioUnitario;
                    linea.Amount = l.Monto;
                    break;
                default: // Propina e impuestos que no son IVA: cantidad 1
                    linea.Amount = l.Monto;
                    linea.Quantity = l.Cantidad;
                    linea.UnitPrice = l.PrecioUnitario;
                    break;
            }
            if (!string.IsNullOrEmpty(l.Cuenta)) linea.AccountReference = cache.Cuenta(l.Cuenta!);
            if (!string.IsNullOrEmpty(l.Job)) linea.JobReference = cache.Job(l.Job!);
        }

        var problemas = new ValidationProblemList();
        if (!oc.Validate(problemas))
        {
            throw new RechazoTrabajoException("Sage rechazó la OC: " + string.Join(" · ", problemas.Select(x => x.ToString())));
        }
        oc.Save();

        var guardada = odbc.OcExistente(p.Proveedor.Id, p.NumeroFactura);
        return Json.Escribir(new ResultadoGuardarOc
        {
            Accion = existente is null ? "Creada" : "Actualizada",
            Mensaje = $"OC {numeroOc} {(existente is null ? "creada" : "actualizada")} para la factura {p.NumeroFactura}.",
            NumeroOc = numeroOc,
            NumeroRetencion = numeroRetencion,
            PostOrder = guardada?.PostOrder ?? 0,
            Proveedor = estadoProveedor,
        });
    }

    /// <summary>Port de <c>LoadVendor</c>: crea el proveedor o actualiza los campos que el `.exe` actualiza, y lo reactiva.</summary>
    private static (Vendor Proveedor, string Estado) GuardarProveedor(Company empresa, Referencias cache, ProveedorContrato p)
    {
        var v = Uno(empresa.Factories.VendorFactory.List(), FilterExpression.Equal(FilterExpression.Property("Vendor.ID"), FilterExpression.Constant(p.Id)));
        if (v is null)
        {
            if (!p.EsNuevo) throw new RechazoTrabajoException($"El proveedor {p.Id} no existe en Sage.");
            v = empresa.Factories.VendorFactory.Create();
            v.AccountNumber = p.TipoIdentificacion;
            v.MailToContact.Address.Country = p.Identificacion;
            v.MailToContact.Address.Address1 = p.Direccion1;
            v.MailToContact.Address.Address2 = p.Direccion2;
            if (!string.IsNullOrEmpty(p.CuentaGasto)) v.ExpenseAccountReference = cache.Cuenta(p.CuentaGasto);
            var cf = v.CustomFieldValues;
            if (cf != null)
            {
                if (!string.IsNullOrEmpty(p.CustomField0)) cf[0].Value = p.CustomField0;
                if (!string.IsNullOrEmpty(p.CustomField1)) cf[1].Value = p.CustomField1;
                if (!string.IsNullOrEmpty(p.CustomField2)) cf[2].Value = p.CustomField2;
                if (!string.IsNullOrEmpty(p.CustomField3)) cf[3].Value = p.CustomField3;
                if (!string.IsNullOrEmpty(p.CustomField4) && p.TipoIdentificacion == "06") cf[4].Value = p.CustomField4;
            }
            v.Email = p.Email;
            v.ID = p.Id;
            v.Name = p.Nombre;
            if (v.PhoneNumbers is { Count: >= 2 } tel)
            {
                tel[0].Number = p.Telefono;
                tel[1].Number = p.Telefono2;
            }
            v.IsInactive = false;
            v.Save();
            return (v, "Creado");
        }

        if (p.EsNuevo && !string.Equals(v.MailToContact.Address.Country?.Trim(), p.Identificacion, StringComparison.OrdinalIgnoreCase))
        {
            throw new RechazoTrabajoException($"El ID de proveedor {p.Id} ya lo usa otro proveedor en Sage ({v.Name}). Elija otro ID.");
        }

        var cambios = false;
        void Poner(string actual, string nuevo, Action<string> asignar)
        {
            if ((actual ?? string.Empty) == (nuevo ?? string.Empty)) return;
            asignar(nuevo ?? string.Empty);
            cambios = true;
        }
        Poner(v.AccountNumber, p.TipoIdentificacion, x => v.AccountNumber = x);
        Poner(v.MailToContact.Address.Address1, p.Direccion1, x => v.MailToContact.Address.Address1 = x);
        Poner(v.MailToContact.Address.Address2, p.Direccion2, x => v.MailToContact.Address.Address2 = x);
        var campos = v.CustomFieldValues;
        if (campos != null)
        {
            if (campos.Count == 0) throw new RechazoTrabajoException("Customfields en proveedores del SAGE no están habilitados");
            var nuevos = new[] { p.CustomField0, p.CustomField1, p.CustomField2, p.CustomField3, p.CustomField4 };
            for (var i = 0; i < nuevos.Length && i < campos.Count; i++)
            {
                var campo = campos[i];
                Poner(campo.Value?.ToString() ?? string.Empty, nuevos[i], x => campo.Value = x);
            }
        }
        Poner(v.Email, p.Email, x => v.Email = x);
        Poner(v.Name, p.Nombre, x => v.Name = x);
        if (v.PhoneNumbers is { Count: >= 2 } telefonos)
        {
            Poner(telefonos[0].Number, p.Telefono, x => telefonos[0].Number = x);
            Poner(telefonos[1].Number, p.Telefono2, x => telefonos[1].Number = x);
        }
        if (v.IsInactive)
        {
            v.IsInactive = false;
            cambios = true;
        }
        if (cambios) v.Save();
        return (v, cambios ? "Actualizado" : "SinCambios");
    }

    private static DateTime Fecha(string texto) =>
        DateTime.ParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static T? Uno<T>(EntityList<T> lista, FilterExpression filtro) where T : Entity
    {
        var m = LoadModifiers.Create();
        m.Filters = filtro;
        lista.Load(m);
        return lista.Count == 1 ? lista.First() : null;
    }

    /// <summary>Referencias por ID (ítems, cuentas, jobs), cacheadas durante el trabajo.</summary>
    private sealed class Referencias
    {
        private readonly Company _empresa;
        private readonly Dictionary<string, EntityReference> _items = new Dictionary<string, EntityReference>();
        private readonly Dictionary<string, EntityReference<Account>> _cuentas = new Dictionary<string, EntityReference<Account>>();
        private readonly Dictionary<string, EntityReference<Job>> _jobs = new Dictionary<string, EntityReference<Job>>();

        public Referencias(Company empresa) => _empresa = empresa;

        public EntityReference Item(string id) => Buscar(_items, id, () =>
            Uno(_empresa.Factories.InventoryItemFactory.List(), Igual("InventoryItem.ID", id))?.Key, "el ítem");

        public EntityReference<Account> Cuenta(string id) => Buscar(_cuentas, id, () =>
            Uno(_empresa.Factories.AccountFactory.List(), Igual("Account.ID", id))?.Key, "la cuenta");

        public EntityReference<Job> Job(string id) => Buscar(_jobs, id, () =>
            Uno(_empresa.Factories.JobFactory.List(), Igual("Job.ID", id))?.Key, "el job");

        private static FilterExpression Igual(string propiedad, string valor) =>
            FilterExpression.Equal(FilterExpression.Property(propiedad), FilterExpression.Constant(valor));

        private static TRef Buscar<TRef>(Dictionary<string, TRef> cache, string id, Func<TRef?> cargar, string que) where TRef : class
        {
            if (cache.TryGetValue(id, out var r)) return r;
            r = cargar() ?? throw new RechazoTrabajoException($"No existe en Sage {que} {id}.");
            cache[id] = r;
            return r;
        }
    }
}

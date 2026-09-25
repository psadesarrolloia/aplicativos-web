using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PsaWeb.Compras.Armado;
using PsaWeb.Compras.Catalogo;
using PsaWeb.SageBridge.Contratos;

namespace PsaWeb.Compras.Bridge;

/// <summary>Trabajo listo para encolar: tipo, payload JSON y clave de idempotencia.</summary>
public sealed record SolicitudTrabajo(string Tipo, string PayloadJson, string ClaveIdempotencia);

/// <summary>Convierte la OC armada por la web en el trabajo <see cref="TiposTrabajo.GuardarOc"/> del Sage Bridge.</summary>
public static class SolicitudGuardarOc
{
    /// <param name="numeroOcAutomatico">El Bridge asigna el siguiente nº de OC al guardar (el de <paramref name="oc"/> era solo una vista previa).</param>
    /// <param name="numeroRetencionAutomatico">Ídem con el nº de retención.</param>
    /// <param name="serieRetencion">Serie del establecimiento electrónico con <c>startNumerationTwh</c>, o <c>001-001</c>.</param>
    public static SolicitudTrabajo Crear(OcArmada oc, ProveedorSage proveedor, bool numeroOcAutomatico, bool numeroRetencionAutomatico,
        string serieRetencion = "001-001", int secuencialRetencionInicial = 0)
    {
        var payload = new PayloadGuardarOc
        {
            CuentaPorPagar = oc.CuentaPorPagar,
            EstadoSustento = oc.EstadoSustento,
            Fecha = oc.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FechaRegistro = oc.FechaRegistro.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Lineas = oc.Lineas.Select(l => new LineaOcContrato
            {
                Cantidad = l.Cantidad,
                Cuenta = l.CuentaId,
                Descripcion = l.Descripcion,
                Item = l.ItemId,
                Job = l.JobId,
                Monto = l.Monto,
                PrecioUnitario = l.PrecioUnitario,
                Tipo = l.Tipo.ToString(),
            }).ToList(),
            NumeroFactura = oc.NumeroFactura,
            NumeroOc = numeroOcAutomatico ? null : oc.Referencia,
            NumeroRetencion = numeroRetencionAutomatico || !oc.LlevaRetencion ? null : oc.NumeroRetencion,
            PrefijoOc = oc.Referencia.Length > 2 && oc.Referencia[2] == '-' ? oc.Referencia[..2] : "OC",
            Proveedor = new ProveedorContrato
            {
                CuentaGasto = proveedor.CuentaGasto,
                CustomField0 = proveedor.CustomField0,
                CustomField1 = proveedor.CustomField1,
                CustomField2 = proveedor.CustomField2,
                CustomField3 = proveedor.CustomField3,
                CustomField4 = proveedor.CustomField4,
                Direccion1 = proveedor.Direccion1,
                Direccion2 = proveedor.Direccion2,
                Email = proveedor.Email,
                EsNuevo = proveedor.RecordNumber is null,
                Id = proveedor.Id,
                Identificacion = proveedor.Pais,
                Nombre = proveedor.Nombre,
                Telefono = proveedor.Telefono,
                Telefono2 = proveedor.Telefono2,
                TipoIdentificacion = proveedor.TipoIdentificacion,
            },
            RequiereRetencion = oc.LlevaRetencion,
            SecuencialRetencionInicial = secuencialRetencionInicial,
            SerieRetencion = serieRetencion,
            ShipVia = oc.ShipVia,
            Zip = oc.Zip,
        };
        var json = JsonSerializer.Serialize(payload);
        // Mismo contenido = mismo trabajo (doble clic); contenido distinto de la misma factura = trabajo nuevo (actualización).
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
        return new SolicitudTrabajo(TiposTrabajo.GuardarOc, json, $"oc|{proveedor.Id}|{oc.NumeroFactura}|{huella}");
    }
}

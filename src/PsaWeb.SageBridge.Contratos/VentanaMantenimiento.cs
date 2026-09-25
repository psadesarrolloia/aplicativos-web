using System;
using System.Globalization;

namespace PsaWeb.SageBridge.Contratos;

/// <summary>
/// Franja horaria diaria (hora local del servidor) en la que el Bridge NO abre una compañía, porque el backup
/// automático de Sage falla si la compañía está abierta por el SDK (F0-g). Puede cruzar la medianoche
/// (p. ej. 22:00–06:00). Se escribe como <c>"HH:mm-HH:mm"</c>.
/// </summary>
public sealed class VentanaMantenimiento
{
    public VentanaMantenimiento(TimeSpan inicio, TimeSpan fin)
    {
        if (inicio < TimeSpan.Zero || inicio >= TimeSpan.FromDays(1) || fin < TimeSpan.Zero || fin >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(inicio), "Las horas deben estar entre 00:00 y 23:59.");
        }

        Inicio = inicio;
        Fin = fin;
    }

    public TimeSpan Inicio { get; }
    public TimeSpan Fin { get; }

    /// <summary>Inicio igual a fin = sin ventana (el Bridge trabaja todo el día).</summary>
    public bool Vacia => Inicio == Fin;

    public static VentanaMantenimiento Ninguna { get; } = new VentanaMantenimiento(TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>Lee <c>"22:00-06:00"</c>. Texto vacío o nulo = sin ventana.</summary>
    public static VentanaMantenimiento Parsear(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return Ninguna;
        }

        if (!TryParsear(texto, out var ventana))
        {
            throw new FormatException($"Ventana de mantenimiento inválida: «{texto}». Formato esperado: HH:mm-HH:mm.");
        }

        return ventana!;
    }

    public static bool TryParsear(string? texto, out VentanaMantenimiento? ventana)
    {
        ventana = null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            ventana = Ninguna;
            return true;
        }

        var partes = texto!.Split('-');
        if (partes.Length != 2
            || !TimeSpan.TryParseExact(partes[0].Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var inicio)
            || !TimeSpan.TryParseExact(partes[1].Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var fin)
            || inicio >= TimeSpan.FromDays(1) || fin >= TimeSpan.FromDays(1))
        {
            return false;
        }

        ventana = new VentanaMantenimiento(inicio, fin);
        return true;
    }

    /// <summary>¿La hora local <paramref name="ahora"/> cae dentro de la ventana? El fin es exclusivo.</summary>
    public bool Contiene(DateTime ahora)
    {
        if (Vacia)
        {
            return false;
        }

        var hora = ahora.TimeOfDay;
        return Inicio < Fin
            ? hora >= Inicio && hora < Fin
            : hora >= Inicio || hora < Fin; // cruza la medianoche
    }

    /// <summary>Momento en que termina la ventana en curso, o <c>null</c> si <paramref name="ahora"/> está fuera.</summary>
    public DateTime? FinDesde(DateTime ahora)
    {
        if (!Contiene(ahora))
        {
            return null;
        }

        var fin = ahora.Date + Fin;
        return fin > ahora ? fin : fin.AddDays(1);
    }

    public override string ToString() => Vacia ? string.Empty : $"{Inicio:hh\\:mm}-{Fin:hh\\:mm}";
}

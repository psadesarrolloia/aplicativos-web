namespace PsaWeb.Shared.Components;

/// <summary>Una opción de <see cref="PsaAutocomplete"/>: código (lo que matchea
/// lo tipeado) + una etiqueta opcional para mostrar junto al código.</summary>
public sealed record SugerenciaAutocompletado(string Codigo, string Etiqueta = "");

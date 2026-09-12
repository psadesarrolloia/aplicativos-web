namespace PsaWeb.Ats.Anulados;

/// <summary>
/// Fila cruda de <c>JrnlHdr</c> — forma compartida entre la consulta de
/// comprobantes anulados y la de órdenes de compra vinculadas (mismas
/// columnas relevantes). Port de <c>LoadCanceled</c> (<c>ATSfromPeach</c>).
/// </summary>
public sealed record FilaJrnlHdrAts(
    long PostOrder,
    string Reference,
    long CustVendId,
    int JournalEx,
    int JrnlKeyJournal,
    string InvPosoOrderNumber,
    string ShipVia,
    string TermsDescription,
    string ShipToAddress2);

namespace Antifestival.FiscalApp.Models;

public class FiscalLogDto
{
    public long Id { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseText { get; set; }
    public int? ReturnCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

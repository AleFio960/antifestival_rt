namespace Antifestival.FiscalApp.Models;

public class ReceiptSummaryDto
{
    public string ReceiptNumber { get; set; } = "";
    public string? ReceiptType { get; set; }
    public DateTimeOffset? ReceiptDate { get; set; }
    public decimal? TotalMoney { get; set; }
    public decimal PaymentCash { get; set; }
    public decimal PaymentElectronic { get; set; }
    public string? PaymentTypeNames { get; set; }
    public string FiscalStatus { get; set; } = "";
    public string? QueueStatus { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? FatturaproUrl { get; set; }
    public string? LastError { get; set; }
    public int? FatturaproReturnCode { get; set; }
}

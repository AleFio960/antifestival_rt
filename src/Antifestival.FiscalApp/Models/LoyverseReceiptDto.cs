namespace Antifestival.FiscalApp.Models;

public class LoyverseReceiptDto
{
    public string ReceiptNumber { get; set; } = "";
    public string? ReceiptType { get; set; }
    public string? RefundFor { get; set; }
    public DateTimeOffset? ReceiptDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? StoreId { get; set; }
    public string? PosDeviceId { get; set; }
    public string? EmployeeId { get; set; }
    public string? CustomerId { get; set; }
    public decimal? TotalMoney { get; set; }
    public decimal? TotalTax { get; set; }
    public decimal? TotalDiscount { get; set; }
    public decimal PaymentCash { get; set; }
    public decimal PaymentElectronic { get; set; }
    public string? PaymentTypeNames { get; set; }
    public string PayloadJson { get; set; } = "";
    public List<LoyverseReceiptRowDto> Rows { get; set; } = new();
}

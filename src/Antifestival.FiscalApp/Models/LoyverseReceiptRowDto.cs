namespace Antifestival.FiscalApp.Models;

public class LoyverseReceiptRowDto
{
    public string ReceiptNumber { get; set; } = "";
    public string LineId { get; set; } = "";
    public string? Sku { get; set; }
    public string? ItemId { get; set; }
    public string? VariantId { get; set; }
    public string? ItemName { get; set; }
    public string? VariantName { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? GrossTotalMoney { get; set; }
    public decimal? TotalDiscount { get; set; }
    public decimal? TotalMoney { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? TaxAmount { get; set; }
    public string PayloadJson { get; set; } = "";
}

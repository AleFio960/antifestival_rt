using Antifestival.FiscalApp.Models;
using System.Text.Json;

namespace Antifestival.FiscalApp.Utils;

public static class LoyverseMapper
{
    // Nomi tipo pagamento riconosciuti come CONTANTI (case-insensitive)
    private static readonly string[] CashKeywords =
        { "CASH", "CONTANTI", "CONTANTE", "CASSA", "DENARO", "MONETA" };

    public static List<LoyverseReceiptDto> MapReceiptsFromRawJson(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var result = new List<LoyverseReceiptDto>();

        if (!doc.RootElement.TryGetProperty("receipts", out var receiptsElement))
            return result;

        foreach (var receiptElement in receiptsElement.EnumerateArray())
        {
            var receipt = new LoyverseReceiptDto
            {
                ReceiptNumber = GetString(receiptElement, "receipt_number") ?? "",
                ReceiptType = GetString(receiptElement, "receipt_type"),
                RefundFor = GetString(receiptElement, "refund_for"),
                ReceiptDate = GetDate(receiptElement, "receipt_date"),
                CreatedAt = GetDate(receiptElement, "created_at"),
                UpdatedAt = GetDate(receiptElement, "updated_at"),
                CancelledAt = GetDate(receiptElement, "cancelled_at"),
                StoreId = GetString(receiptElement, "store_id"),
                PosDeviceId = GetString(receiptElement, "pos_device_id"),
                EmployeeId = GetString(receiptElement, "employee_id"),
                CustomerId = GetString(receiptElement, "customer_id"),
                TotalMoney = GetDecimal(receiptElement, "total_money"),
                TotalTax = GetDecimal(receiptElement, "total_tax"),
                TotalDiscount = GetDecimal(receiptElement, "total_discount"),
                PayloadJson = receiptElement.GetRawText()
            };

            if (receiptElement.TryGetProperty("payments", out var paymentsElement) &&
                paymentsElement.ValueKind == JsonValueKind.Array)
            {
                var paymentNames = new List<string>();
                foreach (var payment in paymentsElement.EnumerateArray())
                {
                    var rawName = GetString(payment, "name") ?? "";
                    var paymentType = GetString(payment, "type")?.ToUpperInvariant() ?? "";
                    var amount = GetDecimal(payment, "money_amount") ?? 0;
                    if (!string.IsNullOrWhiteSpace(rawName))
                        paymentNames.Add(rawName);
                    // Usa il campo "type" di Loyverse: CASH = contanti, tutto il resto = elettronico
                    bool isCash = paymentType == "CASH"
                        || CashKeywords.Any(k => rawName.ToUpperInvariant().Contains(k));
                    if (isCash)
                        receipt.PaymentCash += amount;
                    else
                        receipt.PaymentElectronic += amount;
                }
                receipt.PaymentTypeNames = paymentNames.Count > 0 ? string.Join(", ", paymentNames) : null;
            }

            if (receiptElement.TryGetProperty("line_items", out var rowsElement) &&
                rowsElement.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var rowElement in rowsElement.EnumerateArray())
                {
                    decimal? taxRate = null;
                    decimal? taxAmount = null;

                    if (rowElement.TryGetProperty("line_taxes", out var taxesElement) &&
                        taxesElement.ValueKind == JsonValueKind.Array &&
                        taxesElement.GetArrayLength() > 0)
                    {
                        var firstTax = taxesElement[0];
                        taxRate = GetDecimal(firstTax, "rate");
                        taxAmount = GetDecimal(firstTax, "amount");
                    }

                    var row = new LoyverseReceiptRowDto
                    {
                        ReceiptNumber = receipt.ReceiptNumber,
                        LineId = GetString(rowElement, "line_item_id") ?? $"{receipt.ReceiptNumber}-{i}",
                        Sku = GetString(rowElement, "sku"),
                        ItemId = GetString(rowElement, "item_id"),
                        VariantId = GetString(rowElement, "variant_id"),
                        ItemName = GetString(rowElement, "item_name"),
                        VariantName = GetString(rowElement, "variant_name"),
                        Quantity = GetDecimal(rowElement, "quantity"),
                        UnitPrice = GetDecimal(rowElement, "price"),
                        GrossTotalMoney = GetDecimal(rowElement, "gross_total_money"),
                        TotalDiscount = GetDecimal(rowElement, "total_discount"),
                        TotalMoney = GetDecimal(rowElement, "total_money"),
                        TaxRate = taxRate,
                        TaxAmount = taxAmount,
                        PayloadJson = rowElement.GetRawText()
                    };

                    receipt.Rows.Add(row);
                    i++;
                }
            }

            result.Add(receipt);
        }

        return result;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    private static decimal? GetDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d))
            return d;
        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ds))
            return ds;
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var s = GetString(element, name);
        if (DateTimeOffset.TryParse(s, out var dto))
            return dto;
        return null;
    }
}

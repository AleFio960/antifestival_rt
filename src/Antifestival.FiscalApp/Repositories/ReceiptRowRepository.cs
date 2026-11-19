using Antifestival.FiscalApp.Data;
using Antifestival.FiscalApp.Models;
using Dapper;

namespace Antifestival.FiscalApp.Repositories;

public class ReceiptRowRepository
{
    private readonly DbConnectionFactory _factory;

    public ReceiptRowRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task UpsertAsync(LoyverseReceiptRowDto row)
    {
        const string sql = @"
MERGE loyverse.receipt_rows AS target
USING (SELECT @receipt_number AS receipt_number, @line_id AS line_id) AS source
ON target.receipt_number = source.receipt_number AND target.line_id = source.line_id
WHEN MATCHED THEN
    UPDATE SET
        sku = @sku,
        item_id = @item_id,
        variant_id = @variant_id,
        item_name = @item_name,
        variant_name = @variant_name,
        quantity = @quantity,
        unit_price = @unit_price,
        gross_total_money = @gross_total_money,
        total_discount = @total_discount,
        total_money = @total_money,
        tax_rate = @tax_rate,
        tax_amount = @tax_amount,
        payload_json = @payload_json
WHEN NOT MATCHED THEN
    INSERT (
        receipt_number, line_id, sku, item_id, variant_id, item_name, variant_name,
        quantity, unit_price, gross_total_money, total_discount, total_money, tax_rate,
        tax_amount, payload_json
    )
    VALUES (
        @receipt_number, @line_id, @sku, @item_id, @variant_id, @item_name, @variant_name,
        @quantity, @unit_price, @gross_total_money, @total_discount, @total_money, @tax_rate,
        @tax_amount, @payload_json
    );";

        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new
        {
            receipt_number = row.ReceiptNumber,
            line_id = row.LineId,
            sku = row.Sku,
            item_id = row.ItemId,
            variant_id = row.VariantId,
            item_name = row.ItemName,
            variant_name = row.VariantName,
            quantity = row.Quantity,
            unit_price = row.UnitPrice,
            gross_total_money = row.GrossTotalMoney,
            total_discount = row.TotalDiscount,
            total_money = row.TotalMoney,
            tax_rate = row.TaxRate,
            tax_amount = row.TaxAmount,
            payload_json = row.PayloadJson
        });
    }
}

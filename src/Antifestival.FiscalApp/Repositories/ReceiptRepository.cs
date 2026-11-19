using Antifestival.FiscalApp.Data;
using Antifestival.FiscalApp.Models;
using Dapper;

namespace Antifestival.FiscalApp.Repositories;

public class ReceiptRepository
{
    private readonly DbConnectionFactory _factory;

    public ReceiptRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task UpsertAsync(LoyverseReceiptDto receipt)
    {
        const string sql = @"
MERGE loyverse.receipts AS target
USING (SELECT @receipt_number AS receipt_number) AS source
ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET
        receipt_type        = @receipt_type,
        refund_for          = @refund_for,
        receipt_date        = @receipt_date,
        created_at          = @created_at,
        updated_at          = @updated_at,
        cancelled_at        = @cancelled_at,
        store_id            = @store_id,
        pos_device_id       = @pos_device_id,
        employee_id         = @employee_id,
        customer_id         = @customer_id,
        total_money         = @total_money,
        total_tax           = @total_tax,
        total_discount      = @total_discount,
        payment_cash        = @payment_cash,
        payment_electronic  = @payment_electronic,
        payment_type_names  = @payment_type_names,
        payload_json        = @payload_json,
        last_sync_at        = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (
        receipt_number, receipt_type, refund_for, receipt_date,
        created_at, updated_at, cancelled_at, store_id, pos_device_id,
        employee_id, customer_id, total_money, total_tax, total_discount,
        payment_cash, payment_electronic, payment_type_names,
        payload_json, fiscal_status, last_sync_at
    )
    VALUES (
        @receipt_number, @receipt_type, @refund_for, @receipt_date,
        @created_at, @updated_at, @cancelled_at, @store_id, @pos_device_id,
        @employee_id, @customer_id, @total_money, @total_tax, @total_discount,
        @payment_cash, @payment_electronic, @payment_type_names,
        @payload_json, 'DA_INVIARE', SYSDATETIMEOFFSET()
    );";

        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new
        {
            receipt_number     = receipt.ReceiptNumber,
            receipt_type       = receipt.ReceiptType,
            refund_for         = receipt.RefundFor,
            receipt_date       = receipt.ReceiptDate,
            created_at         = receipt.CreatedAt,
            updated_at         = receipt.UpdatedAt,
            cancelled_at       = receipt.CancelledAt,
            store_id           = receipt.StoreId,
            pos_device_id      = receipt.PosDeviceId,
            employee_id        = receipt.EmployeeId,
            customer_id        = receipt.CustomerId,
            total_money        = receipt.TotalMoney,
            total_tax          = receipt.TotalTax,
            total_discount     = receipt.TotalDiscount,
            payment_cash       = receipt.PaymentCash,
            payment_electronic = receipt.PaymentElectronic,
            payment_type_names = receipt.PaymentTypeNames,
            payload_json       = receipt.PayloadJson
        });
    }

    /// <summary>
    /// Updates fiscal_status only if the receipt is not already INVIATO.
    /// </summary>
    public async Task SetFiscalStatusAsync(string receiptNumber, string status)
    {
        const string sql = @"
UPDATE loyverse.receipts
SET fiscal_status = @status
WHERE receipt_number = @receiptNumber
  AND fiscal_status NOT IN ('INVIATO')";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { receiptNumber, status });
    }

    /// <summary>
    /// Returns the global 1-based position of this cash-only receipt among all
    /// cash-only receipts ever recorded (ordered by receipt_date).
    /// Used with (position - 1) % CashInterval == 0 to send the 1st of every N,
    /// giving the pattern: send, skip, skip, ..., send, skip, skip, ...
    /// </summary>
    public async Task<int> GetCashGlobalPositionAsync(string receiptNumber)
    {
        const string sql = @"
SELECT COUNT(*)
FROM loyverse.receipts r
WHERE r.payment_cash      > 0
  AND r.payment_electronic = 0
  AND (r.receipt_type IS NULL OR UPPER(r.receipt_type) <> 'REFUND')
  AND r.cancelled_at       IS NULL
  AND r.receipt_date <= (
      SELECT r3.receipt_date FROM loyverse.receipts r3
      WHERE r3.receipt_number = @receiptNumber
  )";
        using var connection = _factory.CreateConnection();
        return await connection.QuerySingleAsync<int>(sql, new { receiptNumber });
    }

    public async Task<IEnumerable<ReceiptSummaryDto>> GetReceiptsForDayAsync(string date)
    {
        const string sql = @"
SELECT
    r.receipt_number,
    r.receipt_type,
    r.receipt_date,
    r.total_money,
    r.payment_cash,
    r.payment_electronic,
    r.payment_type_names,
    r.fiscal_status,
    q.status              AS queue_status,
    q.sent_at,
    q.fatturapro_url,
    q.last_error,
    q.fatturapro_return_code
FROM loyverse.receipts r
LEFT JOIN loyverse.fiscal_queue q ON r.receipt_number = q.receipt_number
WHERE CAST(r.receipt_date AT TIME ZONE 'W. Europe Standard Time' AS DATE) = CAST(@date AS DATE)
ORDER BY r.receipt_date DESC";
        using var connection = _factory.CreateConnection();
        return await connection.QueryAsync<ReceiptSummaryDto>(sql, new { date });
    }

    public async Task<DayStatsDto> GetStatsForDayAsync(string date)
    {
        const string sql = @"
SELECT
    COUNT(*) AS total,
    ISNULL(SUM(CASE WHEN payment_electronic > 0 THEN 1 ELSE 0 END), 0) AS electronic,
    ISNULL(SUM(CASE WHEN payment_cash > 0 AND payment_electronic = 0 THEN 1 ELSE 0 END), 0) AS cash_only,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'INVIATO'          THEN 1 ELSE 0 END), 0) AS inviato,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'ERRORE'           THEN 1 ELSE 0 END), 0) AS errore,
    ISNULL(SUM(CASE WHEN q.status        = 'DA_INVIARE'       THEN 1 ELSE 0 END), 0) AS da_inviare,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'NON_PROGRAMMATO'  THEN 1 ELSE 0 END), 0) AS non_programmato,
    ISNULL(SUM(r.total_money), 0) AS total_amount,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'INVIATO' THEN r.total_money ELSE 0 END), 0) AS total_sent_gross,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'INVIATO' THEN ISNULL(r.total_tax, 0) ELSE 0 END), 0) AS total_sent_tax,
    ISNULL(SUM(CASE WHEN r.fiscal_status <> 'INVIATO' THEN r.total_money ELSE 0 END), 0) AS total_not_sent_gross,
    ISNULL(SUM(r.payment_electronic), 0) AS total_samup
FROM loyverse.receipts r
LEFT JOIN loyverse.fiscal_queue q ON r.receipt_number = q.receipt_number
WHERE CAST(r.receipt_date AT TIME ZONE 'W. Europe Standard Time' AS DATE) = CAST(@date AS DATE)
  AND (r.receipt_type IS NULL OR UPPER(r.receipt_type) <> 'REFUND')
  AND r.cancelled_at IS NULL";
        using var connection = _factory.CreateConnection();
        return await connection.QuerySingleAsync<DayStatsDto>(sql, new { date });
    }

    public async Task<IEnumerable<ReceiptSummaryDto>> GetReceiptsForRangeAsync(DateTime start, DateTime end)
    {
        const string sql = @"
SELECT
    r.receipt_number,
    r.receipt_type,
    r.receipt_date,
    r.total_money,
    r.payment_cash,
    r.payment_electronic,
    r.payment_type_names,
    r.fiscal_status,
    q.status              AS queue_status,
    q.sent_at,
    q.fatturapro_url,
    q.last_error,
    q.fatturapro_return_code
FROM loyverse.receipts r
LEFT JOIN loyverse.fiscal_queue q ON r.receipt_number = q.receipt_number
WHERE r.receipt_date >= @start
  AND r.receipt_date <= @end
  AND (r.receipt_type IS NULL OR UPPER(r.receipt_type) <> 'REFUND')
  AND r.cancelled_at IS NULL
ORDER BY r.receipt_date DESC";
        using var connection = _factory.CreateConnection();
        return await connection.QueryAsync<ReceiptSummaryDto>(sql, new { start, end });
    }

    public async Task<DayStatsDto> GetStatsForRangeAsync(DateTime start, DateTime end)
    {
        const string sql = @"
SELECT
    COUNT(*) AS total,
    ISNULL(SUM(CASE WHEN payment_electronic > 0 THEN 1 ELSE 0 END), 0) AS electronic,
    ISNULL(SUM(CASE WHEN payment_cash > 0 AND payment_electronic = 0 THEN 1 ELSE 0 END), 0) AS cash_only,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'INVIATO'          THEN 1 ELSE 0 END), 0) AS inviato,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'ERRORE'           THEN 1 ELSE 0 END), 0) AS errore,
    ISNULL(SUM(CASE WHEN q.status        = 'DA_INVIARE'       THEN 1 ELSE 0 END), 0) AS da_inviare,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'NON_PROGRAMMATO'  THEN 1 ELSE 0 END), 0) AS non_programmato,
    ISNULL(SUM(r.total_money), 0) AS total_amount,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'INVIATO' THEN r.total_money ELSE 0 END), 0) AS total_sent_gross,
    ISNULL(SUM(CASE WHEN r.fiscal_status = 'INVIATO' THEN ISNULL(r.total_tax, 0) ELSE 0 END), 0) AS total_sent_tax,
    ISNULL(SUM(CASE WHEN r.fiscal_status <> 'INVIATO' THEN r.total_money ELSE 0 END), 0) AS total_not_sent_gross,
    ISNULL(SUM(r.payment_electronic), 0) AS total_samup
FROM loyverse.receipts r
LEFT JOIN loyverse.fiscal_queue q ON r.receipt_number = q.receipt_number
WHERE r.receipt_date >= @start
  AND r.receipt_date <= @end
  AND (r.receipt_type IS NULL OR UPPER(r.receipt_type) <> 'REFUND')
  AND r.cancelled_at IS NULL";
        using var connection = _factory.CreateConnection();
        return await connection.QuerySingleAsync<DayStatsDto>(sql, new { start, end });
    }
}

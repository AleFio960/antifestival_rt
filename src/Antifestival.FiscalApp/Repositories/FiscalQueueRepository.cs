using Antifestival.FiscalApp.Data;
using Dapper;

namespace Antifestival.FiscalApp.Repositories;

public class FiscalQueueRepository
{
    private readonly DbConnectionFactory _factory;

    public FiscalQueueRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Returns true if the receipt was newly inserted into the queue.</summary>
    public async Task<bool> EnqueueAsync(string receiptNumber)
    {
        const string sql = @"
DECLARE @inserted INT = 0;
IF NOT EXISTS (SELECT 1 FROM loyverse.fiscal_queue WHERE receipt_number = @receiptNumber)
   AND NOT EXISTS (SELECT 1 FROM loyverse.receipts WHERE receipt_number = @receiptNumber AND fiscal_status = 'INVIATO')
BEGIN
    INSERT INTO loyverse.fiscal_queue(receipt_number, status, attempts)
    VALUES (@receiptNumber, 'DA_INVIARE', 0);
    SET @inserted = 1;
END
SELECT @inserted;";
        using var connection = _factory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(sql, new { receiptNumber }) == 1;
    }

    public async Task<dynamic?> GetNextAsync()
    {
        const string sql = @"
SELECT TOP 1 *
FROM loyverse.fiscal_queue
WHERE status IN ('DA_INVIARE', 'ERRORE')
  AND attempts < 5
ORDER BY inserted_at ASC";
        using var connection = _factory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync(sql);
    }

    public async Task MarkSentAsync(string receiptNumber, int? returnCode, string? description, string? url)
    {
        const string sql = @"
UPDATE loyverse.fiscal_queue
SET status = 'INVIATO',
    fatturapro_return_code = @returnCode,
    fatturapro_description = @description,
    fatturapro_url = @url,
    sent_at = SYSDATETIMEOFFSET(),
    updated_at = SYSDATETIMEOFFSET()
WHERE receipt_number = @receiptNumber";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { receiptNumber, returnCode, description, url });
    }

    public async Task MarkErrorAsync(string receiptNumber, string lastError, int? returnCode, string? description)
    {
        const string sql = @"
UPDATE loyverse.fiscal_queue
SET status = 'ERRORE',
    attempts = attempts + 1,
    last_error = @lastError,
    fatturapro_return_code = @returnCode,
    fatturapro_description = @description,
    updated_at = SYSDATETIMEOFFSET()
WHERE receipt_number = @receiptNumber";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { receiptNumber, lastError, returnCode, description });
    }

    public async Task<IEnumerable<dynamic>> GetPendingForDayAsync(string date)
    {
        const string sql = @"
SELECT q.*
FROM loyverse.fiscal_queue q
JOIN loyverse.receipts r ON r.receipt_number = q.receipt_number
WHERE q.status IN ('DA_INVIARE', 'ERRORE')
  AND q.attempts < 5
  AND CAST(r.receipt_date AS DATE) = CAST(@date AS DATE)
ORDER BY q.inserted_at ASC";
        using var connection = _factory.CreateConnection();
        return await connection.QueryAsync(sql, new { date });
    }

    public async Task RequeueAsync(string receiptNumber)
    {
        const string sql = @"
MERGE loyverse.fiscal_queue AS target
USING (SELECT @receiptNumber AS receipt_number) AS source
ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET
        status     = 'DA_INVIARE',
        attempts   = 0,
        last_error = NULL,
        updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (receipt_number, status, attempts)
    VALUES (@receiptNumber, 'DA_INVIARE', 0);";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { receiptNumber });
    }

    /// <summary>Re-accoda tutti gli scontrini già INVIATO del giorno (usato per reinvio dopo run demo).
    /// Resetta fiscal_status → DA_INVIARE e rimette il record in coda con attempts = 0.</summary>
    public async Task RequeueAllSentForDayAsync(string date)
    {
        const string sql = @"
-- 1) Rimetti in coda settando demo_requeue_at
MERGE loyverse.fiscal_queue AS target
USING (
    SELECT receipt_number FROM loyverse.receipts
    WHERE CAST(receipt_date AS DATE) = CAST(@date AS DATE)
      AND fiscal_status = 'INVIATO'
      AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
      AND cancelled_at IS NULL
) AS source ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET status = 'DA_INVIARE', attempts = 0, last_error = NULL,
               demo_requeue_at = SYSDATETIMEOFFSET(), updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (receipt_number, status, attempts, demo_requeue_at)
    VALUES (source.receipt_number, 'DA_INVIARE', 0, SYSDATETIMEOFFSET());

-- 2) Resetta fiscal_status sulla tabella receipts
UPDATE loyverse.receipts
SET fiscal_status = 'DA_INVIARE'
WHERE CAST(receipt_date AS DATE) = CAST(@date AS DATE)
  AND fiscal_status = 'INVIATO'
  AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
  AND cancelled_at IS NULL;";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { date });
    }

    /// <summary>Re-accoda tutti gli scontrini INVIATO (demo) in un range di date.</summary>
    public async Task RequeueAllSentForRangeAsync(string startDate, string endDate)
    {
        const string sql = @"
MERGE loyverse.fiscal_queue AS target
USING (
    SELECT receipt_number FROM loyverse.receipts
    WHERE CAST(receipt_date AS DATE) BETWEEN CAST(@startDate AS DATE) AND CAST(@endDate AS DATE)
      AND fiscal_status = 'INVIATO'
      AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
      AND cancelled_at IS NULL
) AS source ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET status = 'DA_INVIARE', attempts = 0, last_error = NULL,
               demo_requeue_at = SYSDATETIMEOFFSET(), updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (receipt_number, status, attempts, demo_requeue_at)
    VALUES (source.receipt_number, 'DA_INVIARE', 0, SYSDATETIMEOFFSET());

UPDATE loyverse.receipts
SET fiscal_status = 'DA_INVIARE'
WHERE CAST(receipt_date AS DATE) BETWEEN CAST(@startDate AS DATE) AND CAST(@endDate AS DATE)
  AND fiscal_status = 'INVIATO'
  AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
  AND cancelled_at IS NULL;";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { startDate, endDate });
    }

    /// <summary>Statistiche reinvio demo per range: da reinviare / in elaborazione / reinviati / errori.</summary>
    public async Task<dynamic?> GetDemoRangeStatsAsync(string startDate, string endDate)
    {
        const string sql = @"
SELECT
    COUNT(r.receipt_number)                                                                          AS total,
    SUM(CASE WHEN q.demo_requeue_at IS NULL     AND r.fiscal_status = 'INVIATO'  THEN 1 ELSE 0 END) AS daReinviare,
    SUM(CASE WHEN q.demo_requeue_at IS NOT NULL AND q.status = 'DA_INVIARE'      THEN 1 ELSE 0 END) AS inElaborazione,
    SUM(CASE WHEN q.demo_requeue_at IS NOT NULL AND q.status = 'INVIATO'         THEN 1 ELSE 0 END) AS reinviati,
    SUM(CASE WHEN q.demo_requeue_at IS NOT NULL AND q.status = 'ERRORE'          THEN 1 ELSE 0 END) AS errori
FROM loyverse.receipts r
LEFT JOIN loyverse.fiscal_queue q ON q.receipt_number = r.receipt_number
WHERE CAST(r.receipt_date AS DATE) BETWEEN CAST(@startDate AS DATE) AND CAST(@endDate AS DATE)
  AND (r.receipt_type IS NULL OR UPPER(r.receipt_type) <> 'REFUND')
  AND r.cancelled_at IS NULL";
        using var connection = _factory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync(sql, new { startDate, endDate });
    }

    /// <summary>Forza tutti gli scontrini non INVIATO del giorno in coda (sovrascrive NON_PROGRAMMATO).</summary>
    public async Task ForceEnqueueAllForDayAsync(string date)
    {
        const string sql = @"
MERGE loyverse.fiscal_queue AS target
USING (
    SELECT receipt_number FROM loyverse.receipts
    WHERE CAST(receipt_date AS DATE) = CAST(@date AS DATE)
      AND fiscal_status <> 'INVIATO'
      AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
      AND cancelled_at IS NULL
) AS source ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET status = 'DA_INVIARE', attempts = 0, last_error = NULL, updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (receipt_number, status, attempts) VALUES (source.receipt_number, 'DA_INVIARE', 0);";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { date });
    }

    /// <summary>
    /// Come ForceEnqueueAllForDayAsync, ma per i scontrini SOLO contanti
    /// mette in coda soltanto 1 ogni <paramref name="cashInterval"/>.
    /// Gli scontrini elettronici/misti vengono sempre accodati.
    /// </summary>
    public async Task ForceEnqueueAllForDayWithCashIntervalAsync(string date, int cashInterval)
    {
        // cashInterval guaranteed 1-20 by RuntimeSettingsService, but clamp defensively
        cashInterval = Math.Clamp(cashInterval, 1, 20);

        const string sql = @"
-- 1) Tutti gli scontrini NON solo-contanti → sempre in coda
MERGE loyverse.fiscal_queue AS target
USING (
    SELECT receipt_number FROM loyverse.receipts
    WHERE CAST(receipt_date AS DATE) = CAST(@date AS DATE)
      AND fiscal_status <> 'INVIATO'
      AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
      AND cancelled_at IS NULL
      AND NOT (ISNULL(payment_electronic, 0) = 0 AND ISNULL(payment_cash, 0) > 0)
) AS source ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET status = 'DA_INVIARE', attempts = 0, last_error = NULL, updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (receipt_number, status, attempts) VALUES (source.receipt_number, 'DA_INVIARE', 0);

-- 2) Scontrini solo-contanti: 1 ogni @cashInterval (row 1, 1+N, 1+2N …)
;WITH CashReceipts AS (
    SELECT receipt_number,
           ROW_NUMBER() OVER (ORDER BY receipt_date, receipt_number) AS rn
    FROM loyverse.receipts
    WHERE CAST(receipt_date AS DATE) = CAST(@date AS DATE)
      AND fiscal_status <> 'INVIATO'
      AND (receipt_type IS NULL OR UPPER(receipt_type) <> 'REFUND')
      AND cancelled_at IS NULL
      AND ISNULL(payment_electronic, 0) = 0
      AND ISNULL(payment_cash, 0) > 0
)
MERGE loyverse.fiscal_queue AS target
USING (
    SELECT receipt_number FROM CashReceipts
    WHERE (rn - 1) % @cashInterval = 0
) AS source ON target.receipt_number = source.receipt_number
WHEN MATCHED THEN
    UPDATE SET status = 'DA_INVIARE', attempts = 0, last_error = NULL, updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (receipt_number, status, attempts) VALUES (source.receipt_number, 'DA_INVIARE', 0);";

        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { date, cashInterval });
    }
}

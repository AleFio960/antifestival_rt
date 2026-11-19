using Antifestival.FiscalApp.Data;
using Antifestival.FiscalApp.Integrations;
using Antifestival.FiscalApp.Repositories;
using Dapper;
using System.Collections.Concurrent;
using System.Globalization;

namespace Antifestival.FiscalApp.Services;

public class FiscalSendService
{
    private readonly DbConnectionFactory _factory;
    private readonly FiscalQueueRepository _queueRepository;
    private readonly FiscalLogRepository _logRepository;
    private readonly SyncLogRepository _syncLogRepository;
    private readonly FatturaProClient _fatturaProClient;
    private readonly IConfiguration _configuration;
    private readonly RuntimeSettingsService _runtimeSettings;

    public FiscalSendService(
        DbConnectionFactory factory,
        FiscalQueueRepository queueRepository,
        FiscalLogRepository logRepository,
        SyncLogRepository syncLogRepository,
        FatturaProClient fatturaProClient,
        IConfiguration configuration,
        RuntimeSettingsService runtimeSettings)
    {
        _factory = factory;
        _queueRepository = queueRepository;
        _logRepository = logRepository;
        _syncLogRepository = syncLogRepository;
        _fatturaProClient = fatturaProClient;
        _configuration = configuration;
        _runtimeSettings = runtimeSettings;
    }

    /// <summary>Processes the next DA_INVIARE item in the global queue.</summary>
    public async Task<object?> ProcessNextAsync()
    {
        var queueItem = await _queueRepository.GetNextAsync();
        if (queueItem == null) return null;
        return await ProcessReceiptAsync((string)queueItem.receipt_number);
    }

    /// <summary>Processes all pending queue items for a given day (yyyy-MM-dd).</summary>
    public async Task<List<object>> ProcessAllForDayAsync(string date)
    {
        var pending = (await _queueRepository.GetPendingForDayAsync(date)).ToList();
        var results = new ConcurrentBag<object>();
        var semaphore = new SemaphoreSlim(5);

        var tasks = pending.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await ProcessReceiptAsync((string)item.receipt_number);
                if (result != null) results.Add(result);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        await _syncLogRepository.InsertAsync("FISCAL_SEND", "OK",
            $"Elaborati {results.Count} corrispettivi per il giorno {date}",
            new { date, processed = results.Count, queued = pending.Count });

        return results.ToList();
    }

    /// <summary>Re-invia tutti i corrispettivi già INVIATO (in demo) per un giorno dato.
    /// Resetta il loro stato e li riprocessa senza il flag ade_test.</summary>
    public async Task<List<object>> ResendDemoForDayAsync(string date)
    {
        await _queueRepository.RequeueAllSentForDayAsync(date);
        var results = await ProcessAllForDayAsync(date);
        await _syncLogRepository.InsertAsync("FISCAL_SEND", "OK",
            $"Re-invio post-demo: elaborati {results.Count} corrispettivi per il giorno {date}",
            new { date, processed = results.Count });
        return results;
    }

    /// <summary>Re-invia tutti i corrispettivi INVIATO (demo) per un range di date, giorno per giorno.</summary>
    public async Task ResendDemoForRangeAsync(string startDate, string endDate)
    {
        await _queueRepository.RequeueAllSentForRangeAsync(startDate, endDate);

        var start = DateTime.Parse(startDate);
        var end   = DateTime.Parse(endDate);
        var totalProcessed = 0;

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var dayStr = day.ToString("yyyy-MM-dd");
            var dayResults = await ProcessAllForDayAsync(dayStr);
            totalProcessed += dayResults.Count;
        }

        await _syncLogRepository.InsertAsync("FISCAL_SEND", "OK",
            $"Re-invio post-demo range {startDate}→{endDate}: elaborati {totalProcessed} corrispettivi",
            new { startDate, endDate, processed = totalProcessed });
    }

    /// <summary>Forces a specific receipt into the queue and immediately processes it.</summary>
    public async Task<object?> SendSpecificAsync(string receiptNumber)
    {
        await _queueRepository.RequeueAsync(receiptNumber);
        return await ProcessReceiptAsync(receiptNumber);
    }

    /// <summary>Forza TUTTI gli scontrini del giorno (inclusi NON_PROGRAMMATO) in coda e li invia.
    /// I scontrini solo-contanti vengono accodati 1 ogni <see cref="RuntimeSettingsService.CashInterval"/>.</summary>
    public async Task<List<object>> ForceAllForDayAsync(string date)
    {
        // Force-send bypassa l'intervallo contanti: invia TUTTO il non inviato
        await _queueRepository.ForceEnqueueAllForDayAsync(date);
        return await ProcessAllForDayAsync(date);
    }

    // ── Core processing ─────────────────────────────────────────────────────

    public async Task<object?> ProcessReceiptAsync(string receiptNumber)
    {
        using var connection = _factory.CreateConnection();

        var receipt = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM loyverse.receipts WHERE receipt_number = @receiptNumber",
            new { receiptNumber });

        if (receipt == null)
        {
            await _queueRepository.MarkErrorAsync(receiptNumber, "Receipt non trovata in DB", null, null);
            return null;
        }

        var rows = (await connection.QueryAsync(
            "SELECT * FROM loyverse.receipt_rows WHERE receipt_number = @receiptNumber ORDER BY id",
            new { receiptNumber })).ToList();

        var payload = BuildPayload(receipt, rows);
        var result = await _fatturaProClient.SaveDocumentAsync(payload);

        int? returnCode = null;
        if (int.TryParse(result.ReturnCode, out int parsedCode))
            returnCode = parsedCode;

        await _logRepository.InsertAsync(receiptNumber, payload, result.RawXml, returnCode);

        if (result.ReturnCode == "0")
        {
            await _queueRepository.MarkSentAsync(receiptNumber, returnCode, result.Description, result.Link);
            await connection.ExecuteAsync(
                "UPDATE loyverse.receipts SET fiscal_status = 'INVIATO' WHERE receipt_number = @receiptNumber",
                new { receiptNumber });
            await _syncLogRepository.InsertAsync("FISCAL_SEND", "OK",
                $"Receipt {receiptNumber} inviata",
                new { receiptNumber, result.ReturnCode, result.Description, result.Link });
        }
        else
        {
            await _queueRepository.MarkErrorAsync(receiptNumber,
                result.Description ?? "Errore FatturaPRO", returnCode, result.Description);
            await connection.ExecuteAsync(
                "UPDATE loyverse.receipts SET fiscal_status = 'ERRORE' WHERE receipt_number = @receiptNumber",
                new { receiptNumber });
            await _syncLogRepository.InsertAsync("FISCAL_SEND", "ERROR",
                $"Errore invio receipt {receiptNumber}: {result.Description}",
                new { receiptNumber, result.ReturnCode, result.Description });
        }

        return new
        {
            receiptNumber,
            returnCode = result.ReturnCode,
            description = result.Description,
            link = result.Link,
            token = result.Token
        };
    }

    private Dictionary<string, string> BuildPayload(dynamic receipt, List<dynamic> rows)
    {
        var payload = new Dictionary<string, string>
        {
            ["apiKey"]                            = _configuration["FatturaPro:ApiKey"] ?? "",
            ["action"]                            = "SaveDocument",
            ["target"]                            = "C",
            ["tipo"]                              = "receipt",
            ["tipo_emissione"]                    = "DCO",
            ["ade_password"]                      = _configuration["FatturaPro:AdePassword"] ?? "",
            ["customer_email"]                    = "noreply@antifestival.org",
            ["dati_fattura[order_num]"]           = (string)receipt.receipt_number,
            ["dati_fattura[order_date]"]          = ((DateTimeOffset?)receipt.receipt_date)
                                                        ?.ToString("yyyy-MM-dd")
                                                        ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["dati_fattura[sezionale]"]           = "API",
            ["dati_fattura[paid]"]                = "true",
            ["dati_fattura[add_customer]"]        = "false",
            ["dati_fattura[billing_first_name]"]  = "Cliente",
            ["dati_fattura[billing_last_name]"]   = "Festival",
            ["dati_fattura[billing_address]"]     = "N/D",
            ["dati_fattura[billing_postcode]"]    = "00000",
            ["dati_fattura[billing_city]"]        = "N/D",
            ["dati_fattura[billing_state]"]       = "EE",
            ["dati_fattura[billing_country]"]     = "IT",
            ["dati_fattura[billing_email]"]       = "noreply@antifestival.org"
        };

        var adeTestMode = _configuration["FatturaPro:AdeTestMode"];
        if (!string.IsNullOrWhiteSpace(adeTestMode))
            payload["ade_test"] = adeTestMode;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var qty     = Convert.ToDecimal(row.quantity    ?? 0);
            var total   = Convert.ToDecimal(row.total_money ?? 0);
            var taxRate = Convert.ToDecimal(row.tax_rate    ?? 22m);

            // Loyverse stores IVA-inclusive prices; back-calculate net and tax
            decimal imponibile, taxAmount;
            if (taxRate > 0)
            {
                imponibile = Math.Round(total / (1m + taxRate / 100m), 2, MidpointRounding.AwayFromZero);
                taxAmount  = Math.Round(total - imponibile, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                imponibile = total;
                taxAmount  = 0m;
            }

            payload[$"dati_fattura[document_rows][{i}][row_type]"]          = "line_item";
            payload[$"dati_fattura[document_rows][{i}][sku]"]               = (string?)row.sku ?? $"ROW-{i + 1}";
            payload[$"dati_fattura[document_rows][{i}][product_name]"]      = (string?)row.item_name ?? "Articolo";
            payload[$"dati_fattura[document_rows][{i}][item_quantity]"]     = qty.ToString("0.##", CultureInfo.InvariantCulture);
            payload[$"dati_fattura[document_rows][{i}][item_subtotal]"]     = imponibile.ToString("0.00", CultureInfo.InvariantCulture);
            payload[$"dati_fattura[document_rows][{i}][item_subtotal_tax]"] = taxAmount.ToString("0.00", CultureInfo.InvariantCulture);
            payload[$"dati_fattura[document_rows][{i}][item_total]"]        = imponibile.ToString("0.00", CultureInfo.InvariantCulture);
            payload[$"dati_fattura[document_rows][{i}][item_total_tax]"]    = taxAmount.ToString("0.00", CultureInfo.InvariantCulture);
            payload[$"dati_fattura[document_rows][{i}][item_tax_class]"]    = taxRate.ToString("0.##", CultureInfo.InvariantCulture);
            payload[$"dati_fattura[document_rows][{i}][category]"]          = "prodotti";
        }

        return payload;
    }
}

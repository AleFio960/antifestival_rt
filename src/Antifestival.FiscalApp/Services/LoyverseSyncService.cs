using Antifestival.FiscalApp.Integrations;
using Antifestival.FiscalApp.Repositories;
using Antifestival.FiscalApp.Utils;

namespace Antifestival.FiscalApp.Services;

public class LoyverseSyncService
{
    private readonly LoyverseClient _loyverseClient;
    private readonly ReceiptRepository _receiptRepository;
    private readonly ReceiptRowRepository _rowRepository;
    private readonly FiscalQueueRepository _queueRepository;
    private readonly SyncLogRepository _syncLogRepository;
    private readonly FiscalSendService _fiscalSendService;
    private readonly IConfiguration _configuration;
    private readonly RuntimeSettingsService _runtimeSettings;

    public LoyverseSyncService(
        LoyverseClient loyverseClient,
        ReceiptRepository receiptRepository,
        ReceiptRowRepository rowRepository,
        FiscalQueueRepository queueRepository,
        SyncLogRepository syncLogRepository,
        FiscalSendService fiscalSendService,
        IConfiguration configuration,
        RuntimeSettingsService runtimeSettings)
    {
        _loyverseClient = loyverseClient;
        _receiptRepository = receiptRepository;
        _rowRepository = rowRepository;
        _queueRepository = queueRepository;
        _syncLogRepository = syncLogRepository;
        _fiscalSendService = fiscalSendService;
        _configuration = configuration;
        _runtimeSettings = runtimeSettings;
    }

    public async Task<int> SyncAsync()
    {
        var lookback = int.TryParse(_configuration["Sync:LookbackMinutes"], out var minutes)
            ? minutes
            : 1440;

        var from = DateTimeOffset.UtcNow.AddMinutes(-lookback);
        var rawJson = await _loyverseClient.GetReceiptsRawAsync(from);
        // Ordine crescente per data: garantisce che il calcolo della posizione
        // dei contanti sia corretto anche quando Loyverse restituisce i receipt in ordine inverso.
        var receipts = LoyverseMapper.MapReceiptsFromRawJson(rawJson)
                                     .OrderBy(r => r.ReceiptDate)
                                     .ToList();

        foreach (var receipt in receipts)
        {
            await _receiptRepository.UpsertAsync(receipt);
            foreach (var row in receipt.Rows)
                await _rowRepository.UpsertAsync(row);

            if (string.Equals(receipt.ReceiptType, "REFUND", StringComparison.OrdinalIgnoreCase)
                || receipt.CancelledAt != null)
            {
                await _receiptRepository.SetFiscalStatusAsync(receipt.ReceiptNumber, "NON_PROGRAMMATO");
                continue;
            }

            bool isCashOnly = receipt.PaymentCash > 0 && receipt.PaymentElectronic == 0;

            if (!isCashOnly)
            {
                // Electronic or mixed payment: queue AND send immediately
                bool enqueued = await _queueRepository.EnqueueAsync(receipt.ReceiptNumber);
                if (enqueued)
                    await _fiscalSendService.ProcessReceiptAsync(receipt.ReceiptNumber);
            }
            else
            {
                // Cash-only: invia 1 ogni N usando la posizione globale assoluta.
                // Pattern: 1°→invia, 2°→salta, ..., N°→salta, N+1°→invia, ...
                var cashPosition = await _receiptRepository.GetCashGlobalPositionAsync(receipt.ReceiptNumber);
                if ((cashPosition - 1) % _runtimeSettings.CashInterval == 0)
                {
                    bool enqueued = await _queueRepository.EnqueueAsync(receipt.ReceiptNumber);
                    if (enqueued)
                        await _fiscalSendService.ProcessReceiptAsync(receipt.ReceiptNumber);
                }
                else
                    await _receiptRepository.SetFiscalStatusAsync(receipt.ReceiptNumber, "NON_PROGRAMMATO");
            }
        }

        int electronic  = receipts.Count(r => r.PaymentElectronic > 0);
        int cashOnly    = receipts.Count(r => r.PaymentCash > 0 && r.PaymentElectronic == 0);
        int skipped     = receipts.Count(r =>
            string.Equals(r.ReceiptType, "REFUND", StringComparison.OrdinalIgnoreCase)
            || r.CancelledAt != null);

        await _syncLogRepository.InsertAsync("LOYVERSE_SYNC", "OK",
            $"Sincronizzati {receipts.Count} scontrini ({electronic} elettronici, {cashOnly} contanti, {skipped} annullati/rimborsi)",
            new { from, count = receipts.Count, electronic, cashOnly, skipped });

        return receipts.Count;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Background service: sync Loyverse + auto-send elettronici ogni N secondi
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AutoSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration       _configuration;
    private readonly ILogger<AutoSyncBackgroundService> _logger;

    public AutoSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration       configuration,
        ILogger<AutoSyncBackgroundService> logger)
    {
        _scopeFactory  = scopeFactory;
        _configuration = configuration;
        _logger        = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!int.TryParse(_configuration["Sync:AutoSyncIntervalSeconds"], out var intervalSec)
            || intervalSec <= 0)
            intervalSec = 120;

        _logger.LogInformation("AutoSync attivo — ciclo ogni {Sec}s", intervalSec);

        // Attendi avvio completo prima del primo ciclo
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var syncSvc     = scope.ServiceProvider.GetRequiredService<LoyverseSyncService>();
                var fiscalSvc   = scope.ServiceProvider.GetRequiredService<FiscalSendService>();

                var synced = await syncSvc.SyncAsync();
                if (synced > 0)
                    _logger.LogInformation("AutoSync Loyverse: {Count} scontrino/i", synced);

                // Invia automaticamente tutti i DA_INVIARE di oggi a FatturaPro
                var today   = DateTime.Now.ToString("yyyy-MM-dd");
                var results = await fiscalSvc.ProcessAllForDayAsync(today);
                if (results.Count > 0)
                    _logger.LogInformation("AutoSync FatturaPro: {Count} corrispettivo/i inviato/i", results.Count);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Errore nel ciclo AutoSync");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSec), stoppingToken);
        }
    }
}

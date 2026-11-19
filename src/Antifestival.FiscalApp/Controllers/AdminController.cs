using Antifestival.FiscalApp.Repositories;
using Antifestival.FiscalApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Antifestival.FiscalApp.Controllers;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly LoyverseSyncService _syncService;
    private readonly FiscalSendService _fiscalSendService;
    private readonly ReceiptRepository _receiptRepository;
    private readonly SyncLogRepository _syncLogRepository;
    private readonly FiscalLogRepository _fiscalLogRepository;
    private readonly FiscalQueueRepository _fiscalQueueRepository;
    private readonly RuntimeSettingsService _runtimeSettings;
    private readonly SettingsRepository _settingsRepository;

    public AdminController(
        LoyverseSyncService syncService,
        FiscalSendService fiscalSendService,
        ReceiptRepository receiptRepository,
        SyncLogRepository syncLogRepository,
        FiscalLogRepository fiscalLogRepository,
        FiscalQueueRepository fiscalQueueRepository,
        RuntimeSettingsService runtimeSettings,
        SettingsRepository settingsRepository)
    {
        _syncService = syncService;
        _fiscalSendService = fiscalSendService;
        _receiptRepository = receiptRepository;
        _syncLogRepository = syncLogRepository;
        _fiscalLogRepository = fiscalLogRepository;
        _fiscalQueueRepository = fiscalQueueRepository;
        _runtimeSettings = runtimeSettings;
        _settingsRepository = settingsRepository;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { ok = true });

    // ── Loyverse sync ────────────────────────────────────────────────────────

    [HttpPost("sync/loyverse")]
    public IActionResult SyncLoyverse()
    {
        _ = Task.Run(async () =>
        {
            try { await _syncService.SyncAsync(); }
            catch { /* logged internally */ }
        });
        return Accepted(new { message = "Sync avviato in background" });
    }

    // ── Queue / FatturaPro ───────────────────────────────────────────────────

    [HttpPost("fiscal/send-next")]
    public async Task<IActionResult> SendNext()
    {
        try
        {
            var result = await _fiscalSendService.ProcessNextAsync();
            return Ok(result ?? (object)new { message = "Nessun elemento in coda" });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("fiscal/send-day")]
    public async Task<IActionResult> SendDay([FromQuery] string date)
    {
        try
        {
            var results = await _fiscalSendService.ProcessAllForDayAsync(date);
            return Ok(new { processed = results.Count, items = results });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("fiscal/force-send-all")]
    public async Task<IActionResult> ForceSendAll([FromQuery] string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return BadRequest("date required");
        try
        {
            var results = await _fiscalSendService.ForceAllForDayAsync(date);
            return Ok(new { processed = results.Count, items = results });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("fiscal/demo-range-stats")]
    public async Task<IActionResult> GetDemoRangeStats([FromQuery] string start, [FromQuery] string end)
    {
        if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            return BadRequest("start and end required");
        try
        {
            var stats = await _fiscalQueueRepository.GetDemoRangeStatsAsync(start, end);
            return Ok(stats ?? (object)new { total = 0, daReinviare = 0, inElaborazione = 0, reinviati = 0, errori = 0 });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("fiscal/resend-demo-range")]
    public IActionResult ResendDemoRange([FromQuery] string start, [FromQuery] string end)
    {
        if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            return BadRequest("start and end required");
        _ = Task.Run(async () =>
        {
            try { await _fiscalSendService.ResendDemoForRangeAsync(start, end); }
            catch { /* logged internally */ }
        });
        return Accepted(new { message = $"Re-invio post-demo avviato in background per {start} → {end}" });
    }

    [HttpPost("fiscal/resend-demo")]
    public IActionResult ResendDemo([FromQuery] string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return BadRequest("date required");
        _ = Task.Run(async () =>
        {
            try { await _fiscalSendService.ResendDemoForDayAsync(date); }
            catch { /* logged internally */ }
        });
        return Accepted(new { message = $"Re-invio post-demo avviato in background per il {date}" });
    }

    [HttpPost("fiscal/send/{receiptNumber}")]
    public async Task<IActionResult> SendReceipt(string receiptNumber)
    {
        try
        {
            var result = await _fiscalSendService.SendSpecificAsync(receiptNumber);
            return Ok(result ?? (object)new { returnCode = "-1", description = "Nessun risultato" });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    // ── Data queries ─────────────────────────────────────────────────────────

    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts([FromQuery] string? date, [FromQuery] string? start, [FromQuery] string? end)
    {
        if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
        {
            if (!DateTime.TryParse(start, out var startDt) || !DateTime.TryParse(end, out var endDt))
                return BadRequest("Formato date non valido");
            var italianZone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startDt, DateTimeKind.Unspecified), italianZone);
            var endUtc   = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endDt,   DateTimeKind.Unspecified), italianZone);
            var receipts = await _receiptRepository.GetReceiptsForRangeAsync(startUtc, endUtc);
            return Ok(receipts);
        }
        var targetDate = date ?? DateTime.Today.ToString("yyyy-MM-dd");
        var receipts2 = await _receiptRepository.GetReceiptsForDayAsync(targetDate);
        return Ok(receipts2);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] string? date, [FromQuery] string? start, [FromQuery] string? end)
    {
        if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
        {
            if (!DateTime.TryParse(start, out var startDt) || !DateTime.TryParse(end, out var endDt))
                return BadRequest("Formato date non valido");
            var italianZone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startDt, DateTimeKind.Unspecified), italianZone);
            var endUtc   = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endDt,   DateTimeKind.Unspecified), italianZone);
            var stats = await _receiptRepository.GetStatsForRangeAsync(startUtc, endUtc);
            return Ok(stats);
        }
        var targetDate = date ?? DateTime.Today.ToString("yyyy-MM-dd");
        var stats2 = await _receiptRepository.GetStatsForDayAsync(targetDate);
        return Ok(stats2);
    }

    // ── Log queries ───────────────────────────────────────────────────────────

    [HttpGet("logs/sync")]
    public async Task<IActionResult> GetSyncLogs([FromQuery] int limit = 100)
    {
        var logs = await _syncLogRepository.GetRecentAsync(limit);
        return Ok(logs);
    }

    [HttpGet("logs/fiscal")]
    public async Task<IActionResult> GetFiscalLogs([FromQuery] string? date = null, [FromQuery] int limit = 200)
    {
        var logs = await _fiscalLogRepository.GetRecentAsync(date, limit);
        return Ok(logs);
    }

    // ── Runtime settings ──────────────────────────────────────────────────────

    [HttpGet("settings/cash-interval")]
    public IActionResult GetCashInterval()
        => Ok(new { interval = _runtimeSettings.CashInterval });

    [HttpPost("settings/cash-interval")]
    public async Task<IActionResult> SetCashInterval([FromBody] CashIntervalRequest req)
    {
        if (req.Interval < 1 || req.Interval > 20)
            return BadRequest(new { error = "Il valore deve essere compreso tra 1 e 20." });

        _runtimeSettings.CashInterval = req.Interval;
        await _settingsRepository.SetAsync("cash_interval", req.Interval.ToString());
        return Ok(new { interval = _runtimeSettings.CashInterval });
    }
}

public record CashIntervalRequest(int Interval);

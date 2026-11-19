namespace Antifestival.FiscalApp.Services;

/// <summary>
/// Singleton that holds runtime-configurable settings.
/// Initialised from appsettings.json; changes survive until the process restarts.
/// </summary>
public class RuntimeSettingsService
{
    private volatile int _cashInterval;

    public RuntimeSettingsService(IConfiguration configuration)
    {
        _cashInterval = Math.Clamp(
            configuration.GetValue<int>("FiscalSettings:CashInterval", 1),
            1, 20);
    }

    /// <summary>
    /// How many cash-only receipts to skip between each one that is actually sent to AdE.
    /// 1 = send all, 2 = send every 2nd, 3 = send every 3rd, etc.
    /// </summary>
    public int CashInterval
    {
        get => _cashInterval;
        set => _cashInterval = Math.Clamp(value, 1, 20);
    }
}

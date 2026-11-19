using Antifestival.FiscalApp.Data;
using Antifestival.FiscalApp.Integrations;
using Antifestival.FiscalApp.Repositories;
using Antifestival.FiscalApp.Services;

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddHttpClient<LoyverseClient>();
builder.Services.AddHttpClient<FatturaProClient>();

builder.Services.AddScoped<ReceiptRepository>();
builder.Services.AddScoped<ReceiptRowRepository>();
builder.Services.AddScoped<FiscalQueueRepository>();
builder.Services.AddScoped<FiscalLogRepository>();
builder.Services.AddScoped<SyncLogRepository>();
builder.Services.AddScoped<SettingsRepository>();
builder.Services.AddSingleton<RuntimeSettingsService>();
builder.Services.AddScoped<LoyverseSyncService>();
builder.Services.AddScoped<FiscalSendService>();
builder.Services.AddHostedService<AutoSyncBackgroundService>();

var app = builder.Build();

Console.WriteLine($"[INFO] Ambiente: {app.Environment.EnvironmentName} | FatturaPro: {app.Configuration["FatturaPro:BaseUrl"]}");

// Ensure app_settings table exists and restore persisted values
try
{
    using var scope = app.Services.CreateScope();
    var settingsRepo    = scope.ServiceProvider.GetRequiredService<SettingsRepository>();
    await settingsRepo.EnsureTableCreatedAsync();

    var runtimeSettings = app.Services.GetRequiredService<RuntimeSettingsService>();
    runtimeSettings.CashInterval = await settingsRepo.GetIntAsync("cash_interval", runtimeSettings.CashInterval);
}
catch (Exception ex)
{
    // Non bloccante: l'app parte con i default; si risolve configurando la connection string.
    Console.WriteLine($"[WARN] Inizializzazione settings DB fallita: {ex.Message}");
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.Run();

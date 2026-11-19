using Antifestival.FiscalApp.Data;
using Dapper;

namespace Antifestival.FiscalApp.Repositories;

public class SettingsRepository
{
    private readonly DbConnectionFactory _factory;

    public SettingsRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Creates loyverse.app_settings if it does not already exist.</summary>
    public async Task EnsureTableCreatedAsync()
    {
        const string sql = @"
IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'loyverse' AND t.name = 'app_settings'
)
BEGIN
    CREATE TABLE loyverse.app_settings (
        [key]      NVARCHAR(100)  NOT NULL,
        [value]    NVARCHAR(500)  NOT NULL,
        updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_app_settings PRIMARY KEY ([key])
    )
END";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql);
    }

    public async Task<int> GetIntAsync(string key, int defaultValue)
    {
        const string sql = "SELECT [value] FROM loyverse.app_settings WHERE [key] = @key";
        using var connection = _factory.CreateConnection();
        var raw = await connection.QueryFirstOrDefaultAsync<string>(sql, new { key });
        return raw != null && int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    public async Task SetAsync(string key, string value)
    {
        const string sql = @"
MERGE loyverse.app_settings AS target
USING (SELECT @key AS [key]) AS source ON target.[key] = source.[key]
WHEN MATCHED THEN
    UPDATE SET [value] = @value, updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT ([key], [value]) VALUES (@key, @value);";
        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new { key, value });
    }
}

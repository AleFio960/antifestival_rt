using Antifestival.FiscalApp.Data;
using Antifestival.FiscalApp.Models;
using Dapper;
using System.Text.Json;

namespace Antifestival.FiscalApp.Repositories;

public class SyncLogRepository
{
    private readonly DbConnectionFactory _factory;

    public SyncLogRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InsertAsync(string service, string status, string message, object? details = null)
    {
        const string sql = @"
INSERT INTO loyverse.sync_logs(service, status, message, details_json)
VALUES (@service, @status, @message, @detailsJson)";

        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new
        {
            service,
            status,
            message,
            detailsJson = details != null ? JsonSerializer.Serialize(details) : null
        });
    }

    public async Task<IEnumerable<SyncLogDto>> GetRecentAsync(int limit = 100)
    {
        const string sql = @"
SELECT TOP (@limit) *
FROM loyverse.sync_logs
ORDER BY created_at DESC";
        using var connection = _factory.CreateConnection();
        return await connection.QueryAsync<SyncLogDto>(sql, new { limit });
    }
}

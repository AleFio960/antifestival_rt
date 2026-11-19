using Antifestival.FiscalApp.Data;
using Antifestival.FiscalApp.Models;
using Dapper;
using System.Text.Json;

namespace Antifestival.FiscalApp.Repositories;

public class FiscalLogRepository
{
    private readonly DbConnectionFactory _factory;

    public FiscalLogRepository(DbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task InsertAsync(string receiptNumber, object request, string responseXml, int? returnCode)
    {
        const string sql = @"
INSERT INTO loyverse.fiscal_log(receipt_number, request_json, response_text, return_code)
VALUES (@receiptNumber, @requestJson, @responseText, @returnCode)";

        using var connection = _factory.CreateConnection();
        await connection.ExecuteAsync(sql, new
        {
            receiptNumber,
            requestJson = JsonSerializer.Serialize(request),
            responseText = responseXml,
            returnCode
        });
    }

    public async Task<IEnumerable<FiscalLogDto>> GetRecentAsync(string? date, int limit = 200)
    {
        using var connection = _factory.CreateConnection();

        if (string.IsNullOrWhiteSpace(date))
        {
            return await connection.QueryAsync<FiscalLogDto>(
                "SELECT TOP (@limit) * FROM loyverse.fiscal_log ORDER BY created_at DESC",
                new { limit });
        }

        return await connection.QueryAsync<FiscalLogDto>(
            "SELECT TOP (@limit) * FROM loyverse.fiscal_log WHERE CAST(created_at AS DATE) = CAST(@date AS DATE) ORDER BY created_at DESC",
            new { limit, date });
    }
}

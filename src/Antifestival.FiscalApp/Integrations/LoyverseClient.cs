using System.Net.Http.Headers;
using System.Text.Json;

namespace Antifestival.FiscalApp.Integrations;

public class LoyverseClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public LoyverseClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> GetReceiptsRawAsync(DateTimeOffset updatedAtMin)
    {
        var baseUrl = _configuration["Loyverse:BaseUrl"];
        var token   = _configuration["Loyverse:Token"];
        var allRaw  = new List<string>();
        string? cursor = null;

        do
        {
            var url = new System.Text.StringBuilder()
                .Append(baseUrl).Append("/receipts")
                .Append("?updated_at_min=")
                .Append(Uri.EscapeDataString(updatedAtMin.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")))
                .Append("&limit=250");

            if (cursor != null)
                url.Append("&cursor=").Append(Uri.EscapeDataString(cursor));

            var request = new HttpRequestMessage(HttpMethod.Get, url.ToString());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            cursor = null;
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("receipts", out var receiptsEl))
                foreach (var r in receiptsEl.EnumerateArray())
                    allRaw.Add(r.GetRawText()); // GetRawText copies the string — safe after doc disposal

            if (doc.RootElement.TryGetProperty("cursor", out var cursorEl)
                && cursorEl.ValueKind == JsonValueKind.String)
                cursor = cursorEl.GetString();
        }
        while (cursor != null);

        return $"{{\"receipts\":[{string.Join(",", allRaw)}]}}";
    }
}

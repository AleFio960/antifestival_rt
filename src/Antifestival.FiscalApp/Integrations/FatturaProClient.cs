using Antifestival.FiscalApp.Models;
using System.Xml.Linq;

namespace Antifestival.FiscalApp.Integrations;

public class FatturaProClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public FatturaProClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<FatturaProResultDto> SaveDocumentAsync(Dictionary<string, string> fields)
    {
        var baseUrl = _configuration["FatturaPro:BaseUrl"];
        var content = new FormUrlEncodedContent(fields);

        var response = await _httpClient.PostAsync(baseUrl, content);
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new FatturaProResultDto
            {
                RawXml = raw,
                ReturnCode = ((int)response.StatusCode).ToString(),
                Description = $"HTTP {(int)response.StatusCode}: {raw[..Math.Min(300, raw.Length)]}"
            };

        try
        {
            var doc = XDocument.Parse(raw);
            var result = doc.Descendants("result").FirstOrDefault();
            return new FatturaProResultDto
            {
                RawXml = raw,
                ReturnCode = result?.Element("returnCode")?.Value,
                Description = result?.Element("description")?.Value,
                Link = result?.Element("link")?.Value,
                Token = result?.Element("token")?.Value
            };
        }
        catch (Exception ex)
        {
            return new FatturaProResultDto
            {
                RawXml = raw,
                ReturnCode = "-1",
                Description = $"Risposta non XML: {ex.Message} | {raw[..Math.Min(300, raw.Length)]}"
            };
        }
    }
}

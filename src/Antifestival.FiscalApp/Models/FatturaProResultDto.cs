namespace Antifestival.FiscalApp.Models;

public class FatturaProResultDto
{
    public string RawXml { get; set; } = "";
    public string? ReturnCode { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public string? Token { get; set; }
}

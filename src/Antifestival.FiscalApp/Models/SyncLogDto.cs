namespace Antifestival.FiscalApp.Models;

public class SyncLogDto
{
    public int Id { get; set; }
    public string Service { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

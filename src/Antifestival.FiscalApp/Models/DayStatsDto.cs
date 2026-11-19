namespace Antifestival.FiscalApp.Models;

public class DayStatsDto
{
    public int Total { get; set; }
    public int Electronic { get; set; }
    public int CashOnly { get; set; }
    public int Inviato { get; set; }
    public int Errore { get; set; }
    public int DaInviare { get; set; }
    public int NonProgrammato { get; set; }
    public decimal TotalAmount { get; set; }
    // Totali monetari per i due riquadri fiscali
    public decimal TotalSentGross { get; set; }     // lordo inviato AdE
    public decimal TotalSentTax { get; set; }       // IVA degli inviati
    public decimal TotalNotSentGross { get; set; }  // lordo non ancora inviato (IVA inclusa)
    public decimal TotalSamup { get; set; }         // totale pagamenti Samup
}

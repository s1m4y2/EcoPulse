using System.Globalization;
using System.Net.Http.Json;
using CsvHelper;
using CsvHelper.Configuration;

string apiBase = "http://localhost:5080";
string energyCsv = Path.Combine(AppContext.BaseDirectory, "Data", "nmi_consumption.csv");
string waterCsv = Path.Combine(AppContext.BaseDirectory, "Data", "water_consumption.csv");

// ---- CSV config
var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    HasHeaderRecord = true,
    MissingFieldFound = null,
    BadDataFound = null,
    IgnoreBlankLines = true,
    TrimOptions = TrimOptions.Trim,
    AllowComments = true
};

using var http = new HttpClient();

// --- 1) ENERJİ: nmi_consumption.csv -> /api/readings (EnergyKWh doldurulur)
if (File.Exists(energyCsv))
{
    Console.WriteLine($"[ENERGY] Yükleniyor: {energyCsv}");
    using var sr = new StreamReader(energyCsv);
    using var csv = new CsvReader(sr, csvConfig);

    // beklenen kolonlar: campus_id, meter_id, timestamp, consumption, (opsiyonel demand_* )
    var records = csv.GetRecords<EnergyRow>();

    int ok = 0, fail = 0;
    foreach (var r in records)
    {
        // campus_id boşsa veya null'sa atla
        if (string.IsNullOrWhiteSpace(r.campus_id))
            continue;

        // campus_id parse edilemezse atla
        if (!double.TryParse(r.campus_id, NumberStyles.Any, CultureInfo.InvariantCulture, out var campus))
            continue;

        if (!int.TryParse(r.meter_id, out var meter))
            continue;

        if (!DateTime.TryParse(r.timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts))
            continue;

        if (!double.TryParse(r.consumption, NumberStyles.Any, CultureInfo.InvariantCulture, out var cons))
            continue;

        var buildingId = $"C{NormalizeId(campus)}_M{meter}";

        var payload = new
        {
            BuildingId = buildingId,
            Timestamp = ts,
            EnergyKWh = cons,  // kWh varsayıyoruz
            WaterM3 = 0.0
        };

        var resp = await http.PostAsJsonAsync($"{apiBase}/api/readings", payload);
        if (resp.IsSuccessStatusCode) ok++; else fail++;
    }

    Console.WriteLine($"[ENERGY] Tamamlandı. OK={ok}, FAIL={fail}");
}
else
{
    Console.WriteLine($"[ENERGY] Bulunamadı: {energyCsv}");
}

// --- 2) SU: water_consumption.csv -> /api/readings (WaterM3 doldurulur)
if (File.Exists(waterCsv))
{
    Console.WriteLine($"[WATER] Yükleniyor: {waterCsv}");
    using var sr = new StreamReader(waterCsv);
    using var csv = new CsvReader(sr, csvConfig);

    // beklenen kolonlar: campus_id, timestamp, consumption
    var records = csv.GetRecords<WaterRow>();

    int ok = 0, fail = 0;
    foreach (var r in records)
    {
        if (string.IsNullOrWhiteSpace(r.campus_id))
            continue;

        if (!double.TryParse(r.campus_id, NumberStyles.Any, CultureInfo.InvariantCulture, out var campus))
            continue;

        if (!DateTime.TryParse(r.timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts))
            continue;

        if (!double.TryParse(r.consumption, NumberStyles.Any, CultureInfo.InvariantCulture, out var cons))
            continue;

        // suyu litre varsayıp m3'e çeviriyoruz
        double waterM3 = cons / 1000.0;

        var payload = new
        {
            BuildingId = $"C{NormalizeId(campus)}_W",
            Timestamp = ts,
            EnergyKWh = 0.0,
            WaterM3 = waterM3
        };

        var resp = await http.PostAsJsonAsync($"{apiBase}/api/readings", payload);
        if (resp.IsSuccessStatusCode) ok++; else fail++;
    }

    Console.WriteLine($"[WATER] Tamamlandı. OK={ok}, FAIL={fail}");
}
else
{
    Console.WriteLine($"[WATER] Bulunamadı: {waterCsv}");
}

static string NormalizeId(double campus)
{
    // Bazı dosyalarda campus_id 1.0 gibi gelebiliyor → "1" yap
    if (Math.Abs(campus - Math.Round(campus)) < 1e-9)
        return ((int)Math.Round(campus)).ToString();
    return campus.ToString(CultureInfo.InvariantCulture);
}

// --- CSV modelleri
public record EnergyRow(string campus_id, string meter_id, string timestamp, string consumption);
public record WaterRow(string campus_id, string timestamp, string consumption);


using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using EcoPulse.Common.Database;
using System.Net;
using System.Net.Mail;
using Microsoft.ML.Data;

namespace EcoPulse.Worker.Services;

public class ForecastWorker : BackgroundService
{
    private readonly ILogger<ForecastWorker> _logger;
    private readonly MLContext _ml = new(seed: 7);

    // ---- ML.NET modelleri ----
    private static readonly MLContext _mlctx = new();

    private static readonly string _energyModelPath = Path.Combine("Models", "energy_forecast_model.zip");
    private static readonly string _waterModelPath = Path.Combine("Models", "water_forecast_model.zip");

    private static readonly PredictionEngine<FeatureRow, EnergyPrediction>? _energyEngine =
        File.Exists(_energyModelPath)
            ? _mlctx.Model.CreatePredictionEngine<FeatureRow, EnergyPrediction>(
                _mlctx.Model.Load(_energyModelPath, out _))
            : null;

    private static readonly PredictionEngine<FeatureRow, WaterPrediction>? _waterEngine =
        File.Exists(_waterModelPath)
            ? _mlctx.Model.CreatePredictionEngine<FeatureRow, WaterPrediction>(
                _mlctx.Model.Load(_waterModelPath, out _))
            : null;

    // ---- Feature girişi ve model çıktı sınıfları ----
    private sealed class FeatureRow
    {
        public string BuildingId { get; set; } = "";
        public float Month { get; set; }
        public float DayOfWeek { get; set; }
        public float IsWeekend { get; set; }
        public float IsHoliday { get; set; }
        public float Season { get; set; }
        public float PrevDayEnergy { get; set; }
        public float PrevDayWater { get; set; }
        public float SevenDayAvgEnergy { get; set; }
        public float SevenDayAvgWater { get; set; }
    }

    private sealed class EnergyPrediction { [ColumnName("Score")] public float Score { get; set; } }
    private sealed class WaterPrediction { [ColumnName("Score")] public float Score { get; set; } }

    // Prometheus metrikleri
    private static readonly Gauge EnergyAlert =
        Metrics.CreateGauge("ecopulse_energy_alert", "1 if forecast exceeds threshold",
            new GaugeConfiguration { LabelNames = new[] { "building" } });

    private static readonly Gauge WaterAlert =
        Metrics.CreateGauge("ecopulse_water_alert", "1 if forecast exceeds threshold",
            new GaugeConfiguration { LabelNames = new[] { "building" } });

    private static readonly Gauge EnergyForecastValue =
    Metrics.CreateGauge("ecopulse_energy_forecast_value", "Predicted energy consumption (kWh)",
        new GaugeConfiguration { LabelNames = new[] { "building" } });

    private static readonly Gauge WaterForecastValue =
        Metrics.CreateGauge("ecopulse_water_forecast_value", "Predicted water consumption (m³)",
            new GaugeConfiguration { LabelNames = new[] { "building" } });

    public ForecastWorker(ILogger<ForecastWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ForecastWorker başlatıldı ✅");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunForecastCycle();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tahmin döngüsü başarısız oldu ❌");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task RunForecastCycle()
    {
        using var con = new SqlConnection(DbHelper.ConnectionString);
        var buildings = await con.QueryAsync<string>(
            "SELECT BuildingId FROM Readings WITH (NOLOCK) GROUP BY BuildingId",
            commandTimeout: 300);

        foreach (var building in buildings)
        {
            var now = DateTime.UtcNow;
            var data = (await con.QueryAsync<(DateTime ts, double e, double w)>(
                @"SELECT Timestamp, EnergyKWh, WaterM3 
                  FROM Readings 
                  WHERE BuildingId = @b
                  ORDER BY Timestamp",
                new { b = building },
                commandTimeout: 300)).ToList();
            data = data.Select(x => (ts: x.ts.AddYears(10), e: x.e, w: x.w)).ToList();
            // --- 30 günlük veri seç ---
            data = data.Where(x => x.ts > now.AddDays(-30)).ToList();
            if (data.Count < 10)
            {
                _logger.LogWarning($"{building}: Yeterli veri yok ({data.Count}). Atlanıyor.");
                continue;
            }

            // --- GÜNLÜK TOPLAMLAR ---
            var daily = data.GroupBy(x => x.ts.Date)
                            .Select(g => new { Date = g.Key, Energy = g.Sum(x => x.e), Water = g.Sum(x => x.w) })
                            .OrderBy(x => x.Date)
                            .ToList();

            double predictedEnergy;
            double predictedWater;

            // --- Özellik üretimi ---
            var last = daily.Last();
            var lastDate = last.Date;

            float SevenAvg(IReadOnlyList<float> list)
            {
                if (list.Count == 0) return 0;
                var take = Math.Min(7, list.Count);
                return list.Skip(list.Count - take).Take(take).Average();
            }

            var enSeries = daily.Select(x => (float)x.Energy).ToList();
            var waSeries = daily.Select(x => (float)x.Water).ToList();

            var prevE = enSeries.Count >= 2 ? enSeries[^2] : enSeries.Last();
            var prevW = waSeries.Count >= 2 ? waSeries[^2] : waSeries.Last();

            var feat = new FeatureRow
            {
                BuildingId = building,
                Month = lastDate.Month,
                DayOfWeek = (int)lastDate.DayOfWeek,
                IsWeekend = (lastDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) ? 1f : 0f,
                IsHoliday = IsTrHoliday(lastDate) ? 1f : 0f,
                Season = GetSeason(lastDate),
                PrevDayEnergy = prevE,
                PrevDayWater = prevW,
                SevenDayAvgEnergy = SevenAvg(enSeries),
                SevenDayAvgWater = SevenAvg(waSeries)
            };

            // --- MODEL veya fallback ---
            if (_energyEngine != null)
                predictedEnergy = Math.Round(_energyEngine.Predict(feat).Score, 3);
            else
                predictedEnergy = Math.Round(daily.Average(x => x.Energy), 3);

            if (_waterEngine != null)
                predictedWater = Math.Round(_waterEngine.Predict(feat).Score, 3);
            else
                predictedWater = Math.Round(daily.Average(x => x.Water), 3);

            // --- DB'ye kaydet ---
            await con.ExecuteAsync(
                "INSERT INTO Forecasts(BuildingId,[Date],EnergyKWh,WaterM3) VALUES(@b,@d,@e,@w)",
                new { b = building, d = DateTime.UtcNow.Date.AddDays(1), e = predictedEnergy, w = predictedWater });

            // --- Prometheus metrikleri ---
            EnergyForecastValue.WithLabels(building).Set(predictedEnergy);
            WaterForecastValue.WithLabels(building).Set(predictedWater);

            EnergyAlert.WithLabels(building).Set(predictedEnergy > 80 ? 1 : 0);
            WaterAlert.WithLabels(building).Set(predictedWater > 12 ? 1 : 0);

            // --- E-Posta uyarısı ---
            if (predictedEnergy > 80 || predictedWater > 12)
                await SendEmailAlert(building, predictedEnergy, predictedWater);

            _logger.LogInformation($"Forecast ({building}) -> Energy={predictedEnergy} kWh, Water={predictedWater} m³");
        }
    }

    // === Yardımcılar ===
    private static bool IsTrHoliday(DateTime d)
    {
        var fixedHolidays = new (int m, int day)[]
        {
            (1,1),(4,23),(5,1),(5,19),(8,30),(10,29)
        };
        return fixedHolidays.Any(x => x.m == d.Month && x.day == d.Day);
    }

    private static float GetSeason(DateTime d)
    {
        return d.Month switch
        {
            12 or 1 or 2 => 1f,
            3 or 4 or 5 => 2f,
            6 or 7 or 8 => 3f,
            _ => 4f
        };
    }

    private static async Task SendEmailAlert(string building, double energy, double water)
    {
        const string GRAFANA_URL = "http://localhost:3000/d/ecopulse-dashboard?orgId=1";

        var smtpUser = Environment.GetEnvironmentVariable("EP_SMTP_USER")
                   ?? throw new InvalidOperationException("EP_SMTP_USER is not set");
        var smtpPass = Environment.GetEnvironmentVariable("EP_SMTP_PASS")
                       ?? throw new InvalidOperationException("EP_SMTP_PASS is not set");
        var smtpTo = Environment.GetEnvironmentVariable("EP_SMTP_TO")
                       ?? throw new InvalidOperationException("EP_SMTP_TO is not set");
        var smtpHost = Environment.GetEnvironmentVariable("EP_SMTP_HOST") ?? "smtp.gmail.com";
        var smtpPortStr = Environment.GetEnvironmentVariable("EP_SMTP_PORT") ?? "587";
        int smtpPort = int.Parse(smtpPortStr);

        string ecoPulseLogo = @"
        <svg xmlns='http://www.w3.org/2000/svg' width='200' height='50'>
          <rect width='200' height='50' rx='8' ry='8' fill='#2b5797'/>
          <text x='15' y='32' font-family='Segoe UI, Arial' font-size='20' fill='white'>Eco</text>
          <text x='65' y='32' font-family='Segoe UI, Arial' font-size='20' fill='#5bc0de'>Pulse</text>
          <circle cx='150' cy='25' r='6' fill='#d9534f'>
            <animate attributeName='r' values='5;8;5' dur='1.5s' repeatCount='indefinite'/>
          </circle>
        </svg>";

        var htmlBody = $@"
        <!DOCTYPE html>
        <html>
          <body style='font-family:Segoe UI, Arial, sans-serif; background:#f4f4f4; padding:20px;'>
            <div style='max-width:600px; margin:auto; background:white; border-radius:10px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,0.1);'>
              <div style='background:#2b5797; color:white; padding:15px; text-align:center;'>
                {ecoPulseLogo}
                <h2 style='margin-top:10px;'>⚡ EcoPulse Tüketim Uyarısı</h2>
              </div>
              <div style='padding:20px;'>
                <p>Merhaba,</p>
                <p><strong>{building}</strong> binasında tüketim eşiği aşıldı:</p>
                <table style='width:100%; border-collapse:collapse; margin-top:10px;'>
                  <tr style='background:#e8f4fc;'>
                    <th style='text-align:left; padding:8px;'>Ölçüm</th>
                    <th style='text-align:left; padding:8px;'>Değer</th>
                  </tr>
                  <tr>
                    <td style='padding:8px;'>Enerji</td>
                    <td style='padding:8px; color:#d9534f; font-weight:bold;'>{energy} kWh</td>
                  </tr>
                  <tr>
                    <td style='padding:8px;'>Su</td>
                    <td style='padding:8px; color:#5bc0de; font-weight:bold;'>{water} m³</td>
                  </tr>
                </table>
                <div style='margin-top:22px;'>
                  <a href='{GRAFANA_URL}' style='display:inline-block; padding:12px 18px; background:#2b5797; color:#fff; text-decoration:none; border-radius:8px; font-weight:600;'>
                    Grafana Dashboard’u Aç
                  </a>
                </div>
                <p style='margin-top:20px;'>📅 Tarih: {DateTime.Now:dd.MM.yyyy HH:mm}</p>
                <p>Bu uyarı <b>EcoPulse</b> otomatik izleme sistemi tarafından gönderilmiştir.</p>
              </div>
              <div style='background:#2b5797; color:white; text-align:center; padding:10px;'>
                <small>© 2025 EcoPulse Akıllı Enerji ve Su Platformu</small>
              </div>
            </div>
          </body>
        </html>";

        var msg = new MailMessage
        {
            From = new MailAddress(smtpUser, "EcoPulse Alert System"),
            Subject = $"⚠️ {building} tüketim uyarısı",
            Body = htmlBody,
            IsBodyHtml = true
        };
        msg.To.Add(smtpTo);

        using var smtp = new SmtpClient(smtpHost, smtpPort)
        {
            Credentials = new NetworkCredential(smtpUser, smtpPass),
            EnableSsl = true
        };

        await smtp.SendMailAsync(msg);
        Console.WriteLine($"✅ E-posta gönderildi: {building}");
    }

    private class EnergyInput { public float Value { get; set; } }
    private class EnergyForecast { public float[] Forecast { get; set; } = Array.Empty<float>(); }
}

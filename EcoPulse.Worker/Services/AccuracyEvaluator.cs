using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using EcoPulse.Common.Database;

namespace EcoPulse.Worker.Services;

public class AccuracyEvaluator : BackgroundService
{
    private readonly ILogger<AccuracyEvaluator> _logger;
    private static readonly Gauge ModelRmse =
        Metrics.CreateGauge("ecopulse_model_rmse", "Root Mean Square Error", new GaugeConfiguration { LabelNames = new[] { "metric" } });

    private static readonly Gauge ModelMape =
        Metrics.CreateGauge("ecopulse_model_mape", "Mean Absolute Percentage Error", new GaugeConfiguration { LabelNames = new[] { "metric" } });

    public AccuracyEvaluator(ILogger<AccuracyEvaluator> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AccuracyEvaluator başlatıldı ✅");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateModelAccuracy();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Model doğruluk hesaplaması başarısız oldu ❌");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }

    private async Task EvaluateModelAccuracy()
    {
        using var con = new SqlConnection(DbHelper.ConnectionString);

        var records = (await con.QueryAsync<(string BuildingId, DateTime Date, double ForecastEnergy, double ForecastWater, double RealEnergy, double RealWater)>(
            @"SELECT f.BuildingId, f.[Date], 
                     f.EnergyKWh AS ForecastEnergy, f.WaterM3 AS ForecastWater,
                     rE.TotalEnergy AS RealEnergy, rW.TotalWater AS RealWater
              FROM Forecasts f
              JOIN (
                  SELECT BuildingId, CAST([Timestamp] AS date) AS [Date], SUM(EnergyKWh) AS TotalEnergy
                  FROM Readings GROUP BY BuildingId, CAST([Timestamp] AS date)
              ) rE ON f.BuildingId = rE.BuildingId AND f.[Date] = rE.[Date]
              JOIN (
                  SELECT BuildingId, CAST([Timestamp] AS date) AS [Date], SUM(WaterM3) AS TotalWater
                  FROM Readings GROUP BY BuildingId, CAST([Timestamp] AS date)
              ) rW ON f.BuildingId = rW.BuildingId AND f.[Date] = rW.[Date]"
        )).ToList();

        if (records.Count == 0)
        {
            _logger.LogWarning("Karşılaştırılacak forecast/real veri yok");
            return;
        }

        double Rmse(List<double> forecast, List<double> actual) =>
            Math.Sqrt(forecast.Zip(actual, (f, a) => Math.Pow(f - a, 2)).Average());

        double Mape(List<double> forecast, List<double> actual) =>
            forecast.Zip(actual, (f, a) => Math.Abs((a - f) / (a == 0 ? 1 : a))).Average() * 100;

        var fEnergy = records.Select(r => r.ForecastEnergy).ToList();
        var aEnergy = records.Select(r => r.RealEnergy).ToList();

        var fWater = records.Select(r => r.ForecastWater).ToList();
        var aWater = records.Select(r => r.RealWater).ToList();

        var rmseEnergy = Rmse(fEnergy, aEnergy);
        var rmseWater = Rmse(fWater, aWater);

        var mapeEnergy = Mape(fEnergy, aEnergy);
        var mapeWater = Mape(fWater, aWater);

        // Prometheus metriklerini set et
        ModelRmse.WithLabels("energy").Set(rmseEnergy);
        ModelRmse.WithLabels("water").Set(rmseWater);
        ModelMape.WithLabels("energy").Set(mapeEnergy);
        ModelMape.WithLabels("water").Set(mapeWater);

        _logger.LogInformation($"📊 RMSE(energy)={rmseEnergy:F3}, MAPE(energy)={mapeEnergy:F2}%");
        _logger.LogInformation($"📊 RMSE(water)={rmseWater:F3}, MAPE(water)={mapeWater:F2}%");
    }
}

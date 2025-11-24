using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.ML;
using Microsoft.ML.Data;
using EcoPulse.Common.Database;

namespace EcoPulse.Analytics;

public static class ModelTrainer
{
    private const int SHIFT_YEARS = 10;
    private const int MIN_DAYS_PER_BUILDING = 10;
    private const string OUT_DIR = "Models";

    private static readonly MLContext ml = new(seed: 7);

    public static void TrainAndSaveModel()
    {
        Directory.CreateDirectory(OUT_DIR);

        using var con = new SqlConnection(DbHelper.ConnectionString);
        var rows = con.Query<DbRow>(@"
    SELECT
        BuildingId,
        CAST([Timestamp] AS date) AS [Date],
        SUM(EnergyKWh) AS EnergyKWh,
        SUM(WaterM3)  AS WaterM3
    FROM Readings WITH (NOLOCK)
    GROUP BY BuildingId, CAST([Timestamp] AS date)
    ORDER BY BuildingId, CAST([Timestamp] AS date);
", commandTimeout: 300).ToList(); // 5 dakika timeout


        if (rows.Count == 0)
        {
            Console.WriteLine("Empty query result");
            return;
        }

        foreach (var r in rows) r.Date = r.Date.AddYears(SHIFT_YEARS);

        var feats = BuildFeatures(rows);
        var data = ml.Data.LoadFromEnumerable(feats);

        var prep = ml.Transforms.Categorical.OneHotEncoding(
                        outputColumnName: "BuildingIdEncoded",
                        inputColumnName: nameof(FeatureRow.BuildingId))
                   .Append(ml.Transforms.Concatenate("Features",
                       nameof(FeatureRow.Month),
                       nameof(FeatureRow.DayOfWeek),
                       nameof(FeatureRow.IsWeekend),
                       nameof(FeatureRow.IsHoliday),
                       nameof(FeatureRow.Season),
                       nameof(FeatureRow.PrevDayEnergy),
                       nameof(FeatureRow.PrevDayWater),
                       nameof(FeatureRow.SevenDayAvgEnergy),
                       nameof(FeatureRow.SevenDayAvgWater),
                       "BuildingIdEncoded"))
                   .Append(ml.Transforms.NormalizeMinMax("Features"));

        var energyPipe = prep.Append(ml.Regression.Trainers.FastTree(
            labelColumnName: nameof(FeatureRow.EnergyKWh),
            featureColumnName: "Features"));
        var waterPipe = prep.Append(ml.Regression.Trainers.FastTree(
            labelColumnName: nameof(FeatureRow.WaterM3),
            featureColumnName: "Features"));

        var energyModel = energyPipe.Fit(data);
        var waterModel = waterPipe.Fit(data);

        var energyPath = Path.Combine(OUT_DIR, "energy_forecast_model.zip");
        var waterPath = Path.Combine(OUT_DIR, "water_forecast_model.zip");
        ml.Model.Save(energyModel, data.Schema, energyPath);
        ml.Model.Save(waterModel, data.Schema, waterPath);

        var workerModelsDir = Path.GetFullPath(Path.Combine("..", "EcoPulse.Worker", "Models"));
        Directory.CreateDirectory(workerModelsDir);
        File.Copy(energyPath, Path.Combine(workerModelsDir, "energy_forecast_model.zip"), true);
        File.Copy(waterPath, Path.Combine(workerModelsDir, "water_forecast_model.zip"), true);

        Console.WriteLine("✅ Modeller kaydedildi:");
        Console.WriteLine(" - " + Path.GetFullPath(energyPath));
        Console.WriteLine(" - " + Path.GetFullPath(waterPath));
    }

    private static List<FeatureRow> BuildFeatures(List<DbRow> rows)
    {
        var byBuilding = rows.GroupBy(r => r.BuildingId)
                             .Where(g => g.Count() >= MIN_DAYS_PER_BUILDING)
                             .ToList();
        var result = new List<FeatureRow>();

        foreach (var g in byBuilding)
        {
            var ordered = g.OrderBy(r => r.Date).ToList();
            var lastE = new Queue<double>();
            var lastW = new Queue<double>();
            double sumE = 0, sumW = 0;
            DbRow? prev = null;

            foreach (var r in ordered)
            {
                lastE.Enqueue(r.EnergyKWh); sumE += r.EnergyKWh;
                lastW.Enqueue(r.WaterM3); sumW += r.WaterM3;
                if (lastE.Count > 7) sumE -= lastE.Dequeue();
                if (lastW.Count > 7) sumW -= lastW.Dequeue();

                var month = r.Date.Month;
                var dow = (int)r.Date.DayOfWeek;
                var isWeekend = dow is 0 or 6 ? 1f : 0f;
                var isHoliday = IsTrHoliday(r.Date) ? 1f : 0f;
                var season = GetSeason(r.Date);

                var prevE = prev?.EnergyKWh ?? r.EnergyKWh;
                var prevW = prev?.WaterM3 ?? r.WaterM3;
                var sevenAvgE = (float)(sumE / lastE.Count);
                var sevenAvgW = (float)(sumW / lastW.Count);

                result.Add(new FeatureRow
                {
                    BuildingId = r.BuildingId,
                    Month = month,
                    DayOfWeek = dow,
                    IsWeekend = isWeekend,
                    IsHoliday = isHoliday,
                    Season = season,
                    PrevDayEnergy = (float)prevE,
                    PrevDayWater = (float)prevW,
                    SevenDayAvgEnergy = sevenAvgE,
                    SevenDayAvgWater = sevenAvgW,
                    EnergyKWh = (float)r.EnergyKWh,
                    WaterM3 = (float)r.WaterM3
                });

                prev = r;
            }
        }
        return result;
    }

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

    private sealed class DbRow
    {
        public string BuildingId { get; set; } = "";
        public DateTime Date { get; set; }
        public double EnergyKWh { get; set; }
        public double WaterM3 { get; set; }
    }

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
        public float EnergyKWh { get; set; }
        public float WaterM3 { get; set; }
    }
}

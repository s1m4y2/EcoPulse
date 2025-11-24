using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Prometheus;
using Microsoft.OpenApi.Models;
using EcoPulse.Common.Database;
using EcoPulse.Common.Models;
using DotNetEnv;

var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
Env.Load(envPath);


var builder = WebApplication.CreateBuilder(args);

// 🔹 Swagger hizmeti
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "EcoPulse API", Version = "v1" });
});

var app = builder.Build();

// 🔹 Veritabanı yoksa oluştur
await DbHelper.EnsureDatabaseAsync();

// 🔹 Prometheus özel metrikleri
var energyGauge = Metrics.CreateGauge(
    "ecopulse_energy_kwh",
    "Toplam enerji tüketimi (kWh)",
    new GaugeConfiguration { LabelNames = new[] { "building" } }
);

var waterGauge = Metrics.CreateGauge(
    "ecopulse_water_m3",
    "Toplam su tüketimi (m³)",
    new GaugeConfiguration { LabelNames = new[] { "building" } }
);

// 🔹 Middleware’ler
app.UseRouting();
app.UseHttpMetrics();

// 🔹 Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "EcoPulse API v1");
});

// 🔹 API endpoint’leri
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/api/readings", async () =>
{
    using var con = new SqlConnection(DbHelper.ConnectionString);
    var data = await con.QueryAsync<Reading>("SELECT TOP (1000) * FROM Readings ORDER BY Timestamp DESC");
    return Results.Ok(data);
});

app.MapGet("/api/forecasts", async (string building) =>
{
    using var con = new SqlConnection(DbHelper.ConnectionString);
    var data = await con.QueryAsync<Forecast>(
        "SELECT TOP (30) * FROM Forecasts WHERE BuildingId=@b ORDER BY [Date] DESC",
        new { b = building });
    return Results.Ok(data);
});

app.MapPost("/api/readings", async (Reading dto) =>
{
    using var con = new SqlConnection(DbHelper.ConnectionString);
    string sql = """
        INSERT INTO Readings(BuildingId, Timestamp, EnergyKWh, WaterM3)
        VALUES(@BuildingId, @Timestamp, @EnergyKWh, @WaterM3)
        """;
    await con.ExecuteAsync(sql, dto);

    // 🔹 Prometheus metrik güncelle
    energyGauge.WithLabels(dto.BuildingId).Inc(dto.EnergyKWh);
    waterGauge.WithLabels(dto.BuildingId).Inc(dto.WaterM3);

    return Results.Accepted();
});

// 🔹 Prometheus metrik endpoint'i
app.MapMetrics();

// 🔹 TEST VERİ ÜRETİCİ (otomatik)
var random = new Random();
var buildings = new[] { "C1_M1", "C2_M1", "C3_M1" };

app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        while (true)
        {
            foreach (var b in buildings)
            {
                // 🔹 Rastgele değerlerle metrikleri doldur
                energyGauge.WithLabels(b).Set(random.Next(50, 200));
                waterGauge.WithLabels(b).Set(random.Next(10, 80));
            }

            Console.WriteLine($"[{DateTime.Now:T}] Otomatik metrikler güncellendi ✅");
            await Task.Delay(10000); // 10 saniyede bir yenile
        }
    });
});

// 🔹 Port
app.Urls.Add("http://0.0.0.0:5080");

app.Run();

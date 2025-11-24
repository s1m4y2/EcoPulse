using Dapper;
using Microsoft.Data.SqlClient;

namespace EcoPulse.Common.Database;

public static class DbHelper
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("EP_SQL")
        ?? throw new InvalidOperationException("EP_SQL environment variable is not set.");

    public static async Task EnsureDatabaseAsync()
    {
        // master bağlantısını da env'den oku
        var masterConn =
            Environment.GetEnvironmentVariable("EP_SQL_MASTER")
            ?? throw new InvalidOperationException("EP_SQL_MASTER environment variable is not set.");

        // önce master'a bağlanıp veritabanı var mı bakıyoruz
        using (var con = new SqlConnection(masterConn))
        {
            await con.OpenAsync();
            var exists = await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.databases WHERE name = 'EcoPulse'");

            if (exists == 0)
            {
                await con.ExecuteAsync("CREATE DATABASE EcoPulse");
            }
        }

        // sonra asıl EcoPulse veritabanına bağlanıp tabloları oluşturuyoruz
        using var db = new SqlConnection(ConnectionString);
        await db.OpenAsync();

        await db.ExecuteAsync("""
            IF OBJECT_ID('dbo.Readings','U') IS NULL
            CREATE TABLE dbo.Readings(
                Id INT IDENTITY(1,1) PRIMARY KEY,
                BuildingId NVARCHAR(64) NOT NULL,
                Timestamp DATETIME2 NOT NULL,
                EnergyKWh FLOAT NOT NULL,
                WaterM3 FLOAT NOT NULL
            );

            IF OBJECT_ID('dbo.Forecasts','U') IS NULL
            CREATE TABLE dbo.Forecasts(
                Id INT IDENTITY(1,1) PRIMARY KEY,
                BuildingId NVARCHAR(64) NOT NULL,
                [Date] DATE NOT NULL,
                EnergyKWh FLOAT NOT NULL,
                WaterM3 FLOAT NOT NULL
            );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Readings_Building_Ts')
            CREATE INDEX IX_Readings_Building_Ts ON dbo.Readings(BuildingId, Timestamp);
        """);
    }
}

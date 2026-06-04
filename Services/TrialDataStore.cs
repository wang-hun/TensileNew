using System.Collections.Concurrent;
using System.IO;
using Microsoft.Data.Sqlite;
using NLog;
using TensileNeW.Models;

namespace TensileNeW.Services;

public static class TrialDataStore
{
    private const string DataDirectoryName = "data";
    private const string DatabaseFileName = "trial-data.sqlite";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly BlockingCollection<TrialPoint> PendingPoints = new();
    private static readonly object StartLock = new();
    private static Task? _writerTask;
    private static bool _started;
    private static bool _disabled;

    public static void EnqueuePoint(string trialSerialNumber, Loadmodel source)
    {
        try
        {
            if (_disabled)
            {
                return;
            }

            EnsureStarted();

            PendingPoints.Add(new TrialPoint(
                trialSerialNumber,
                source.Index,
                source.RealPress,
                source.RealDistance,
                source.RealForce,
                source.Time,
                DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _disabled = true;
            Logger.Error(ex, "试验数据 SQLite 持久化已禁用。");
        }
    }

    private static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        lock (StartLock)
        {
            if (_started)
            {
                return;
            }

            _writerTask = Task.Run(ProcessQueue);
            _started = true;
        }
    }

    private static void ProcessQueue()
    {
        try
        {
            string databasePath = GetDatabasePath();
            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();
            EnsureSchema(connection);

            foreach (TrialPoint point in PendingPoints.GetConsumingEnumerable())
            {
                try
                {
                    InsertPoint(connection, point);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "试验数据写入 SQLite 失败。");
                }
            }
        }
        catch (Exception ex)
        {
            _disabled = true;
            Logger.Error(ex, "试验数据写入 SQLite 失败。");
        }
    }

    private static string GetDatabasePath()
    {
        string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DataDirectoryName);
        Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, DatabaseFileName);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TrialPoints (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TrialSerialNumber TEXT NOT NULL,
                PointIndex INTEGER NOT NULL,
                RealPress REAL NOT NULL,
                RealDistance REAL NOT NULL,
                RealForce REAL NOT NULL,
                Time TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_TrialPoints_TrialSerialNumber_Id
            ON TrialPoints (TrialSerialNumber, Id);
            """;
        command.ExecuteNonQuery();
    }

    private static void InsertPoint(SqliteConnection connection, TrialPoint point)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrialPoints (
                TrialSerialNumber,
                PointIndex,
                RealPress,
                RealDistance,
                RealForce,
                Time,
                CreatedAtUtc
            )
            VALUES (
                @trialSerialNumber,
                @pointIndex,
                @realPress,
                @realDistance,
                @realForce,
                @time,
                @createdAtUtc
            );
            """;

        command.Parameters.AddWithValue("@trialSerialNumber", point.TrialSerialNumber);
        command.Parameters.AddWithValue("@pointIndex", point.PointIndex);
        command.Parameters.AddWithValue("@realPress", point.RealPress);
        command.Parameters.AddWithValue("@realDistance", point.RealDistance);
        command.Parameters.AddWithValue("@realForce", point.RealForce);
        command.Parameters.AddWithValue("@time", point.Time);
        command.Parameters.AddWithValue("@createdAtUtc", point.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    private sealed record TrialPoint(
        string TrialSerialNumber,
        int PointIndex,
        float RealPress,
        float RealDistance,
        float RealForce,
        string Time,
        DateTime CreatedAtUtc);
}

using System.Collections.Concurrent;
using System.Globalization;
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
    private static readonly BlockingCollection<IDatabaseWork> PendingWork = new();
    private static readonly object StartLock = new();
    private static readonly object StateLock = new();
    private static Task? _writerTask;
    private static bool _started;
    private static bool _disabled;
    private static RecipeSnapshot? _currentRecipe;
    private static bool _startNewGroupOnNextPoint = true;

    public sealed record TrialCurveSummary(string TrialSerialNumber, DateTime StartedAtUtc);

    public static void InitializeRecipes(
        IEnumerable<RecipeModel> builtInRecipes,
        IEnumerable<RecipeModel> userRecipes,
        RecipeModel? currentRecipe)
    {
        try
        {
            if (_disabled)
            {
                return;
            }

            EnsureStarted();
            SetCurrentRecipe(currentRecipe);
            PendingWork.Add(new SyncRecipesWork(
                builtInRecipes.Select(recipe => RecipeSnapshot.From(recipe, true)).ToList(),
                userRecipes.Select(recipe => RecipeSnapshot.From(recipe, false)).ToList()));
        }
        catch (Exception ex)
        {
            DisableStore(ex);
        }
    }

    public static void SetCurrentRecipe(RecipeModel? recipe)
    {
        lock (StateLock)
        {
            _currentRecipe = recipe == null
                ? null
                : RecipeSnapshot.From(recipe, recipe.IsBuiltInRecipe);
        }
    }

    public static void RecordRecipeVersion(RecipeModel? recipe)
    {
        try
        {
            if (_disabled || recipe == null)
            {
                return;
            }

            EnsureStarted();
            RecipeSnapshot snapshot = RecipeSnapshot.From(recipe, recipe.IsBuiltInRecipe);
            PendingWork.Add(new UpsertRecipeVersionWork(snapshot));
            SetCurrentRecipe(recipe);
        }
        catch (Exception ex)
        {
            DisableStore(ex);
        }
    }

    public static void RecordRecipeDeleted(RecipeModel? recipe)
    {
        try
        {
            if (_disabled || recipe == null)
            {
                return;
            }

            EnsureStarted();
            PendingWork.Add(new MarkRecipeDeletedWork(RecipeSnapshot.From(recipe, recipe.IsBuiltInRecipe)));
        }
        catch (Exception ex)
        {
            DisableStore(ex);
        }
    }

    public static void BeginNewTrialOnNextPoint()
    {
        lock (StateLock)
        {
            _startNewGroupOnNextPoint = true;
        }
    }

    public static void EnqueuePoint(string trialSerialNumber, Loadmodel source)
    {
        try
        {
            if (_disabled)
            {
                return;
            }

            EnsureStarted();

            RecipeSnapshot? recipe;
            bool forceNewGroup;
            lock (StateLock)
            {
                recipe = _currentRecipe;
                forceNewGroup = _startNewGroupOnNextPoint;
                if (forceNewGroup)
                {
                    _startNewGroupOnNextPoint = false;
                }
            }

            PendingWork.Add(new InsertPointWork(
                trialSerialNumber,
                recipe,
                forceNewGroup,
                source.Index,
                source.RealPress,
                source.RealDistance,
                source.RealForce,
                source.Time,
                DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            DisableStore(ex);
        }
    }

    public static void TryDeleteDatabaseFile()
    {
        try
        {
            if (_started && !PendingWork.IsAddingCompleted)
            {
                PendingWork.CompleteAdding();
                _writerTask?.Wait(TimeSpan.FromSeconds(1));
            }

            string databasePath = GetDatabasePath(createDirectory: false);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
        catch
        {
        }
    }

    public static IReadOnlyList<TrialCurveSummary> GetRecentCurveSummaries(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        string databasePath = GetDatabasePath(createDirectory: false);
        if (!File.Exists(databasePath))
        {
            return [];
        }

        try
        {
            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT TrialSerialNumber, StartedAtUtc
                FROM TrialGroups
                ORDER BY Id DESC
                LIMIT @count;
                """;
            command.Parameters.AddWithValue("@count", count);

            List<TrialCurveSummary> summaries = [];
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string trialSerialNumber = reader.GetString(0);
                string startedAtText = reader.GetString(1);
                if (DateTime.TryParse(
                        startedAtText,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTime startedAtUtc))
                {
                    summaries.Add(new TrialCurveSummary(trialSerialNumber, startedAtUtc));
                }
            }

            summaries.Reverse();
            return summaries;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "读取试验曲线摘要失败。");
            return [];
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

            WriterState state = new();
            foreach (IDatabaseWork work in PendingWork.GetConsumingEnumerable())
            {
                try
                {
                    work.Execute(connection, state);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "试验数据写入 SQLite 失败。");
                }
            }
        }
        catch (Exception ex)
        {
            DisableStore(ex);
        }
    }

    private static string GetDatabasePath(bool createDirectory = true)
    {
        string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DataDirectoryName);
        if (createDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
        }

        return Path.Combine(dataDirectory, DatabaseFileName);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Recipes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecipeName TEXT NOT NULL,
                StrokeStampingForce REAL NOT NULL,
                ClosedLoopStampingForce REAL NOT NULL,
                ShutdownDelay INTEGER NOT NULL,
                ShutdownRatio REAL NOT NULL,
                Speed REAL NOT NULL,
                TensileDistanceLimit REAL NOT NULL,
                IsBuiltInRecipe INTEGER NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                DeletedAtUtc TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Recipes_Name_Deleted_Id
            ON Recipes (RecipeName, IsDeleted, Id);

            CREATE TABLE IF NOT EXISTS TrialGroups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TrialSerialNumber TEXT NOT NULL,
                RecipeId INTEGER NULL,
                StartedAtUtc TEXT NOT NULL,
                FOREIGN KEY (RecipeId) REFERENCES Recipes(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_TrialGroups_Id
            ON TrialGroups (Id);

            CREATE TABLE IF NOT EXISTS TrialPoints (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TrialGroupId INTEGER NOT NULL,
                PointIndex INTEGER NOT NULL,
                RealPress REAL NOT NULL,
                RealDistance REAL NOT NULL,
                RealForce REAL NOT NULL,
                Time TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TrialGroupId) REFERENCES TrialGroups(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_TrialPoints_TrialGroupId_Id
            ON TrialPoints (TrialGroupId, Id);
            """;
        command.ExecuteNonQuery();
    }

    private static long EnsureActiveRecipe(SqliteConnection connection, WriterState state, RecipeSnapshot recipe)
    {
        string key = recipe.Key;
        if (state.ActiveRecipeIds.TryGetValue(key, out long recipeId))
        {
            return recipeId;
        }

        MarkRecipeDeleted(connection, recipe);
        recipeId = InsertRecipe(connection, recipe);
        state.ActiveRecipeIds[key] = recipeId;
        return recipeId;
    }

    private static long InsertRecipe(SqliteConnection connection, RecipeSnapshot recipe)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Recipes (
                RecipeName,
                StrokeStampingForce,
                ClosedLoopStampingForce,
                ShutdownDelay,
                ShutdownRatio,
                Speed,
                TensileDistanceLimit,
                IsBuiltInRecipe,
                IsDeleted,
                CreatedAtUtc
            )
            VALUES (
                @recipeName,
                @strokeStampingForce,
                @closedLoopStampingForce,
                @shutdownDelay,
                @shutdownRatio,
                @speed,
                @tensileDistanceLimit,
                @isBuiltInRecipe,
                0,
                @createdAtUtc
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("@recipeName", recipe.RecipeName);
        command.Parameters.AddWithValue("@strokeStampingForce", recipe.StrokeStampingForce);
        command.Parameters.AddWithValue("@closedLoopStampingForce", recipe.ClosedLoopStampingForce);
        command.Parameters.AddWithValue("@shutdownDelay", recipe.ShutdownDelay);
        command.Parameters.AddWithValue("@shutdownRatio", recipe.ShutdownRatio);
        command.Parameters.AddWithValue("@speed", recipe.Speed);
        command.Parameters.AddWithValue("@tensileDistanceLimit", recipe.TensileDistanceLimit);
        command.Parameters.AddWithValue("@isBuiltInRecipe", recipe.IsBuiltInRecipe ? 1 : 0);
        command.Parameters.AddWithValue("@createdAtUtc", DateTime.UtcNow.ToString("O"));
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static void MarkRecipeDeleted(SqliteConnection connection, RecipeSnapshot recipe)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Recipes
            SET IsDeleted = 1,
                DeletedAtUtc = @deletedAtUtc
            WHERE RecipeName = @recipeName
              AND IsBuiltInRecipe = @isBuiltInRecipe
              AND IsDeleted = 0;
            """;

        command.Parameters.AddWithValue("@recipeName", recipe.RecipeName);
        command.Parameters.AddWithValue("@isBuiltInRecipe", recipe.IsBuiltInRecipe ? 1 : 0);
        command.Parameters.AddWithValue("@deletedAtUtc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static long CreateTrialGroup(
        SqliteConnection connection,
        string trialSerialNumber,
        long? recipeId,
        DateTime startedAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrialGroups (
                TrialSerialNumber,
                RecipeId,
                StartedAtUtc
            )
            VALUES (
                @trialSerialNumber,
                @recipeId,
                @startedAtUtc
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("@trialSerialNumber", trialSerialNumber);
        command.Parameters.AddWithValue("@recipeId", recipeId.HasValue ? recipeId.Value : DBNull.Value);
        command.Parameters.AddWithValue("@startedAtUtc", startedAtUtc.ToString("O"));
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static void InsertPoint(SqliteConnection connection, long trialGroupId, InsertPointWork point)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrialPoints (
                TrialGroupId,
                PointIndex,
                RealPress,
                RealDistance,
                RealForce,
                Time,
                CreatedAtUtc
            )
            VALUES (
                @trialGroupId,
                @pointIndex,
                @realPress,
                @realDistance,
                @realForce,
                @time,
                @createdAtUtc
            );
            """;

        command.Parameters.AddWithValue("@trialGroupId", trialGroupId);
        command.Parameters.AddWithValue("@pointIndex", point.PointIndex);
        command.Parameters.AddWithValue("@realPress", point.RealPress);
        command.Parameters.AddWithValue("@realDistance", point.RealDistance);
        command.Parameters.AddWithValue("@realForce", point.RealForce);
        command.Parameters.AddWithValue("@time", point.Time);
        command.Parameters.AddWithValue("@createdAtUtc", point.CreatedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void DisableStore(Exception ex)
    {
        _disabled = true;
        Logger.Error(ex, "试验数据 SQLite 持久化已禁用。");
    }

    private interface IDatabaseWork
    {
        void Execute(SqliteConnection connection, WriterState state);
    }

    private sealed class WriterState
    {
        public Dictionary<string, long> ActiveRecipeIds { get; } = [];
        public long? CurrentTrialGroupId { get; set; }
        public int? PreviousPointIndex { get; set; }
    }

    private sealed record RecipeSnapshot(
        string RecipeName,
        float StrokeStampingForce,
        float ClosedLoopStampingForce,
        ushort ShutdownDelay,
        float ShutdownRatio,
        float Speed,
        float TensileDistanceLimit,
        bool IsBuiltInRecipe)
    {
        public string Key => $"{(IsBuiltInRecipe ? "B" : "U")}|{RecipeName}";

        public static RecipeSnapshot From(RecipeModel recipe, bool isBuiltInRecipe)
        {
            return new RecipeSnapshot(
                recipe.RecipeName ?? string.Empty,
                recipe.StrokeStampingForce,
                recipe.ClosedLoopStampingForce,
                recipe.ShutdownDelay,
                recipe.ShutdownRatio,
                recipe.Speed,
                recipe.TensileDistanceLimit,
                isBuiltInRecipe);
        }
    }

    private sealed record SyncRecipesWork(
        IReadOnlyList<RecipeSnapshot> BuiltInRecipes,
        IReadOnlyList<RecipeSnapshot> UserRecipes) : IDatabaseWork
    {
        public void Execute(SqliteConnection connection, WriterState state)
        {
            foreach (RecipeSnapshot recipe in BuiltInRecipes.Concat(UserRecipes))
            {
                long id = InsertRecipe(connection, recipe);
                state.ActiveRecipeIds[recipe.Key] = id;
            }
        }
    }

    private sealed record UpsertRecipeVersionWork(RecipeSnapshot Recipe) : IDatabaseWork
    {
        public void Execute(SqliteConnection connection, WriterState state)
        {
            MarkRecipeDeleted(connection, Recipe);
            long id = InsertRecipe(connection, Recipe);
            state.ActiveRecipeIds[Recipe.Key] = id;
        }
    }

    private sealed record MarkRecipeDeletedWork(RecipeSnapshot Recipe) : IDatabaseWork
    {
        public void Execute(SqliteConnection connection, WriterState state)
        {
            MarkRecipeDeleted(connection, Recipe);
            state.ActiveRecipeIds.Remove(Recipe.Key);
        }
    }

    private sealed record InsertPointWork(
        string TrialSerialNumber,
        RecipeSnapshot? Recipe,
        bool ForceNewGroup,
        int PointIndex,
        float RealPress,
        float RealDistance,
        float RealForce,
        string Time,
        DateTime CreatedAtUtc) : IDatabaseWork
    {
        public void Execute(SqliteConnection connection, WriterState state)
        {
            bool startsNewGroup =
                ForceNewGroup ||
                state.CurrentTrialGroupId is null ||
                PointIndex <= 1 ||
                (state.PreviousPointIndex.HasValue && PointIndex <= state.PreviousPointIndex.Value);

            if (startsNewGroup)
            {
                long? recipeId = Recipe == null
                    ? null
                    : EnsureActiveRecipe(connection, state, Recipe);
                state.CurrentTrialGroupId = CreateTrialGroup(connection, TrialSerialNumber, recipeId, CreatedAtUtc);
            }

            if (!state.CurrentTrialGroupId.HasValue)
            {
                throw new InvalidOperationException("试验组别未创建，无法写入原始数据。");
            }

            InsertPoint(connection, state.CurrentTrialGroupId.Value, this);
            state.PreviousPointIndex = PointIndex;
        }
    }
}

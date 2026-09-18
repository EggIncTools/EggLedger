using System.IO.Compression;
using EggLedger.Desktop.Storage;
using EggLedger.Web.Data;

namespace EggLedger.Desktop.Tests;

public sealed class SqliteIndexedDbTests : IDisposable {
    private readonly SqliteDatabase _missionDb;
    private readonly SqliteDatabase _reportDb;
    private readonly SqliteIndexedDb _db;

    public SqliteIndexedDbTests() {
        var tag = Guid.NewGuid().ToString("N");
        _missionDb = SqliteDatabase.Open($"Data Source=m_{tag};Mode=Memory;Cache=Shared", isConnectionString: true);
        _reportDb = SqliteDatabase.Open($"Data Source=r_{tag};Mode=Memory;Cache=Shared", isConnectionString: true);
        SqliteMigrationRunner.MigrateMissionDb(_missionDb.Connection);
        SqliteMigrationRunner.MigrateReportDb(_reportDb.Connection);
        _db = new SqliteIndexedDb(_missionDb, _reportDb);
    }

    public void Dispose() {
        _missionDb.Dispose();
        _reportDb.Dispose();
    }

    [Fact]
    public async Task Settings_PutGetRoundTrip() {
        await _db.PutAsync("settings", new SettingRow { Key = "theme", Value = "dark" });
        var row = await _db.GetAsync<SettingRow>("settings", "theme");
        Assert.NotNull(row);
        Assert.Equal("dark", row!.Value);

        await _db.PutAsync("settings", new SettingRow { Key = "theme", Value = "light" });
        row = await _db.GetAsync<SettingRow>("settings", "theme");
        Assert.Equal("light", row!.Value);
    }

    [Fact]
    public async Task Mission_CompositeKeyAndBlobAndBool() {
        var payload = new byte[] { 1, 2, 3, 250, 255 };
        var mission = new MissionRow {
            PlayerId = "EI1",
            MissionId = "abc",
            StartTimestamp = 1700000000,
            CompletePayload = payload,
            MissionType = 1,
            Ship = 5,
            DurationType = 2,
            Level = 3,
            Capacity = 10,
            IsDubCap = true,
            IsBuggedCap = false,
            Target = 4,
            ReturnTimestamp = 1700003600,
            NominalCapacity = 6,
        };
        await _db.PutAsync("mission", mission);

        var got = await _db.GetAsync<MissionRow>("mission", new object[] { "EI1", "abc" });
        Assert.NotNull(got);
        Assert.Equal("abc", got!.MissionId);
        Assert.Equal(payload, got.CompletePayload);
        Assert.True(got.IsDubCap);
        Assert.False(got.IsBuggedCap);
        Assert.Equal(5, got.Ship);
        Assert.Equal(1700003600d, got.ReturnTimestamp);
    }

    [Fact]
    public async Task Mission_GetAllByPlayerIndex() {
        await _db.PutAsync("mission", Mission("EI1", "m1"));
        await _db.PutAsync("mission", Mission("EI1", "m2"));
        await _db.PutAsync("mission", Mission("EI2", "m3"));

        var rows = await _db.GetAllByIndexAsync<MissionRow>("mission", "player_id", "EI1");
        Assert.Equal(2, rows.Length);
        Assert.All(rows, r => Assert.Equal("EI1", r.PlayerId));
    }

    [Fact]
    public async Task ArtifactDrops_AutoIncrementInsert() {
        await _db.PutManyAsync("artifact_drops", new object[] {
            new ArtifactDropRow { MissionId = "m1", PlayerId = "EI1", DropIndex = 0, ArtifactId = 12, SpecType = "Artifact", Level = 1, Rarity = 0, Quality = 1.5 },
            new ArtifactDropRow { MissionId = "m1", PlayerId = "EI1", DropIndex = 1, ArtifactId = 13, SpecType = "Stone", Level = 2, Rarity = 1, Quality = 2.0 },
        });

        var all = await _db.GetAllAsync<ArtifactDropRow>("artifact_drops");
        Assert.Equal(2, all.Length);
        Assert.All(all, r => Assert.NotNull(r.Id));
        Assert.Contains(all, r => r.ArtifactId == 12 && r.Quality == 1.5);
    }

    [Fact]
    public async Task Reports_RoutedToReportDb_WithBoolAndNullableColumns() {
        var report = new ReportRow {
            Id = "r1",
            AccountId = "EI1",
            Name = "Test",
            Subject = "missions",
            Mode = "aggregate",
            DisplayMode = "chart",
            GroupBy = "ship_type",
            TimeBucket = null,
            CustomBucketN = null,
            CustomBucketUnit = null,
            MennoEnabled = true,
            CreatedAt = 100,
            UpdatedAt = 200,
        };
        await _db.PutAsync("reports", report);

        var got = await _db.GetAsync<ReportRow>("reports", "r1");
        Assert.NotNull(got);
        Assert.Equal("Test", got!.Name);
        Assert.True(got.MennoEnabled);
        Assert.Null(got.TimeBucket);

        var byAccount = await _db.GetAllByIndexAsync<ReportRow>("reports", "account_id", "EI1");
        Assert.Single(byAccount);
    }

    [Fact]
    public async Task Delete_And_Count_And_Clear() {
        await _db.PutAsync("settings", new SettingRow { Key = "a", Value = "1" });
        await _db.PutAsync("settings", new SettingRow { Key = "b", Value = "2" });
        Assert.Equal(2, await _db.CountAsync("settings"));

        await _db.DeleteAsync("settings", "a");
        Assert.Equal(1, await _db.CountAsync("settings"));

        await _db.ClearAsync("settings");
        Assert.Equal(0, await _db.CountAsync("settings"));
    }

    [Fact]
    public async Task ReportStore_OverSqlite_CrudWorks() {

        var store = new IndexedDbReportStore(_db, now: () => 1234);
        await store.InsertReportAsync(new ReportRow {
            Id = "r1",
            AccountId = "EI1",
            Name = "R1",
            Subject = "missions",
            Mode = "aggregate",
            DisplayMode = "chart",
            GroupBy = "ship_type",
            SortOrder = 0,
        });
        await store.InsertReportAsync(new ReportRow {
            Id = "r2",
            AccountId = "EI1",
            Name = "R2",
            Subject = "missions",
            Mode = "aggregate",
            DisplayMode = "chart",
            GroupBy = "ship_type",
            SortOrder = 1,
        });

        var list = await store.RetrieveAccountReportsAsync("EI1");
        Assert.Equal(2, list.Count);
        Assert.Equal("r1", list[0].Id);

        await store.DeleteReportAsync("r1");
        var after = await store.RetrieveAccountReportsAsync("EI1");
        Assert.Single(after);
        Assert.Equal("r2", after[0].Id);
    }

    [Fact]
    public async Task Backup_RoundTripTimestampAndPayload() {

        var payload = new byte[] { 9, 8, 7, 254, 255, 0 };
        await _db.PutAsync("backup", new BackupRow {
            PlayerId = "EI1",
            RecordedAt = 1700000000d,
            Payload = payload,
        });

        var got = await _db.GetAsync<BackupRow>("backup", "EI1");
        Assert.NotNull(got);
        Assert.Equal("EI1", got!.PlayerId);
        Assert.Equal(1700000000d, got.RecordedAt);
        Assert.Equal(payload, got.Payload);

        await _db.PutAsync("backup", new BackupRow {
            PlayerId = "EI1",
            RecordedAt = 1700000500d,
            Payload = [1],
        });
        Assert.Equal(1, await _db.CountAsync("backup"));
        got = await _db.GetAsync<BackupRow>("backup", "EI1");
        Assert.Equal(1700000500d, got!.RecordedAt);
    }

    [Fact]
    public async Task InsertBackup_RoundTripAndTwelveHourDedup() {

        var store = new IndexedDbMissionStore(_db, new EggLedger.Domain.Api.LocalApiPayloadDecoder(new EggLedger.Domain.Api.ApiClient()));
        var raw = new byte[] { 42, 7, 0, 255 };
        var gap = TimeSpan.FromHours(12);
        double t0 = 1_700_000_000d;

        await store.InsertBackupAsync("EI1", t0, raw, gap);
        var row = await _db.GetAsync<BackupRow>("backup", "EI1");
        Assert.NotNull(row);
        Assert.Equal(t0, row!.RecordedAt);
        Assert.Equal(raw, Gunzip(row.Payload));

        await store.InsertBackupAsync("EI1", t0 + 3600, [1, 2, 3], gap);
        row = await _db.GetAsync<BackupRow>("backup", "EI1");
        Assert.Equal(t0, row!.RecordedAt);
        Assert.Equal(raw, Gunzip(row.Payload));
        Assert.Equal(1, await _db.CountAsync("backup"));

        var later = new byte[] { 5, 6, 7, 8 };
        double t1 = t0 + (13 * 3600);
        await store.InsertBackupAsync("EI1", t1, later, gap);
        row = await _db.GetAsync<BackupRow>("backup", "EI1");
        Assert.Equal(t1, row!.RecordedAt);
        Assert.Equal(later, Gunzip(row.Payload));
        Assert.Equal(1, await _db.CountAsync("backup"));
    }

    private static byte[] Gunzip(byte[] data) {
        using var input = new MemoryStream(data, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static MissionRow Mission(string player, string mission) => new() {
        PlayerId = player,
        MissionId = mission,
        StartTimestamp = 1700000000,
        CompletePayload = [],
        Ship = 1,
        DurationType = 0,
        Level = 1,
        Capacity = 1,
        NominalCapacity = 1,
        Target = 0,
        ReturnTimestamp = 1700000000,
        MissionType = 0,
    };
}

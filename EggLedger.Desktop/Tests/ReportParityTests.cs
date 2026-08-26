using EggLedger.Desktop.Storage;
using EggLedger.Domain.Reports;
using Microsoft.Data.Sqlite;

namespace EggLedger.Desktop.Tests;

public sealed class ReportParityTests {
    private const string Eid = "EI1";

    private sealed class NoWeights : IWeightData {
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => Array.Empty<int>();
    }

    private sealed class FixedFamily : IWeightData {
        private readonly int[] _ids;
        public FixedFamily(params int[] ids) => _ids = ids;
        public double CraftingWeight(long artifactId, long level) => 1;
        public IReadOnlyList<int> FamilyAfxIds(string familyId) => _ids;
    }


    private static MissionRowData M(
        string id, int ship, int duration, long start, long ret,
        int cap = 1, int nominal = 1, int target = 0, int type = 0,
        int level = 0, bool dub = false, bool bugged = false) => new() {
            PlayerId = Eid,
            MissionId = id,
            Ship = ship,
            DurationType = duration,
            Level = level,
            Target = target,
            MissionType = type,
            StartTimestamp = start,
            ReturnTimestamp = ret,
            Capacity = cap,
            NominalCapacity = nominal,
            IsDubCap = dub,
            IsBuggedCap = bugged,
        };

    private static ArtifactDropRowData D(
        string mission, int artifactId, int rarity, int tier,
        int dropIndex = 0, double quality = 0, string spec = "Artifact") => new() {
            PlayerId = Eid,
            MissionId = mission,
            DropIndex = dropIndex,
            ArtifactId = artifactId,
            Rarity = rarity,
            Level = tier,
            Quality = quality,
            SpecType = spec,
        };



    private static List<MissionRowData> Missions() =>
    [


        M("m1", ship: 1, duration: 0, start: 1758100000, ret: 1758100000 + 3600, cap: 10, nominal: 5, type: 0, level: 1),
        M("m2", ship: 1, duration: 0, start: 1758200000, ret: 1758200000 + 7200, cap: 10, nominal: 5, type: 0, level: 1),
        M("m3", ship: 1, duration: 1, start: 1761000000, ret: 1761000000 + 3600, cap: 8, nominal: 4, type: 1, level: 2),
        M("m4", ship: 2, duration: 0, start: 1758300000, ret: 1758300000 + 1800, cap: 6, nominal: 6, type: 0, level: 0),
        M("m5", ship: 2, duration: 1, start: 1763600000, ret: 1763600000 + 3600, cap: 6, nominal: 3, type: 0, level: 1),
        M("m6", ship: 3, duration: 2, start: 1766300000, ret: 1766300000 + 14400, cap: 4, nominal: 2, type: 1, level: 3),
    ];

    private static List<ArtifactDropRowData> Drops() =>
    [

        D("m1", artifactId: 12, rarity: 0, tier: 1, dropIndex: 0, quality: 1.5, spec: "Artifact"),
        D("m1", artifactId: 13, rarity: 1, tier: 2, dropIndex: 1, quality: 2, spec: "Stone"),

        D("m2", artifactId: 12, rarity: 1, tier: 1, dropIndex: 0, quality: 3.25, spec: "Artifact"),

        D("m3", artifactId: 14, rarity: 3, tier: 2, dropIndex: 0, quality: 0.75, spec: "Artifact"),

        D("m4", artifactId: 13, rarity: 0, tier: 1, dropIndex: 0, quality: 0, spec: "Stone"),

        D("m5", artifactId: 0, rarity: 0, tier: 0, dropIndex: -1, quality: 0, spec: ""),

        D("m6", artifactId: 12, rarity: 2, tier: 3, dropIndex: 0, quality: 2.5, spec: "Artifact"),
        D("m6", artifactId: 14, rarity: 2, tier: 1, dropIndex: 1, quality: 1.25, spec: "Artifact"),
    ];

    private static SqliteConnection SeedSqlite(IReadOnlyList<MissionRowData> missions, IReadOnlyList<ArtifactDropRowData> drops) {
        var name = "parity_" + Guid.NewGuid().ToString("N");
        var conn = new SqliteConnection($"Data Source={name};Mode=Memory;Cache=Shared");
        conn.Open();
        SqliteMigrationRunner.MigrateMissionDb(conn);

        using (var tx = conn.BeginTransaction()) {
            foreach (var m in missions) {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    @"INSERT INTO mission
                      (player_id, mission_id, start_timestamp, complete_payload, mission_type,
                       ship, duration_type, level, capacity, nominal_capacity, is_dub_cap, is_bugged_cap, target, return_timestamp)
                      VALUES (@pid, @mid, @start, @payload, @type, @ship, @dur, @lvl, @cap, @nom, @dub, @bug, @target, @ret);";
                cmd.Parameters.AddWithValue("@pid", m.PlayerId);
                cmd.Parameters.AddWithValue("@mid", m.MissionId);
                cmd.Parameters.AddWithValue("@start", m.StartTimestamp);
                cmd.Parameters.AddWithValue("@payload", Array.Empty<byte>());
                cmd.Parameters.AddWithValue("@type", m.MissionType);
                cmd.Parameters.AddWithValue("@ship", m.Ship);
                cmd.Parameters.AddWithValue("@dur", m.DurationType);
                cmd.Parameters.AddWithValue("@lvl", m.Level);
                cmd.Parameters.AddWithValue("@cap", m.Capacity);
                cmd.Parameters.AddWithValue("@nom", m.NominalCapacity);
                cmd.Parameters.AddWithValue("@dub", m.IsDubCap ? 1 : 0);
                cmd.Parameters.AddWithValue("@bug", m.IsBuggedCap ? 1 : 0);
                cmd.Parameters.AddWithValue("@target", m.Target);
                cmd.Parameters.AddWithValue("@ret", m.ReturnTimestamp);
                cmd.ExecuteNonQuery();
            }
            foreach (var d in drops) {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    @"INSERT INTO artifact_drops
                      (mission_id, player_id, drop_index, artifact_id, spec_type, level, rarity, quality)
                      VALUES (@mid, @pid, @idx, @aid, @spec, @lvl, @rar, @q);";
                cmd.Parameters.AddWithValue("@mid", d.MissionId);
                cmd.Parameters.AddWithValue("@pid", d.PlayerId);
                cmd.Parameters.AddWithValue("@idx", d.DropIndex);
                cmd.Parameters.AddWithValue("@aid", d.ArtifactId);
                cmd.Parameters.AddWithValue("@spec", d.SpecType);
                cmd.Parameters.AddWithValue("@lvl", d.Level);
                cmd.Parameters.AddWithValue("@rar", d.Rarity);
                cmd.Parameters.AddWithValue("@q", d.Quality);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        return conn;
    }

    private static void AssertParity(ReportDefinition def, IWeightData weights) {
        using var conn = SeedSqlite(Missions(), Drops());
        var sqlDb = new SqliteMissionDb(conn);
        var sqlResult = new ReportExecutor(sqlDb, weights).ExecuteReport(def);

        var source = SqliteReportSource.Load(sqlDb, Eid);
        var memResult = new InMemoryReportRunner(weights).Run(def, source.Missions, source.Drops);

        Assert.Equal(memResult, sqlResult);
    }

    [Fact]
    public void Parity_Aggregate1D_ByShip() {
        AssertParity(
            new ReportDefinition { Mode = "aggregate", GroupBy = "ship_type", Subject = "missions", AccountId = Eid },
            new NoWeights());
    }

    [Fact]
    public void Parity_Aggregate1D_WithMissionFilter() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "duration", Op = "=", Val = "0" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_Pivot2D_ShipByDuration() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                SecondaryGroupBy = "duration_type",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_DropsSubject_ByRarity() {
        AssertParity(
            new ReportDefinition { Mode = "aggregate", GroupBy = "rarity", Subject = "artifacts", AccountId = Eid },
            new NoWeights());
    }

    [Fact]
    public void Parity_DropsExistsSubquery_Contains() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "drops", Op = "c", Val = "%_%_2_%" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_DropsExistsSubquery_DoesNotContain() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "drops", Op = "dnc", Val = "%_%_0_%" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_TimeSeries_ByMonth() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                TimeBucket = "month",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_Normalized_Launches() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                NormalizeBy = "launches",
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_Normalized_Airtime() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                NormalizeBy = "airtime",
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_FamilyWeighted_Aggregate() {
        AssertParity(
            new ReportDefinition {
                Subject = "artifacts",
                Mode = "aggregate",
                GroupBy = "ship_type",
                FamilyWeight = "tachyon-stone",
                AccountId = Eid,
            },
            new FixedFamily(12, 13));
    }

    [Fact]
    public void Parity_TimePivot_MonthByShip() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                SecondaryGroupBy = "ship_type",
                TimeBucket = "month",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_DateFilter_LaunchOnOrAfter() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "duration_type",
                Subject = "missions",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = "2025-10-01" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_DateFilter_ReturnBefore() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "returnDT", Op = "<", Val = "2025-10-01" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_DateFilter_LaunchWindow() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                Subject = "missions",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [
                        new FilterCondition { TopLevel = "launchDT", Op = ">=", Val = "2025-09-18" },
                        new FilterCondition { TopLevel = "launchDT", Op = "<=", Val = "2025-11-01" },
                    ],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_TimeSeries_ByWeek() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                TimeBucket = "week",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_TimePivot_WeekByShip() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                SecondaryGroupBy = "ship_type",
                TimeBucket = "week",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_CustomBucket_WindowCoversAllRows() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                TimeBucket = "custom",
                CustomBucketN = 600,
                CustomBucketUnit = "month",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_CustomBucket_WindowExcludesAllRows() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                TimeBucket = "custom",
                CustomBucketN = 1,
                CustomBucketUnit = "day",
                Subject = "missions",
                AccountId = Eid,
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_SpecTypeGrouping() {
        AssertParity(
            new ReportDefinition { Mode = "aggregate", GroupBy = "spec_type", Subject = "artifacts", AccountId = Eid },
            new NoWeights());
    }

    [Fact]
    public void Parity_QualityFilter_AtOrAboveThreshold() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "rarity",
                Subject = "artifacts",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "artifact_quality", Op = ">=", Val = "2" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_QualityFilter_BelowThreshold_BySpecType() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "spec_type",
                Subject = "artifacts",
                AccountId = Eid,
                Filters = new ReportFilters {
                    And = [new FilterCondition { TopLevel = "artifact_quality", Op = "<", Val = "2" }],
                },
            },
            new NoWeights());
    }

    [Fact]
    public void Parity_Pivot2D_RowPct() {
        AssertParity(Pivot2D("row_pct"), new NoWeights());
    }

    [Fact]
    public void Parity_Pivot2D_ColPct() {
        AssertParity(Pivot2D("col_pct"), new NoWeights());
    }

    [Fact]
    public void Parity_Pivot2D_GlobalPct() {
        AssertParity(Pivot2D("global_pct"), new NoWeights());
    }

    [Fact]
    public void Parity_FamilyWeighted_Pivot() {
        AssertParity(
            new ReportDefinition {
                Mode = "aggregate",
                GroupBy = "ship_type",
                SecondaryGroupBy = "duration_type",
                Subject = "artifacts",
                FamilyWeight = "tachyon-stone",
                AccountId = Eid,
            },
            new FixedFamily(12, 13));
    }

    [Fact]
    public void Parity_FamilyWeighted_TimeSeries() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                TimeBucket = "month",
                Subject = "artifacts",
                FamilyWeight = "tachyon-stone",
                AccountId = Eid,
            },
            new FixedFamily(12, 13));
    }

    [Fact]
    public void Parity_FamilyWeighted_TimePivot() {
        AssertParity(
            new ReportDefinition {
                Mode = "time_series",
                GroupBy = "time_bucket",
                SecondaryGroupBy = "ship_type",
                TimeBucket = "month",
                Subject = "artifacts",
                FamilyWeight = "tachyon-stone",
                AccountId = Eid,
            },
            new FixedFamily(12, 13));
    }

    private static ReportDefinition Pivot2D(string normalizeBy) => new() {
        Mode = "aggregate",
        GroupBy = "ship_type",
        SecondaryGroupBy = "duration_type",
        Subject = "missions",
        AccountId = Eid,
        NormalizeBy = normalizeBy,
    };

    [Fact]
    public void SqliteReportSource_LoadsSeededRowsForTheAccountOnly() {
        var missions = Missions();
        var drops = Drops();
        missions.Add(M("z1", ship: 1, duration: 0, start: 1758100000, ret: 1758100100) with { PlayerId = "EI2" });
        drops.Add(D("z1", artifactId: 99, rarity: 3, tier: 1, dropIndex: 0) with { PlayerId = "EI2" });

        using var conn = SeedSqlite(missions, drops);
        var source = SqliteReportSource.Load(new SqliteMissionDb(conn), Eid);

        Assert.Equal(Missions(), source.Missions);
        Assert.Equal(Drops(), source.Drops);
    }

    [Fact]
    public void SqliteReportSource_ConvertsStoredColumnTypes() {
        var missions = new List<MissionRowData> {
            M("x1", ship: 4, duration: 2, start: 1758100000, ret: 1758103600,
                cap: 12, nominal: 6, target: 3, type: 1, level: 5, dub: true, bugged: true),
        };
        var drops = new List<ArtifactDropRowData> {
            D("x1", artifactId: 21, rarity: 2, tier: 3, dropIndex: 1, quality: 4.25, spec: "Stone"),
        };

        using var conn = SeedSqlite(missions, drops);
        var source = SqliteReportSource.Load(new SqliteMissionDb(conn), Eid);

        var mission = Assert.Single(source.Missions);
        Assert.Equal(missions[0], mission);
        Assert.Equal(1758100000, mission.StartTimestamp);
        Assert.Equal(1758103600, mission.ReturnTimestamp);
        Assert.True(mission.IsDubCap);
        Assert.True(mission.IsBuggedCap);

        var drop = Assert.Single(source.Drops);
        Assert.Equal(drops[0], drop);
        Assert.Equal(4.25, drop.Quality);
        Assert.Equal("Stone", drop.SpecType);
    }
}

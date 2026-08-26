using System.Text.Json;
using EggLedger.Web.Data;

namespace EggLedger.Web.Tests.Data;

public sealed class RowsTests {
    [Fact]
    public void MissionRow_serializes_with_snake_case_keys() {
        var row = new MissionRow {
            PlayerId = "EI1",
            MissionId = "m1",
            StartTimestamp = 1.5,
            CompletePayload = [1, 2, 3],
            MissionType = 0,
            Ship = 4,
            DurationType = 2,
            Level = 5,
            Capacity = 100,
            IsDubCap = false,
            IsBuggedCap = false,
            Target = 7,
            ReturnTimestamp = 2.5,
            NominalCapacity = 99,
        };

        var json = JsonSerializer.Serialize(row, Rows.JsonOptions);

        Assert.Contains("\"player_id\":\"EI1\"", json);
        Assert.Contains("\"is_dub_cap\":false", json);
        Assert.Contains("\"complete_payload\":\"AQID\"", json);
        Assert.Contains("\"nominal_capacity\":99", json);
    }

    [Fact]
    public void ArtifactDropRow_null_id_omits_id_key() {
        var row = new ArtifactDropRow {
            Id = null,
            MissionId = "m1",
            PlayerId = "EI1",
            DropIndex = 0,
            ArtifactId = 12,
            SpecType = "x",
            Level = 1,
            Rarity = 2,
            Quality = 3.0,
        };

        var json = JsonSerializer.Serialize(row, Rows.JsonOptions);

        Assert.DoesNotContain("\"id\"", json);
    }

    [Fact]
    public void ArtifactDropRow_non_null_id_emits_id_key() {
        var row = new ArtifactDropRow {
            Id = 42,
            MissionId = "m1",
            PlayerId = "EI1",
            DropIndex = 0,
            ArtifactId = 12,
            SpecType = "x",
            Level = 1,
            Rarity = 2,
            Quality = 3.0,
        };

        var json = JsonSerializer.Serialize(row, Rows.JsonOptions);

        Assert.Contains("\"id\":42", json);
    }

    [Fact]
    public void ReportRow_round_trips_with_nullable_and_filters() {
        var row = new ReportRow {
            Id = "r1",
            AccountId = "acc1",
            Name = "Test",
            Subject = "drops",
            Mode = "count",
            DisplayMode = "table",
            GroupBy = "ship",
            TimeBucket = null,
            CustomBucketN = null,
            CustomBucketUnit = null,
            Filters = "{\"and\":[],\"or\":[]}",
            CreatedAt = 1000,
            UpdatedAt = 2000,
        };

        var json = JsonSerializer.Serialize(row, Rows.JsonOptions);
        var back = JsonSerializer.Deserialize<ReportRow>(json, Rows.JsonOptions);

        Assert.NotNull(back);
        Assert.Equal("r1", back!.Id);
        Assert.Equal("acc1", back.AccountId);
        Assert.Null(back.TimeBucket);
        Assert.Equal("{\"and\":[],\"or\":[]}", back.Filters);
        Assert.Equal(1000, back.CreatedAt);
        Assert.Equal(2000, back.UpdatedAt);
        Assert.Contains("\"time_bucket\":null", json);
    }
}

using EggLedger.Domain.MissionPacking;
using Ei;

namespace EggLedger.Domain.Tests;

public class MissionPackerTests {
    private static readonly MissionPacker Packer = new(new EmbeddedMissionConfigSource());

    private static CompleteMissionResponse MakeTimestampMission(double startTimeDerived) =>
        new() { Info = new MissionInfo { StartTimeDerived = startTimeDerived } };

    private static float Nominal(MissionInfo.Spaceship ship, MissionInfo.DurationType dur, int level) {
        Assert.True(Packer.TryGetShipCapacities(ship, dur, out var caps));
        return caps[level];
    }

    [Fact]
    public void CompileInFlightMission_MapsCoreFields() {
        var info = new MissionInfo {
            Identifier = "flying-1",
            StartTimeDerived = 1_700_000_000,
            DurationSeconds = 3600,
            Ship = MissionInfo.Spaceship.ChickenOne,
            duration_type = MissionInfo.DurationType.Short,
            Level = 0,
            Capacity = 4,
            status = MissionInfo.Status.Exploring,
            TargetArtifact = ArtifactSpec.Name.BookOfBasan,
        };

        var mission = Packer.CompileInFlightMission(info);

        Assert.Equal("flying-1", mission.MissiondId);
        Assert.Equal(1_700_000_000, mission.LaunchDT);
        Assert.Equal(1_700_003_600, mission.ReturnDT);
        Assert.Equal(MissionInfo.Spaceship.ChickenOne, mission.Ship);
        Assert.Equal(MissionInfo.DurationType.Short, mission.DurationType);
        Assert.Equal(4, mission.Capacity);
        Assert.Equal("BOOK_OF_BASAN", mission.Target);
        Assert.Equal((int)ArtifactSpec.Name.BookOfBasan, mission.TargetInt);
        Assert.Equal("CHICKEN_ONE", mission.ShipEnumString);
    }

    [Fact]
    public void CompileInFlightMission_NoTarget_HasEmptyTargetAndMinusOne() {
        var info = new MissionInfo {
            Identifier = "flying-2",
            StartTimeDerived = 1_700_000_000,
            DurationSeconds = 60,
            Ship = MissionInfo.Spaceship.ChickenOne,
            duration_type = MissionInfo.DurationType.Short,
        };

        var mission = Packer.CompileInFlightMission(info);

        Assert.Equal("", mission.Target);
        Assert.Equal(-1, mission.TargetInt);
    }

    [Fact]
    public void IsBuggedCap_InsideRange() {
        Assert.True(Packer.IsBuggedCap(MakeTimestampMission(1712900000)));
    }

    [Fact]
    public void IsBuggedCap_BeforeRange() {
        Assert.False(Packer.IsBuggedCap(MakeTimestampMission(1712721599)));
    }

    [Fact]
    public void IsBuggedCap_AfterRange() {
        Assert.False(Packer.IsBuggedCap(MakeTimestampMission(1713286801)));
    }

    [Fact]
    public void IsBuggedCap_AtLowerBound() {
        Assert.False(Packer.IsBuggedCap(MakeTimestampMission(1712721600)));
    }

    [Fact]
    public void IsBuggedCap_AtUpperBound() {
        Assert.False(Packer.IsBuggedCap(MakeTimestampMission(1713286800)));
    }

    private static CompleteMissionResponse MakeDubCapMission(
        MissionInfo.Spaceship ship, MissionInfo.DurationType dur, uint level, uint capacity) =>
        new() {
            Info = new MissionInfo {
                Ship = ship,
                duration_type = dur,
                Level = level,
                Capacity = capacity,
            },
        };

    [Fact]
    public void IsDubCap_AboveThreshold() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        var nominal = Nominal(ship, dur, 0);
        Assert.True(Packer.IsDubCap(MakeDubCapMission(ship, dur, 0, (uint)(nominal * 2.0f))));
    }

    [Fact]
    public void IsDubCap_BelowThreshold() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        var nominal = Nominal(ship, dur, 0);
        Assert.False(Packer.IsDubCap(MakeDubCapMission(ship, dur, 0, (uint)(nominal * 1.5f))));
    }

    [Fact]
    public void IsDubCap_AtThreshold() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        var nominal = Nominal(ship, dur, 0);
        Assert.True(Packer.IsDubCap(MakeDubCapMission(ship, dur, 0, (uint)(nominal * 1.8f))));
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(30, "30s")]
    [InlineData(90, "1m")]
    [InlineData((3 * 3600) + (30 * 60), "3h30m")]
    [InlineData((2 * 86400) + (6 * 3600) + (45 * 60), "2d6h45m")]
    public void DurationStringFromSecs(double secs, string want) {
        Assert.Equal(want, MissionPacker.DurationStringFromSecs(secs));
    }

    private static CompleteMissionResponse MakeFullMissionResponse(
        MissionInfo.Spaceship ship, MissionInfo.DurationType dur, uint level, uint capacity, double durSecs) =>
        new() {
            Success = true,
            Info = new MissionInfo {
                Ship = ship,
                duration_type = dur,
                Level = level,
                Capacity = capacity,
                DurationSeconds = durSecs,
            },
        };

    [Fact]
    public void ComputeMissionFilterCols_NilInfo() {
        Assert.False(Packer.TryComputeMissionFilterCols(1000000, new CompleteMissionResponse(), out _));
    }

    [Fact]
    public void ComputeMissionFilterCols_Normal() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        var nominal = Nominal(ship, dur, 0);
        var capacity = (uint)nominal;
        const double startTs = 1000000;

        var resp = MakeFullMissionResponse(ship, dur, 0, capacity, 300);
        Assert.True(Packer.TryComputeMissionFilterCols(startTs, resp, out var cols));

        Assert.Equal((int)ship, cols.Ship);
        Assert.Equal((int)dur, cols.DurationType);
        Assert.Equal(0, cols.Level);
        Assert.Equal((int)capacity, cols.Capacity);
        Assert.False(cols.IsDubCap);
        Assert.False(cols.IsBuggedCap);
        Assert.Equal(-1, cols.Target);
        Assert.Equal(startTs + 300, cols.ReturnTimestamp);
    }

    [Fact]
    public void ComputeMissionFilterCols_DubCap() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        var nominal = Nominal(ship, dur, 0);
        var capacity = (uint)(nominal * 2.0f);

        var resp = MakeFullMissionResponse(ship, dur, 0, capacity, 300);
        Assert.True(Packer.TryComputeMissionFilterCols(1000000, resp, out var cols));
        Assert.True(cols.IsDubCap);
    }

    [Fact]
    public void ComputeMissionFilterCols_BuggedCap() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        const double startTs = 1712900000;

        var resp = MakeFullMissionResponse(ship, dur, 0, 6, 300);
        Assert.True(Packer.TryComputeMissionFilterCols(startTs, resp, out var cols));
        Assert.True(cols.IsBuggedCap);
    }

    [Fact]
    public void ComputeMissionFilterCols_WithTarget() {
        var ship = MissionInfo.Spaceship.ChickenOne;
        var dur = MissionInfo.DurationType.Short;
        var targetArtifact = ArtifactSpec.Name.BookOfBasan;

        var resp = MakeFullMissionResponse(ship, dur, 0, 6, 300);
        resp.Info!.TargetArtifact = targetArtifact;

        Assert.True(Packer.TryComputeMissionFilterCols(1000000, resp, out var cols));
        Assert.Equal((int)targetArtifact, cols.Target);
    }

    [Fact]
    public void MissionMetaToDBMission_Basic() {
        var meta = new MissionMeta {
            MissionId = "m1",
            StartTimestamp = 1000000,
            ReturnTimestamp = 1000300,
            Ship = (int)MissionInfo.Spaceship.ChickenOne,
            DurationType = (int)MissionInfo.DurationType.Short,
            Level = 0,
            Capacity = 6,
            IsDubCap = false,
            IsBuggedCap = false,
            Target = -1,
            MissionType = (int)MissionInfo.MissionType.Standard,
        };

        var dm = Packer.MissionMetaToDBMission(meta);

        Assert.Equal(1000000, dm.LaunchDT);
        Assert.Equal(1000300, dm.ReturnDT);
        Assert.Equal("5m", dm.DurationString);
        Assert.Equal("m1", dm.MissiondId);
        Assert.Equal(0, dm.Level);
        Assert.Equal(6, dm.Capacity);
        Assert.False(dm.IsDubCap);
        Assert.Equal(-1, dm.TargetInt);
        Assert.Equal("", dm.Target);
        Assert.Equal("Home", dm.MissionTypeString);
        Assert.False(string.IsNullOrEmpty(dm.ShipString));
        Assert.NotNull(dm.Ship);
    }

    [Fact]
    public void MissionMetaToDBMission_WithTarget() {
        int target = (int)ArtifactSpec.Name.BookOfBasan;
        var meta = new MissionMeta {
            MissionId = "m2",
            StartTimestamp = 1000000,
            ReturnTimestamp = 1000300,
            Ship = (int)MissionInfo.Spaceship.ChickenOne,
            DurationType = (int)MissionInfo.DurationType.Short,
            Level = 0,
            Capacity = 6,
            Target = target,
            MissionType = (int)MissionInfo.MissionType.Standard,
        };

        var dm = Packer.MissionMetaToDBMission(meta);

        Assert.Equal(target, dm.TargetInt);
        Assert.False(string.IsNullOrEmpty(dm.Target));
    }

    [Fact]
    public void MissionMetaToDBMission_VirtueMissionType() {
        var meta = new MissionMeta {
            MissionId = "m3",
            StartTimestamp = 1000000,
            ReturnTimestamp = 1000300,
            Ship = (int)MissionInfo.Spaceship.ChickenOne,
            DurationType = (int)MissionInfo.DurationType.Short,
            Level = 0,
            Capacity = 6,
            Target = -1,
            MissionType = (int)MissionInfo.MissionType.Virtue,
        };

        var dm = Packer.MissionMetaToDBMission(meta);
        Assert.Equal("Virtue", dm.MissionTypeString);
    }

    [Fact]
    public void CompileMissionInformation_NoMissionTypeField() {
        var resp = new CompleteMissionResponse {
            Info = new MissionInfo {
                Ship = MissionInfo.Spaceship.ChickenOne,
                duration_type = MissionInfo.DurationType.Short,
                Level = 0,
                Capacity = 6,
                StartTimeDerived = 1000000,
                DurationSeconds = 300,
                Identifier = "test-no-type",

            },
        };

        var dm = Packer.CompileMissionInformation(resp);
        Assert.Equal(0, dm.MissionType);
    }

    [Fact]
    public void CompileMissionInformation_NilInfo() {
        var dm = Packer.CompileMissionInformation(new CompleteMissionResponse());
        Assert.Equal("", dm.MissiondId);
        Assert.Null(dm.Ship);
    }
}

using System.IO.Compression;
using System.Text;
using EggLedger.Domain.Export;
using Ei;

namespace EggLedger.Domain.Tests.Export;

public class ExportTests {
    private static List<Mission> TestMissions() =>
    [
        new Mission {
            Id = "test-uuid-001",
            TypeName = "Standard",
            ShipName = "Chicken One",
            DurationTypeName = "Short",
            Level = 1,
            LaunchedAt = new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero),
            LaunchedAtStr = "2023-01-01T12:00:00Z",
            ReturnedAt = new DateTimeOffset(2023, 1, 1, 14, 0, 0, TimeSpan.Zero),
            ReturnedAtStr = "2023-01-01T14:00:00Z",
            DurationDays = 2.0 / 24.0,
            Capacity = 50,
            TargetArtifact = ArtifactSpec.Name.Unknown,
            ArtifactNames = ["Book of Basan (T4)", "Lunar Totem (T1)"],
        },
        new Mission {
            Id = "test-uuid-002",
            TypeName = "Standard",
            ShipName = "Chicken Nine",
            DurationTypeName = "Epic",
            Level = 8,
            LaunchedAt = new DateTimeOffset(2023, 6, 15, 0, 0, 0, TimeSpan.Zero),
            LaunchedAtStr = "2023-06-15T00:00:00Z",
            ReturnedAt = new DateTimeOffset(2023, 6, 22, 0, 0, 0, TimeSpan.Zero),
            ReturnedAtStr = "2023-06-22T00:00:00Z",
            DurationDays = 7.0,
            Capacity = 400,
            TargetArtifact = ArtifactSpec.Name.BookOfBasan,
            ArtifactNames = ["Interstellar Compass (T4)"],
        },
    ];

    private static List<string[]> ParseCsv(byte[] bytes) {

        var text = Encoding.UTF8.GetString(bytes);
        return [.. text.Split("\r\n").Where(line => line.Length != 0).Select(SplitCsvLine)];
    }

    private static string[] SplitCsvLine(string line) {
        List<string> fields = [];
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++) {
            char c = line[i];
            if (inQuotes) {
                if (c == '"') {
                    if (i + 1 < line.Length && line[i + 1] == '"') {
                        sb.Append('"');
                        i++;
                    } else {
                        inQuotes = false;
                    }
                } else {
                    sb.Append(c);
                }
            } else if (c == '"') {
                inQuotes = true;
            } else if (c == ',') {
                fields.Add(sb.ToString());
                sb.Clear();
            } else {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return [.. fields];
    }

    private static string ReadZipEntry(byte[] data, string name) {
        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry(name);
        if (entry is null) {
            return "";
        }
        using var s = entry.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    [Fact]
    public void ExportMissionsToCsv_Structure() {
        var records = ParseCsv(MissionExport.MissionsToCsvBytes(TestMissions()));
        Assert.Equal(3, records.Count);

        var header = records[0];
        Assert.Equal(12, header.Length);
        string[] wantHeaders = [
            "ID", "Type", "Ship", "Duration Type", "Level",
            "Launched at", "Returned at", "Duration days", "Capacity", "Target",
            "Artifact 1", "Artifact 2",
        ];
        for (int i = 0; i < wantHeaders.Length; i++) {
            Assert.Equal(wantHeaders[i], header[i]);
        }

        var row1 = records[1];
        Assert.Equal("test-uuid-001", row1[0]);
        Assert.Equal("Book of Basan (T4)", row1[10]);
        Assert.Equal("Lunar Totem (T1)", row1[11]);

        var row2 = records[2];
        Assert.Equal("", row2[11]);
    }

    [Theory]
    [InlineData(0, "Standard")]
    [InlineData(1, "Virtue")]
    [InlineData(-1, "Unknown")]
    [InlineData(99, "Unknown")]
    public void MissionTypeName_Cases(int input, string want) =>
        Assert.Equal(want, Mission.MissionTypeName(input));

    [Fact]
    public void ExportMissionsToCsv_UnknownMissionType() {
        var missions = new List<Mission> {
            new() {
                Id = "test-unknown-type",
                TypeName = Mission.MissionTypeName(-1),
                ShipName = "Chicken One",
                DurationTypeName = "Short",
                Level = 0,
                LaunchedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                LaunchedAtStr = "2024-01-01T00:00:00Z",
                ReturnedAt = new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero),
                ReturnedAtStr = "2024-01-01T01:00:00Z",
                DurationDays = 1.0 / 24.0,
                Capacity = 6,
                TargetArtifact = ArtifactSpec.Name.Unknown,
                ArtifactNames = [],
            },
        };

        var records = ParseCsv(MissionExport.MissionsToCsvBytes(missions));
        Assert.Equal(2, records.Count);
        Assert.Equal("Unknown", records[1][1]);
    }

    [Fact]
    public void ExportMissionsToXlsx_Structure() {
        var data = MissionExport.MissionsToXlsxBytes(TestMissions());
        var sheetXml = ReadZipEntry(data, "xl/worksheets/sheet1.xml");
        Assert.NotEqual("", sheetXml);

        string[] wantHeaders = [
            "ID", "Type", "Ship", "Duration Type", "Level",
            "Launched at", "Returned at", "Duration days", "Capacity", "Target",
        ];
        foreach (var h in wantHeaders) {
            Assert.Contains("<t>" + h + "</t>", sheetXml, StringComparison.Ordinal);
        }
        Assert.Contains("<t>Artifact 2</t>", sheetXml, StringComparison.Ordinal);
        Assert.Contains("s=\"1\"", sheetXml, StringComparison.Ordinal);
        Assert.Contains("test-uuid-001", sheetXml, StringComparison.Ordinal);
    }
}

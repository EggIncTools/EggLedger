using System.IO.Compression;
using System.Text;
using EggLedger.Domain.Export.Xlsx;

namespace EggLedger.Domain.Tests.Export;

public class XlsxWriterTests {
    private static byte[] BuildXlsx() {
        using var ms = new MemoryStream();
        using (var w = XlsxWriter.New(ms)) {
            w.SetColWidths(new[] { 20.0, 22.0, 15.0 });
            w.WriteRow(new[]
            {
                XlsxCell.String("ID"),
                XlsxCell.String("Launched at"),
                XlsxCell.String("Duration days"),
            });
            var ts = new DateTimeOffset(2023, 5, 15, 14, 30, 0, TimeSpan.Zero);
            w.WriteRow(new[]
            {
                XlsxCell.String("abc<>&123"),
                XlsxCell.Datetime(ts),
                XlsxCell.Number(0.25),
            });
            w.Close();
        }
        return ms.ToArray();
    }

    private static Dictionary<string, string> OpenXlsx(byte[] data) {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries) {
            using var s = entry.Open();
            using var r = new StreamReader(s, Encoding.UTF8);
            contents[entry.FullName] = r.ReadToEnd();
        }
        return contents;
    }

    [Fact]
    public void Writer_ZipEntries() {
        var contents = OpenXlsx(BuildXlsx());
        string[] required =
        [
            "[Content_Types].xml",
            "_rels/.rels",
            "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels",
            "xl/styles.xml",
            "xl/worksheets/sheet1.xml",
        ];
        foreach (var name in required) {
            Assert.True(contents.ContainsKey(name), "missing required ZIP entry: " + name);
        }
    }

    [Fact]
    public void Writer_SheetStructure() {
        var sheet = OpenXlsx(BuildXlsx())["xl/worksheets/sheet1.xml"];
        string[] wants =
        [
            "<cols>",
            "width=\"20.00\"",
            "<sheetData>",
            "<row r=\"1\">",
            "<row r=\"2\">",
            "r=\"A1\"",
            "r=\"B2\"",
        ];
        foreach (var w in wants) {
            Assert.Contains(w, sheet, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Writer_StringCell() {
        var sheet = OpenXlsx(BuildXlsx())["xl/worksheets/sheet1.xml"];
        Assert.Contains("t=\"inlineStr\"", sheet, StringComparison.Ordinal);
        Assert.Contains("<t>ID</t>", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_XmlEscaping() {
        var sheet = OpenXlsx(BuildXlsx())["xl/worksheets/sheet1.xml"];
        Assert.DoesNotContain("abc<>&123", sheet, StringComparison.Ordinal);
        Assert.Contains("abc&lt;&gt;&amp;123", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_DatetimeCell() {
        var sheet = OpenXlsx(BuildXlsx())["xl/worksheets/sheet1.xml"];
        Assert.Contains("s=\"1\"", sheet, StringComparison.Ordinal);
        Assert.Contains("45061", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_NumberCell() {
        var sheet = OpenXlsx(BuildXlsx())["xl/worksheets/sheet1.xml"];
        Assert.Contains("<v>0.25</v>", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_Styles() {
        var styles = OpenXlsx(BuildXlsx())["xl/styles.xml"];
        Assert.Contains("Consolas", styles, StringComparison.Ordinal);
        Assert.Contains("yyyy-mm-dd hh:mm:ss", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_EmptySheet() {
        using var ms = new MemoryStream();
        using (var w = XlsxWriter.New(ms)) {
            w.Close();
        }
        var bytes = ms.ToArray();
        using var read = new MemoryStream(bytes);

        using var zip = new ZipArchive(read, ZipArchiveMode.Read);
        Assert.NotEmpty(zip.Entries);
    }

    [Theory]
    [InlineData(0.25, "0.25")]
    [InlineData(7.0, "7")]
    [InlineData(45061.604166666664, "45061.604166666664")]
    public void GoFloat_FormatF_Matches(double v, string want) {
        Assert.Equal(want, Domain.Export.GoFloat.FormatF(v));
    }
}

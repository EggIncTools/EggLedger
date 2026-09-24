using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace EggLedger.Domain.Export.Xlsx;

public sealed class XlsxWriter : IDisposable {
    private readonly ZipArchive _zip;
    private double[] _colWidths = [];
    private int _rowNum;
    private bool _sheetStarted;
    private StringBuilder? _sheet;
    private bool _closed;

    private XlsxWriter(Stream output) {
        _zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
    }

    public static XlsxWriter New(Stream output) {
        var w = new XlsxWriter(output);
        w.WriteStaticEntries();
        return w;
    }

    public void SetColWidths(IReadOnlyList<double> widths) => _colWidths = [.. widths];

    public void WriteRow(IReadOnlyList<XlsxCell> cells) {
        if (!_sheetStarted) {
            StartSheet();
            _sheetStarted = true;
        }
        _rowNum++;
        var buf = _sheet!;
        buf.Append(CultureInfo.InvariantCulture, $"""<row r="{_rowNum}">""");
        for (int col = 0; col < cells.Count; col++) {
            var cell = cells[col];
            string r = XlsxCell.CellRef(col + 1, _rowNum);
            if (cell.IsNum) {
                string s = GoFloat.FormatF(cell.NumVal);
                if (cell.Style != XlsxStyle.None) {
                    buf.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" s="{(int)cell.Style}"><v>{s}</v></c>""");
                } else {
                    buf.Append(CultureInfo.InvariantCulture, $"""<c r="{r}"><v>{s}</v></c>""");
                }
            } else {
                string escaped = EscapeText(cell.StrVal);
                buf.Append(CultureInfo.InvariantCulture, $"""<c r="{r}" t="inlineStr"><is><t>{escaped}</t></is></c>""");
            }
        }
        buf.Append("</row>");
    }

    public void Close() {
        if (_closed) {
            return;
        }
        if (!_sheetStarted) {
            StartSheet();
            _sheetStarted = true;
        }
        _sheet!.Append("</sheetData></worksheet>");
        WriteEntry("xl/worksheets/sheet1.xml", _sheet.ToString());
        _zip.Dispose();
        _closed = true;
    }

    public void Dispose() {
        if (!_closed) {
            _zip.Dispose();
            _closed = true;
        }
    }

    private void StartSheet() {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<sheetFormatPr defaultRowHeight="15"/>""");
        if (_colWidths.Length > 0) {
            sb.Append("<cols>");
            for (int i = 0; i < _colWidths.Length; i++) {
                int col = i + 1;
                string width = _colWidths[i].ToString("F2", CultureInfo.InvariantCulture);
                sb.Append(CultureInfo.InvariantCulture, $"""<col min="{col}" max="{col}" width="{width}" customWidth="1"/>""");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        _sheet = sb;
    }

    private void WriteStaticEntries() {
        foreach (var (name, content) in StaticEntries) {
            WriteEntry(name, content);
        }
    }

    private void WriteEntry(string name, string content) {
        var entry = _zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string EscapeText(string s) {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) {
            switch (c) {
                case '"':
                    sb.Append("&#34;");
                    break;
                case '\'':
                    sb.Append("&#39;");
                    break;
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '\t':
                    sb.Append("&#x9;");
                    break;
                case '\n':
                    sb.Append("&#xA;");
                    break;
                case '\r':
                    sb.Append("&#xD;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static readonly (string Name, string Content)[] StaticEntries = [
        ("[Content_Types].xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
            + """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">"""
            + """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>"""
            + """<Default Extension="xml" ContentType="application/xml"/>"""
            + """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>"""
            + """<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""
            + """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>"""
            + "</Types>"),
        ("_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
            + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
            + """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>"""
            + "</Relationships>"),
        ("xl/workbook.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
            + """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">"""
            + """<sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>"""
            + "</workbook>"),
        ("xl/_rels/workbook.xml.rels",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
            + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
            + """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>"""
            + """<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>"""
            + "</Relationships>"),
        ("xl/styles.xml",
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>"""
            + """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
            + """<numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd hh:mm:ss"/></numFmts>"""
            + """<fonts count="1"><font><name val="Consolas"/><sz val="11"/></font></fonts>"""
            + """<fills count="2">"""
            + """<fill><patternFill patternType="none"/></fill>"""
            + """<fill><patternFill patternType="gray125"/></fill>"""
            + "</fills>"
            + """<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"""
            + """<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>"""
            + """<cellXfs count="2">"""
            + """<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>"""
            + """<xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>"""
            + "</cellXfs>"
            + """<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>"""
            + "</styleSheet>"),
    ];
}

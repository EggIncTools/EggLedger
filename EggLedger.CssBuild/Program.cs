using EggLedger.CssBuild;

if (args.Length < 1) {
    Console.Error.WriteLine("Usage: EggLedger.CssBuild <EggLedger.Web project directory>");
    return 1;
}

var webProjectDir = Path.GetFullPath(args[0]);
if (!Directory.Exists(webProjectDir)) {
    Console.Error.WriteLine($"Web project directory not found: {webProjectDir}");
    return 1;
}

string finalCss;
try {
    finalCss = CssBuilder.Build(webProjectDir, Console.WriteLine);
} catch (InvalidOperationException ex) {
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var outputPath = Path.Combine(webProjectDir, "wwwroot", "styles.css");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, finalCss);

Console.WriteLine($"Wrote {finalCss.Length} chars to {outputPath}");
return 0;

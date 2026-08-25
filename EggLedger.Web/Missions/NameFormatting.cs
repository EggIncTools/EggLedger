namespace EggLedger.Web.Missions;

public static class NameFormatting {
    public static string ProperCase(string value) {
        var words = value.ToLowerInvariant().Split(' ');
        for (var i = 0; i < words.Length; i++) {
            if (words[i] is not ("of" or "the") && words[i].Length > 0) {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            }
        }

        var joined = string.Join(' ', words);
        return joined.Length > 0 ? char.ToUpperInvariant(joined[0]) + joined[1..] : joined;
    }

    public static string TargetDisplayName(string target) =>
        ProperCase(target.Replace("_", " ", StringComparison.Ordinal));
}

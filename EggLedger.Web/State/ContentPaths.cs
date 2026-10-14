namespace EggLedger.Web.State;

public static class ContentPaths {
    public const string Base = "_content/EggLedger.Web/";
    public const string GameBase = "_content/EggSharp.GameData.Assets/";
    public static string Asset(string relative) => Base + relative;
    public static string GameAsset(string relative) => GameBase + relative;
}

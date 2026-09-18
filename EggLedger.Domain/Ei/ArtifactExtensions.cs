using System.Globalization;
using EggLedger.Domain.LedgerData;
using Ei;

namespace EggLedger.Domain.Ei;

public static class ArtifactExtensions {
    private static LedgerDisplayData Config => LedgerData.LedgerData.Config;

    public static string GameName(this ArtifactSpec.Name a) => a.FamilyBaseName();

    public static string CasedName(this ArtifactSpec.Name a) =>
        CapitalizeArtifactName(a.GameName().ToLowerInvariant());

    public static int InventoryVisualizerOrder(this ArtifactSpec.Name a) =>
        Config.InventoryVisualizerOrder.TryGetValue(EnumNames.ProtoName(a), out var order) ? order : 0;

    public static ArtifactSpec.Type ArtifactType(this ArtifactSpec.Name a) {
        if (Config.ArtifactTypes.TryGetValue(EnumNames.ProtoName(a), out var t)) {
            switch (t) {
                case "ARTIFACT":
                    return ArtifactSpec.Type.Artifact;
                case "STONE":
                    return ArtifactSpec.Type.Stone;
                case "STONE_INGREDIENT":
                    return ArtifactSpec.Type.StoneIngredient;
                case "INGREDIENT":
                    return ArtifactSpec.Type.Ingredient;
            }
        }
        return ArtifactSpec.Type.Artifact;
    }

    public static ArtifactSpec.Name Family(this ArtifactSpec.Name a) =>
        a.ArtifactType() == ArtifactSpec.Type.StoneIngredient ? a.CorrespondingStone() : a;

    public static ArtifactSpec.Name CorrespondingStone(this ArtifactSpec.Name a) {
        if (Config.StoneFragmentMap.TryGetValue(EnumNames.ProtoName(a), out var stone)
            && EnumNames.TryValue<ArtifactSpec.Name>(stone, out var val)) {
            return val;
        }
        return ArtifactSpec.Name.Unknown;
    }

    public static ArtifactSpec.Name CorrespondingFragment(this ArtifactSpec.Name a) {
        string target = EnumNames.ProtoName(a);
        foreach (var (fragment, stone) in Config.StoneFragmentMap) {
            if (stone == target && EnumNames.TryValue<ArtifactSpec.Name>(fragment, out var val)) {
                return val;
            }
        }
        return ArtifactSpec.Name.Unknown;
    }

    public static string GenericBenefitString(this ArtifactSpec a) {
        if (!a.ShouldSerializename()) {
            return "";
        }
        return Config.GenericBenefitStrings.TryGetValue(EnumNames.ProtoName(a.name), out var s) ? s : "";
    }

    public static string DropEffectString(this ArtifactSpec a) {
        var v = EffectTableValue(a);
        return v == "" ? "" : "[" + v + "]";
    }

    private static string EffectTableValue(ArtifactSpec a) {
        if (!a.ShouldSerializename()) {
            return "";
        }
        if (!Config.ArtifactEffects.TryGetValue(EnumNames.ProtoName(a.name), out var effects)
            || !a.ShouldSerializelevel() || (int)a.level >= effects.Length) {
            return "";
        }
        var row = effects[(int)a.level];
        if (!a.ShouldSerializerarity() || (int)a.rarity >= row.Length) {
            return "";
        }
        return row[(int)a.rarity];
    }

    public static string CombinedEffectString(this ArtifactSpec a) {
        var v = EffectTableValue(a);
        if (v == "") {
            return "";
        }
        if (v.StartsWith("!!", StringComparison.Ordinal)) {
            return v;
        }
        var generic = a.GenericBenefitString();
        if (generic != "") {
            if (generic.Contains("[^b]", StringComparison.Ordinal)) {
                return ReplaceFirst(generic, "[^b]", "[" + v + "]");
            }
            if (generic.Contains("[+^b]", StringComparison.Ordinal)) {
                return ReplaceFirst(generic, "[+^b]", "[+" + v + "]");
            }
            if (generic.Contains("[-^b]", StringComparison.Ordinal)) {
                return ReplaceFirst(generic, "[-^b]", "[-" + v + "]");
            }
        }
        return "[" + v + "]";
    }

    public static string DisplayTierName(this ArtifactSpec a, bool includeSpace) {
        var tierName = a.TierName();
        if (tierName == "REGULAR") {
            return "";
        }
        return includeSpace ? tierName + " " : tierName;
    }

    private static readonly Dictionary<ArtifactSpec.Name, string> BaseNameOverrides = new() {
        [ArtifactSpec.Name.VialMartianDust] = "VIAL OF MARTIAN DUST",
        [ArtifactSpec.Name.TheChalice] = "CHALICE",
        [ArtifactSpec.Name.MercurysLens] = "MERCURY'S LENS",
    };

    public static string FamilyBaseName(this ArtifactSpec.Name a) =>
        BaseNameOverrides.TryGetValue(a, out var overrideName) ? overrideName : EnumNames.ProtoName(a).Replace("_", " ");

    public static string GameName(this ArtifactSpec a) {
        string baseName = "";
        switch (a.name) {

            case ArtifactSpec.Name.LunarTotem:
            case ArtifactSpec.Name.NeodymiumMedallion:
            case ArtifactSpec.Name.BeakOfMidas:
            case ArtifactSpec.Name.LightOfEggendil:
            case ArtifactSpec.Name.DemetersNecklace:
            case ArtifactSpec.Name.VialMartianDust:
            case ArtifactSpec.Name.OrnateGusset:
            case ArtifactSpec.Name.TheChalice:
            case ArtifactSpec.Name.BookOfBasan:
            case ArtifactSpec.Name.PhoenixFeather:
            case ArtifactSpec.Name.TungstenAnkh:
            case ArtifactSpec.Name.AurelianBrooch:
            case ArtifactSpec.Name.CarvedRainstick:
            case ArtifactSpec.Name.PuzzleCube:
            case ArtifactSpec.Name.QuantumMetronome:
            case ArtifactSpec.Name.ShipInABottle:
            case ArtifactSpec.Name.TachyonDeflector:
            case ArtifactSpec.Name.InterstellarCompass:
            case ArtifactSpec.Name.DilithiumMonocle:
            case ArtifactSpec.Name.TitaniumActuator:
            case ArtifactSpec.Name.MercurysLens:
            case ArtifactSpec.Name.TachyonStone:
            case ArtifactSpec.Name.DilithiumStone:
            case ArtifactSpec.Name.ShellStone:
            case ArtifactSpec.Name.LunarStone:
            case ArtifactSpec.Name.SoulStone:
            case ArtifactSpec.Name.ProphecyStone:
            case ArtifactSpec.Name.QuantumStone:
            case ArtifactSpec.Name.TerraStone:
            case ArtifactSpec.Name.LifeStone:
            case ArtifactSpec.Name.ClarityStone:
                baseName = a.name.FamilyBaseName();
                break;

            case ArtifactSpec.Name.TachyonStoneFragment:
            case ArtifactSpec.Name.DilithiumStoneFragment:
            case ArtifactSpec.Name.ShellStoneFragment:
            case ArtifactSpec.Name.LunarStoneFragment:
            case ArtifactSpec.Name.SoulStoneFragment:
            case ArtifactSpec.Name.ProphecyStoneFragment:
            case ArtifactSpec.Name.QuantumStoneFragment:
            case ArtifactSpec.Name.TerraStoneFragment:
            case ArtifactSpec.Name.LifeStoneFragment:
            case ArtifactSpec.Name.ClarityStoneFragment:
                return EnumNames.ProtoName(a.name).Replace("_", " ");

            case ArtifactSpec.Name.GoldMeteorite:
                switch (a.level) {
                    case ArtifactSpec.Level.Inferior:
                        return "TINY GOLD METEORITE";
                    case ArtifactSpec.Level.Lesser:
                        return "ENRICHED GOLD METEORITE";
                    case ArtifactSpec.Level.Normal:
                        return "SOLID GOLD METEORITE";
                }
                break;
            case ArtifactSpec.Name.TauCetiGeode:
                switch (a.level) {
                    case ArtifactSpec.Level.Inferior:
                        return "TAU CETI GEODE PIECE";
                    case ArtifactSpec.Level.Lesser:
                        return "GLIMMERING TAU CETI GEODE";
                    case ArtifactSpec.Level.Normal:
                        return "RADIANT TAU CETI GEODE";
                }
                break;
            case ArtifactSpec.Name.SolarTitanium:
                switch (a.level) {
                    case ArtifactSpec.Level.Inferior:
                        return "SOLAR TITANIUM ORE";
                    case ArtifactSpec.Level.Lesser:
                        return "SOLAR TITANIUM BAR";
                    case ArtifactSpec.Level.Normal:
                        return "SOLAR TITANIUM GEOGON";
                }
                break;

            case ArtifactSpec.Name.ExtraterrestrialAluminum:
            case ArtifactSpec.Name.AncientTungsten:
            case ArtifactSpec.Name.SpaceRocks:
            case ArtifactSpec.Name.AlienWood:
            case ArtifactSpec.Name.CentaurianSteel:
            case ArtifactSpec.Name.EridaniFeather:
            case ArtifactSpec.Name.DroneParts:
            case ArtifactSpec.Name.CelestialBronze:
            case ArtifactSpec.Name.LalandeHide:
                return "? " + EnumNames.ProtoName(a.name);
        }

        return a.DisplayTierName(true) + baseName;
    }

    public static string CasedName(this ArtifactSpec a) =>
        CapitalizeArtifactName(a.GameName().ToLowerInvariant());

    public static string CasedSmallName(this ArtifactSpec a) =>
        CapitalizeArtifactName(a.name.GameName().ToLowerInvariant());

    public static ArtifactSpec.Type Type(this ArtifactSpec a) => a.name.ArtifactType();

    public static ArtifactSpec.Name Family(this ArtifactSpec a) => a.name.Family();

    public static int TierNumber(this ArtifactSpec a) {
        return a.Type() switch {
            ArtifactSpec.Type.Artifact => (int)a.level + 1,
            ArtifactSpec.Type.Stone => (int)a.level + 2,
            ArtifactSpec.Type.StoneIngredient => 1,
            ArtifactSpec.Type.Ingredient => (int)a.level + 1,
            _ => 1,
        };
    }

    public static string TierName(this ArtifactSpec a) {
        if (!a.ShouldSerializename() || !a.ShouldSerializelevel()) {
            return "?";
        }
        if (Config.ArtifactTierNames.TryGetValue(EnumNames.ProtoName(a.name), out var names)) {
            int idx = (int)a.level;
            if (idx >= 0 && idx < names.Length) {
                return names[idx];
            }
        }
        return "?";
    }

    public static string CasedTierName(this ArtifactSpec a) =>
        CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(a.TierName().ToLowerInvariant());

    public static string Display(this ArtifactSpec a) {
        string s = $"{a.CasedName()} (T{a.TierNumber()})";
        if ((int)a.rarity > 0) {
            s += $", {a.rarity.Display()}";
        }
        return s;
    }

    public static string Display(this ArtifactSpec.Rarity r) {
        return r switch {
            ArtifactSpec.Rarity.Common => "Common",
            ArtifactSpec.Rarity.Rare => "Rare",
            ArtifactSpec.Rarity.Epic => "Epic",
            ArtifactSpec.Rarity.Legendary => "Legendary",
            _ => "Unknown",
        };
    }

    public static string Display(this ArtifactSpec.Type t) =>
        new[] { "Artifact", "Stone", "Ingredient", "Stone ingredient" }[(int)t];

    private static string CapitalizeArtifactName(string n) {
        n = char.ToUpperInvariant(n[0]) + n[1..];

        var replacements = new (string from, string to)[] {
            ("demeters", "Demeters"),
            ("midas", "Midas"),
            ("eggendil", "Eggendil"),
            ("martian", "Martian"),
            ("basan", "Basan"),
            ("aurelian", "Aurelian"),
            ("mercury", "Mercury"),
            ("tau ceti", "Tau Ceti"),
            ("Tau ceti", "Tau Ceti"),
        };
        foreach (var (from, to) in replacements) {
            n = n.Replace(from, to);
        }
        return n;
    }

    private static string ReplaceFirst(string source, string oldValue, string newValue) {
        int idx = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (idx < 0) {
            return source;
        }
        return string.Concat(source.AsSpan(0, idx), newValue, source.AsSpan(idx + oldValue.Length));
    }
}

using Ei;

namespace EggLedger.Domain.Ei;

public static class EggExtensions {
    public static string Display(this Egg e) => e switch {
        Egg.RocketFuel => "Rocket Fuel",
        Egg.SuperMaterial => "Super Material",
        Egg.DarkMatter => "Dark Matter",
        Egg.Ai => "AI",
        _ => e.ToString(),
    };
}

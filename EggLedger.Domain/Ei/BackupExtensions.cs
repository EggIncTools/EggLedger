using Ei;

namespace EggLedger.Domain.Ei;

public static class BackupExtensions {
    public static double Sum<T>(IEnumerable<T>? slice, Func<T, double> toFloat) {
        double total = 0;
        if (slice is not null) {
            foreach (var v in slice) {
                total += toFloat(v);
            }
        }
        return total;
    }

    public static Exception? Validate(this EggIncFirstContactResponse fc) {
        if (fc.ErrorCode > 0) {
            return new InvalidOperationException(
                $"/ei/first_contact: error_code {fc.ErrorCode}");
        }
        if (fc.Backup?.game is null) {
            return new InvalidOperationException("backup is empty");
        }
        if (fc.Backup.settings is null) {
            return new InvalidOperationException("backup settings is empty");
        }
        return fc.Backup.ArtifactsDb is null
            ? new InvalidOperationException("backup has empty artifacts database")
            : null;
    }

    public static double GetEarningsBonus(this Backup b) {
        var virtue = b.virtue;
        var game = b.game;

        double soulEggBonus = 10.0;
        double prophecyEggBonus = 1.05;
        if (game is not null) {
            foreach (var er in game.EpicResearchs) {
                if (string.Equals(er.Id, "soul_eggs", StringComparison.OrdinalIgnoreCase)) {
                    soulEggBonus = er.Level + 10;
                } else if (string.Equals(er.Id, "prophecy_bonus", StringComparison.OrdinalIgnoreCase)) {
                    prophecyEggBonus = (er.Level + 5) / 100.0 + 1;
                }
            }
        }

        double totalPE = game?.EggsOfProphecy ?? 0;
        double peBonus = Math.Pow(prophecyEggBonus, totalPE);

        double totalSE = game?.SoulEggsD ?? 0;
        double seBonus = soulEggBonus * totalSE;

        double totalTEEarned = Sum(virtue?.EovEarneds, v => (double)v);
        double teFactor = Math.Pow(1.01, totalTEEarned);

        double result = peBonus * seBonus * teFactor;
        return result;
    }
}

using System.Globalization;
using EggLedger.Domain.Ei;
using EggLedger.Domain.Util;
using Ei;

namespace EggLedger.Domain.MissionQuery;

public static class AccountFactory {
    public static AccountInfo FromBackup(string eid, Backup backup) {
        string nickname = backup.UserName;
        double eb = backup.GetEarningsBonus();
        var (roleColor, _, ebAddendum, ebValue, precision) = Role.RoleFromEB(eb);
        string ebString = ebValue.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + ebAddendum;

        var game = backup.game;
        string seString = Format.AbbreviateFloat(game?.SoulEggsD ?? 0);
        int peCount = (int)(game?.EggsOfProphecy ?? 0);

        int totalTE = 0;
        var virtue = backup.virtue;
        if (virtue?.EovEarneds is { } eov) {
            foreach (var v in eov) {
                totalTE += (int)v;
            }
        }

        return new AccountInfo {
            Id = eid,
            Nickname = nickname,
            EBString = ebString,
            AccountColor = roleColor,
            SeString = seString,
            PeCount = peCount,
            TeCount = totalTE,
            LastBackupTime = backup.settings?.LastBackupTime ?? 0,
        };
    }
}

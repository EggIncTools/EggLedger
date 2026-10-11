namespace EggLedger.Domain.Util;

public static class Matrix {
    public static void Apply2DPctNormalization(double[] vals, int nR, int nC, string mode) {
        if (nR == 0 || nC == 0) return;
        switch (mode) {
            case "row_pct":
                for (var r = 0; r < nR; r++) {
                    double rowSum = 0;
                    for (var c = 0; c < nC; c++) rowSum += vals[r * nC + c];
                    if (rowSum > 0) for (var c = 0; c < nC; c++) vals[r * nC + c] = vals[r * nC + c] / rowSum * 100;
                }
                break;
            case "col_pct":
                for (var c = 0; c < nC; c++) {
                    double colSum = 0;
                    for (var r = 0; r < nR; r++) colSum += vals[r * nC + c];
                    if (colSum > 0) for (var r = 0; r < nR; r++) vals[r * nC + c] = vals[r * nC + c] / colSum * 100;
                }
                break;
            case "global_pct":
                double total = 0;
                foreach (var v in vals) total += v;
                if (total > 0) {
                    for (var i = 0; i < vals.Length; i++) vals[i] = vals[i] / total * 100;
                }
                break;
        }
    }
}

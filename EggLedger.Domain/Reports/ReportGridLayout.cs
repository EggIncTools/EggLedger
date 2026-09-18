namespace EggLedger.Domain.Reports;

public readonly record struct GridCardPos(int Col, int Row, int W, int H, int Idx);

public readonly record struct EmptyZone(int ColStart, int ColEnd, int RowStart, int InsertAfter);

public static class ReportGridLayout {
    public const int GridCols = 8;

    public const int GridGap = 12;

    public static (int W, int H) ClampDims(int gridW, int gridH) =>
        (Math.Min(Math.Max(gridW, 1), GridCols), Math.Min(Math.Max(gridH, 1), 8));

    public static void MarkOccupied(HashSet<(int, int)> occupied, int col, int row, int w, int h) {
        for (int rr = row; rr < row + h; rr++) {
            for (int cc = col; cc < col + w; cc++) {
                occupied.Add((cc, rr));
            }
        }
    }

    public static bool CellsFit(HashSet<(int, int)> occupied, int c, int r, int w, int h) {
        for (int rr = r; rr < r + h; rr++) {
            for (int cc = c; cc < c + w; cc++) {
                if (occupied.Contains((cc, rr))) {
                    return false;
                }
            }
        }
        return true;
    }

    public static (int Col, int Row) FindPlacement(HashSet<(int, int)> occupied, int w, int h, int col, int row) {
        int c = col;
        int r = row;
        while (true) {
            if (c + w - 1 > GridCols) {
                c = 1;
                r++;
            }
            if (CellsFit(occupied, c, r, w, h)) {
                return (c, r);
            }
            c++;
        }
    }

    public static (int Col, int Row, HashSet<(int, int)> Occupied) SimulatePlacement(IReadOnlyList<ReportDefinition> defs) {
        var occupied = new HashSet<(int, int)>();
        int col = 1;
        int row = 1;
        foreach (var def in defs) {
            var (w, h) = ClampDims(def.GridW, def.GridH);
            var pos = FindPlacement(occupied, w, h, col, row);
            col = pos.Col;
            row = pos.Row;
            MarkOccupied(occupied, col, row, w, h);
            col += w;
        }
        return (col, row, occupied);
    }

    public static (List<GridCardPos> CardPositions, HashSet<(int, int)> Occupied) BuildOccupancyFromLayout(
        IReadOnlyList<ReportDefinition> defs) {
        var cardPositions = new List<GridCardPos>();
        var occupied = new HashSet<(int, int)>();
        int col = 1;
        int row = 1;
        for (int idx = 0; idx < defs.Count; idx++) {
            var def = defs[idx];
            var (w, h) = ClampDims(def.GridW, def.GridH);
            var pos = FindPlacement(occupied, w, h, col, row);
            col = pos.Col;
            row = pos.Row;
            cardPositions.Add(new GridCardPos(col, row, w, h, idx));
            MarkOccupied(occupied, col, row, w, h);
            col += w;
        }
        return (cardPositions, occupied);
    }

    public static int ZoneInsertAfter(IReadOnlyList<GridCardPos> cardPositions, int targetRow, int runStart) {
        int zoneFirst = (targetRow - 1) * GridCols + runStart;
        int best = -1;
        foreach (var cp in cardPositions) {
            if ((cp.Row - 1) * GridCols + cp.Col < zoneFirst) {
                best = Math.Max(best, cp.Idx);
            }
        }
        return best;
    }

    public static int FindInsertIndexForZone(IReadOnlyList<ReportDefinition> defs, EmptyZone zone, int fromIdx) {
        if (fromIdx < 0 || fromIdx >= defs.Count) {
            return fromIdx;
        }
        var draggedDef = defs[fromIdx];

        var withoutDragged = defs.Where((_, i) => i != fromIdx).ToList();
        var (dw, dh) = ClampDims(draggedDef.GridW, draggedDef.GridH);

        for (int insertPos = 0; insertPos <= withoutDragged.Count; insertPos++) {
            var (col, row, occupied) = SimulatePlacement(withoutDragged.Take(insertPos).ToList());
            var pos = FindPlacement(occupied, dw, dh, col, row);
            if (pos.Col == zone.ColStart && pos.Row == zone.RowStart) {
                return insertPos;
            }
        }

        int insertAt = zone.InsertAfter;
        if (fromIdx <= insertAt) {
            insertAt--;
        }
        return Math.Max(insertAt + 1, 0);
    }

    public static List<EmptyZone> RowEmptyZones(int r, HashSet<(int, int)> occupied, IReadOnlyList<GridCardPos> cardPositions) {
        var zones = new List<EmptyZone>();
        int? runStart = null;
        for (int c = 1; c <= GridCols + 1; c++) {
            bool isEmpty = c <= GridCols && !occupied.Contains((c, r));
            if (isEmpty && runStart is null) {
                runStart = c;
                continue;
            }
            if (!isEmpty && runStart is int start) {
                zones.Add(new EmptyZone(start, c, r, ZoneInsertAfter(cardPositions, r, start)));
                runStart = null;
            }
        }
        return zones;
    }

    public static List<EmptyZone> ComputeEmptyZones(IReadOnlyList<ReportDefinition> defs) {
        var (cardPositions, occupied) = BuildOccupancyFromLayout(defs);
        if (cardPositions.Count == 0) {
            return [];
        }
        int maxRow = cardPositions.Max(p => p.Row + p.H - 1);
        return [.. Enumerable.Range(1, maxRow).SelectMany(r => RowEmptyZones(r, occupied, cardPositions))];
    }

    public static int ComputeRowHeightPx(double containerWidthPx) {
        var raw = (containerWidthPx - (GridCols - 1) * GridGap) / GridCols;
        return Math.Max(80, (int)Math.Floor(raw));
    }
}

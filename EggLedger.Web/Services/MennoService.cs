using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggLedger.Domain.Eiafx;
using EggLedger.Domain.Reports;
using EggLedger.Domain.Util;
using Ei;

namespace EggLedger.Web.Services;

public static class MennoDecode {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = false,

        NumberHandling = JsonNumberHandling.Strict,
    };

    public static List<ConfigurationItem> Decode(ReadOnlySpan<byte> json) {
        List<ConfigurationItem>? items;
        try {
            items = JsonSerializer.Deserialize<List<ConfigurationItem>>(json, Options);
        } catch (JsonException ex) {
            throw new MennoSchemaException("Menno data response was not valid JSON or did not match the expected shape", ex);
        }

        if (items is null || items.Count == 0) {
            throw new MennoSchemaException("Menno data response was empty or schema has changed");
        }

        for (int i = 0; i < items.Count; i++) {
            Validate(items[i], i);
        }
        return items;
    }

    private static void Validate(ConfigurationItem item, int index) {
        var sc = item.ShipConfiguration
            ?? throw Missing(index, "shipConfiguration");
        if (sc.ShipType is null) throw Missing(index, "shipConfiguration.shipType");
        if (sc.ShipDurationType is null) throw Missing(index, "shipConfiguration.shipDurationType");
        if (sc.TargetArtifact is null) throw Missing(index, "shipConfiguration.targetArtifact");

        var ac = item.ArtifactConfiguration
            ?? throw Missing(index, "artifactConfiguration");
        if (ac.ArtifactType is null) throw Missing(index, "artifactConfiguration.artifactType");
        if (ac.ArtifactRarity is null) throw Missing(index, "artifactConfiguration.artifactRarity");
    }

    private static MennoSchemaException Missing(int index, string field) =>
        new($"Menno item {index} is missing required field '{field}'; schema may have changed");
}

public sealed class MennoSchemaException : Exception {
    public MennoSchemaException(string message) : base(message) { }
    public MennoSchemaException(string message, Exception inner) : base(message, inner) { }
}

public interface IMennoDataStore {
    Task<byte[]?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(byte[] utf8Json, CancellationToken cancellationToken = default);
}

public sealed class MennoService {
    public const string DataUrl =
        "https://eggincdatacollectionsa.blob.core.windows.net/mission-data/all-data.json.gz";



    private readonly HttpClient _http;
    private readonly IMennoDataStore? _store;
    private readonly Lock _gate = new();
    private List<ConfigurationItem>? _cache;
    private Task<IReadOnlyList<ConfigurationItem>>? _inFlight;

    public MennoService(HttpClient http, IMennoDataStore? store = null) {
        _http = http;
        _store = store;
    }

    public bool HasData => _cache is { Count: > 0 };

    public Task<IReadOnlyList<ConfigurationItem>> EnsureLoadedAsync(CancellationToken cancellationToken = default) {
        if (_cache is { Count: > 0 } cached) {
            return Task.FromResult<IReadOnlyList<ConfigurationItem>>(cached);
        }
        lock (_gate) {
            if (_cache is { Count: > 0 } cachedLocked) {
                return Task.FromResult<IReadOnlyList<ConfigurationItem>>(cachedLocked);
            }
            _inFlight ??= LoadOnceAsync(cancellationToken);
            return _inFlight;
        }
    }

    private async Task<IReadOnlyList<ConfigurationItem>> LoadOnceAsync(CancellationToken cancellationToken) {
        try {
            var stored = await TryLoadStoredAsync(cancellationToken).ConfigureAwait(false);
            if (stored is not null) {
                return stored;
            }
            return await RefreshAsync(cancellationToken).ConfigureAwait(false);
        } finally {
            lock (_gate) {
                _inFlight = null;
            }
        }
    }

    private async Task<IReadOnlyList<ConfigurationItem>?> TryLoadStoredAsync(CancellationToken cancellationToken) {
        if (_store is null) {
            return null;
        }
        try {
            var bytes = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) {
                return null;
            }
            var items = MennoDecode.Decode(bytes);
            _cache = items;
            return items;
        } catch (Exception ex) when (ex is MennoSchemaException or IOException) {
            return null;
        }
    }

    public async Task<IReadOnlyList<ConfigurationItem>> RefreshAsync(CancellationToken cancellationToken = default) {
        await using var compressed = await _http.GetStreamAsync(DataUrl, cancellationToken).ConfigureAwait(false);
        await using var gz = new GZipStream(compressed, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        await gz.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);

        var items = MennoDecode.Decode(ms.GetBuffer().AsSpan(0, (int)ms.Length));
        _cache = items;
        if (_store is not null) {
            await _store.SaveAsync(ms.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        return items;
    }

    public IReadOnlyList<ConfigurationItem>? CachedItems => _cache;

    public const int NoTarget = 10000;

    public IReadOnlyList<ConfigurationItem> GetData(int shipId, int durationId, int level, int targetId) {
        if (_cache is not { Count: > 0 } items) {
            return Array.Empty<ConfigurationItem>();
        }
        var matches = Filter(items, shipId, durationId, level, targetId);
        if (matches.Count == 0 && targetId != NoTarget) {
            matches = Filter(items, shipId, durationId, level, NoTarget);
        }
        return matches;
    }

    private static List<ConfigurationItem> Filter(
        IReadOnlyList<ConfigurationItem> items, int shipId, int durationId, int level, int targetId) {
        var result = new List<ConfigurationItem>();
        foreach (var item in items) {
            var sc = item.ShipConfiguration;
            if (sc?.ShipType is null || sc.ShipDurationType is null || sc.TargetArtifact is null) {
                continue;
            }
            if (sc.ShipType.Id == shipId
                && sc.ShipDurationType.Id == durationId
                && sc.Level == level
                && sc.TargetArtifact.Id == targetId) {
                result.Add(item);
            }
        }
        return result;
    }

    public static ReportResult? ExecuteComparison(
        ReportDefinition def,
        IReadOnlyList<ConfigurationItem> items,
        IReadOnlyList<string> rawRowLabels,
        IReadOnlyList<string> rawColLabels) {
        if (!def.MennoEnabled) {
            return null;
        }
        if (def.Subject != "artifacts") {
            return null;
        }
        if (!Report.MennoComparableGroupBy(def.GroupBy) || !Report.MennoComparableGroupBy(def.SecondaryGroupBy)) {
            return null;
        }
        if (items.Count == 0 || rawRowLabels.Count == 0 || rawColLabels.Count == 0) {
            return null;
        }

        var rowMatcher = MatcherFor(def.GroupBy);
        var colMatcher = MatcherFor(def.SecondaryGroupBy);
        if (rowMatcher is null || colMatcher is null) {
            return null;
        }

        var familySet = FamilyAfxIdSet(def.FamilyWeight);

        int nR = rawRowLabels.Count;
        int nC = rawColLabels.Count;

        string pctMode = def.NormalizeBy;
        bool isPct = pctMode is "row_pct" or "col_pct" or "global_pct";

        var (familyDrops, estimatedMissions) = AccumulateDrops(
            items, rowMatcher, colMatcher, rawRowLabels, rawColLabels, nR, nC, isPct, def, familySet);

        var (shipAxis, durAxis) = ResolveAxisRoles(def);

        var (matrixValues, airtimeMatrixValues) = ComputeMatrix(
            familyDrops, estimatedMissions, nR, nC, isPct, pctMode, shipAxis, durAxis, rawRowLabels, rawColLabels);

        var rowLabels = new List<string>(nR);
        var colLabels = new List<string>(nC);
        for (int i = 0; i < nR; i++) {
            rowLabels.Add(Labels.FormatLabel(def.GroupBy, rawRowLabels[i]));
        }
        for (int i = 0; i < nC; i++) {
            colLabels.Add(Labels.FormatLabel(def.SecondaryGroupBy, rawColLabels[i]));
        }

        return new ReportResult {
            RowLabels = rowLabels,
            ColLabels = colLabels,
            MatrixValues = [.. matrixValues],
            Is2D = true,
            IsFloat = true,
            Weight = def.Weight,
            RawRowLabels = [.. rawRowLabels],
            RawColLabels = [.. rawColLabels],
            AirtimeMatrixValues = airtimeMatrixValues?.ToList(),
        };
    }

    private static (double[] familyDrops, double[] estimatedMissions) AccumulateDrops(
        IReadOnlyList<ConfigurationItem> items,
        MennoMatcher rowMatcher,
        MennoMatcher colMatcher,
        IReadOnlyList<string> rawRowLabels,
        IReadOnlyList<string> rawColLabels,
        int nR,
        int nC,
        bool isPct,
        ReportDefinition def,
        HashSet<int>? familySet) {
        var familyDrops = new double[nR * nC];
        var estimatedMissions = new double[nR * nC];

        foreach (var item in items) {
            for (int r = 0; r < nR; r++) {
                if (!rowMatcher(item, rawRowLabels[r])) {
                    continue;
                }
                for (int c = 0; c < nC; c++) {
                    if (!colMatcher(item, rawColLabels[c])) {
                        continue;
                    }
                    int idx = (r * nC) + c;
                    if (!isPct) {
                        double itemCap = LevelAdjustedCapacityFor(
                            item.ShipConfiguration!.ShipType!.Id,
                            item.ShipConfiguration.ShipDurationType!.Id,
                            item.ShipConfiguration.Level);
                        estimatedMissions[idx] += item.TotalDrops / itemCap;
                    }
                    int artifactTypeId = item.ArtifactConfiguration!.ArtifactType!.Id;
                    if (familySet is null || familySet.Contains(artifactTypeId)) {
                        double w = 1.0;
                        if (def.FamilyWeight != "") {
                            double cw = EiafxData.CraftingWeights.TryGetValue(
                                (artifactTypeId, item.ArtifactConfiguration.ArtifactLevel), out var found)
                                ? found
                                : 0;
                            if (cw > 0) {
                                w = cw;
                            }
                        }
                        familyDrops[idx] += item.TotalDrops * w;
                    }
                }
            }
        }

        return (familyDrops, estimatedMissions);
    }

    private static (string shipAxis, string durAxis) ResolveAxisRoles(ReportDefinition def) {
        string shipAxis = "";
        string durAxis = "";
        if (def.GroupBy == "ship_type") {
            shipAxis = "row";
        } else if (def.SecondaryGroupBy == "ship_type") {
            shipAxis = "col";
        }
        if (def.GroupBy == "duration_type") {
            durAxis = "row";
        } else if (def.SecondaryGroupBy == "duration_type") {
            durAxis = "col";
        }
        return (shipAxis, durAxis);
    }

    private static (double[] matrixValues, double[]? airtimeMatrixValues) ComputeMatrix(
        double[] familyDrops,
        double[] estimatedMissions,
        int nR,
        int nC,
        bool isPct,
        string pctMode,
        string shipAxis,
        string durAxis,
        IReadOnlyList<string> rawRowLabels,
        IReadOnlyList<string> rawColLabels) {
        var matrixValues = new double[nR * nC];

        bool canComputeAirtime = shipAxis != "" && durAxis != "" && !isPct;
        double[]? airtimeMatrixValues = canComputeAirtime ? new double[nR * nC] : null;

        for (int r = 0; r < nR; r++) {
            for (int c = 0; c < nC; c++) {
                int idx = (r * nC) + c;
                double fd = familyDrops[idx];

                if (isPct) {
                    matrixValues[idx] = fd;
                    continue;
                }

                double estMissions = estimatedMissions[idx];
                if (estMissions <= 0 || double.IsNaN(estMissions) || double.IsInfinity(estMissions)) {
                    matrixValues[idx] = 0;
                    continue;
                }

                matrixValues[idx] = fd / estMissions;

                if (canComputeAirtime) {
                    int shipId = 0;
                    int durId = 0;
                    if (shipAxis == "row") {
                        shipId = ParseIntOrZero(rawRowLabels[r]);
                    } else if (shipAxis == "col") {
                        shipId = ParseIntOrZero(rawColLabels[c]);
                    }
                    if (durAxis == "row") {
                        durId = ParseIntOrZero(rawRowLabels[r]);
                    } else if (durAxis == "col") {
                        durId = ParseIntOrZero(rawColLabels[c]);
                    }
                    double nominalSecs = DurationSecondsFor(shipId, durId);
                    if (nominalSecs > 0) {
                        airtimeMatrixValues![idx] = fd / estMissions / (nominalSecs / 3600.0);
                    }
                }
            }
        }

        if (isPct) {
            Matrix.Apply2DPctNormalization(matrixValues, nR, nC, pctMode);
        }

        return (matrixValues, airtimeMatrixValues);
    }

    private delegate bool MennoMatcher(ConfigurationItem item, string rawVal);


    private static MennoMatcher? MatcherFor(string groupBy) => groupBy switch {
        "ship_type" => (item, raw) => TryInt(raw, out var v) && item.ShipConfiguration!.ShipType!.Id == v,
        "duration_type" => (item, raw) => TryInt(raw, out var v) && item.ShipConfiguration!.ShipDurationType!.Id == v,
        "level" => (item, raw) => TryInt(raw, out var v) && item.ShipConfiguration!.Level == v,
        "mission_target" => (item, raw) => TryInt(raw, out var v) && item.ShipConfiguration!.TargetArtifact!.Id == v,
        "artifact_name" => (item, raw) => TryInt(raw, out var v) && item.ArtifactConfiguration!.ArtifactType!.Id == v,
        "rarity" => (item, raw) => TryInt(raw, out var v) && item.ArtifactConfiguration!.ArtifactRarity!.Id == v,
        "tier" => (item, raw) => TryInt(raw, out var v) && item.ArtifactConfiguration!.ArtifactLevel == v,
        _ => null,
    };

    private static HashSet<int>? FamilyAfxIdSet(string familyWeight) {
        if (familyWeight == "") {
            return null;
        }
        if (!EiafxData.FamilyAfxIds.TryGetValue(familyWeight, out var ids)) {
            return null;
        }
        return [.. ids];
    }

    private static double LevelAdjustedCapacityFor(int shipId, int durationId, int level) {
        var ship = (MissionInfo.Spaceship)shipId;
        var dur = (MissionInfo.DurationType)durationId;
        foreach (var mp in EiafxConfig.Config.mission_parameters) {
            if (mp.Ship != ship) {
                continue;
            }
            foreach (var d in mp.Durations) {
                if (d.DurationType == dur) {
                    double cap = d.Capacity + (d.LevelCapacityBump * (double)level);
                    if (cap > 0) {
                        return cap;
                    }
                }
            }
        }
        return 4;
    }

    private static double DurationSecondsFor(int shipId, int durationId) {
        var ship = (MissionInfo.Spaceship)shipId;
        var dur = (MissionInfo.DurationType)durationId;
        foreach (var mp in EiafxConfig.Config.mission_parameters) {
            if (mp.Ship != ship) {
                continue;
            }
            foreach (var d in mp.Durations) {
                if (d.DurationType == dur) {
                    return d.Seconds;
                }
            }
        }
        return 0;
    }

    private static bool TryInt(string raw, out int value) =>
        int.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private static int ParseIntOrZero(string raw) => TryInt(raw, out var v) ? v : 0;
}

namespace EggLedger.Web.Missions.Model;

public abstract class FilterViewContext {
    public const string MissionsDraftKey = "missions";

    public static readonly FilterViewContext Empty = new StandaloneFilterContext(new FilterFieldCtx(), false);

    public abstract string DraftKey { get; }

    public abstract bool IncludeArtifactFields { get; }

    public abstract FilterFieldCtx Fields { get; }

    public abstract string EmptyNote { get; }

    public bool UsesSharedDraft => DraftKey.Length > 0;

    public bool BindsLike(FilterViewContext other) =>
        DraftKey == other.DraftKey
        && IncludeArtifactFields == other.IncludeArtifactFields
        && ReferenceEquals(Fields, other.Fields);
}

public sealed class ShipsFilterContext(FilterFieldCtx fields) : FilterViewContext {
    public override string DraftKey => MissionsDraftKey;

    public override bool IncludeArtifactFields => false;

    public override FilterFieldCtx Fields { get; } = fields;

    public override string EmptyNote => "";
}

public sealed class DropsFilterContext(FilterFieldCtx fields) : FilterViewContext {
    public override string DraftKey => MissionsDraftKey;

    public override bool IncludeArtifactFields => false;

    public override FilterFieldCtx Fields { get; } = fields;

    public override string EmptyNote => "";
}

public sealed class ReportsFilterContext(FilterFieldCtx fields) : FilterViewContext {
    public override string DraftKey => "";

    public override bool IncludeArtifactFields => true;

    public override FilterFieldCtx Fields { get; } = fields;

    public override string EmptyNote => "Filters are saved per report.";
}

public sealed class StandaloneFilterContext(FilterFieldCtx fields, bool includeArtifactFields) : FilterViewContext {
    public override string DraftKey => "";

    public override bool IncludeArtifactFields { get; } = includeArtifactFields;

    public override FilterFieldCtx Fields { get; } = fields;

    public override string EmptyNote => "";
}

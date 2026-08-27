namespace Quizizzo.Web.Drawing;

public sealed record DrawingDraftIdentity(
    Guid PartyId,
    Guid GameSessionId,
    string RoundId,
    Guid PlayerId);

public sealed class DrawingControllerOptions
{
    public static readonly IReadOnlyList<string> DefaultColours =
    [
        "#111827", "#ffffff", "#dc2626", "#f97316", "#facc15", "#16a34a", "#0d9488",
        "#06b6d4", "#2563eb", "#7c3aed", "#c026d3", "#f472b6", "#92400e", "#6b7280"
    ];

    public static readonly IReadOnlyList<int> DefaultWidths = [2, 5, 9, 16, 28];

    public required DrawingDraftIdentity Identity { get; init; }

    public int LogicalWidth { get; init; } = 512;

    public int LogicalHeight { get; init; } = 512;

    public int FrameCount { get; init; } = 3;

    public bool OnionSkinEnabled { get; init; } = true;

    public IReadOnlyList<string> Colours { get; init; } = DefaultColours;

    public IReadOnlyList<int> Widths { get; init; } = DefaultWidths;

    public string DraftKey => FormattableString.Invariant(
        $"quizizzo:drawing:v1:{Identity.PartyId:N}:{Identity.GameSessionId:N}:{Identity.RoundId}:{Identity.PlayerId:N}");

    public string DraftFamilyPrefix => FormattableString.Invariant(
        $"quizizzo:drawing:v1:{Identity.PartyId:N}:");

    public void Validate()
    {
        if (LogicalWidth is < 64 or > 2048 || LogicalHeight is < 64 or > 2048)
        {
            throw new InvalidOperationException("Logical drawing dimensions must be between 64 and 2048.");
        }
        if (FrameCount is < 1 or > 12)
        {
            throw new InvalidOperationException("A drawing controller must have between 1 and 12 frames.");
        }
        if (string.IsNullOrWhiteSpace(Identity.RoundId) || Identity.RoundId.Length > 128)
        {
            throw new InvalidOperationException("A bounded drawing round identifier is required.");
        }
        if (Identity.PartyId == Guid.Empty || Identity.GameSessionId == Guid.Empty || Identity.PlayerId == Guid.Empty)
        {
            throw new InvalidOperationException("Drawing draft identity values cannot be empty.");
        }
        if (Colours.Count is < 1 or > 32 || Widths.Count is < 1 or > 10)
        {
            throw new InvalidOperationException("The drawing palette or brush-size collection is invalid.");
        }
        if (Colours.Any(colour => colour.Length != 7 || colour[0] != '#' ||
                !colour[1..].All(Uri.IsHexDigit)) ||
            Widths.Any(width => width is < 1 or > 64) ||
            Colours.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Colours.Count ||
            Widths.Distinct().Count() != Widths.Count)
        {
            throw new InvalidOperationException("Drawing colours or brush sizes contain invalid values.");
        }
    }
}

public sealed record DrawingStateSummary(
    int CurrentFrame,
    int FrameCount,
    IReadOnlyList<bool> FrameHasContent,
    bool CanUndo,
    string SelectedColour,
    int SelectedWidth,
    string Tool,
    bool OnionSkinEnabled,
    bool RestoredDraft,
    DateTimeOffset LastUpdatedAt);

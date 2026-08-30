using Quizizzo.Application.Displays;
using Quizizzo.Application.Games;
using Quizizzo.Application.Players;
using Quizizzo.GameContracts;

namespace Quizizzo.Web.Presentation;

public sealed record PhaserPresentationSnapshot(
    string Mode,
    string? GameKey,
    string Phase,
    long Revision,
    IReadOnlyList<PhaserPlayerSnapshot> Players,
    IReadOnlyList<PhaserResultSnapshot> Results,
    PhaserDrawingPresentationSnapshot? Drawing);

public sealed record PhaserPlayerSnapshot(
    string PlayerId,
    string DisplayName,
    int Score,
    string Status,
    PhaserCharacterSnapshot Character);

public sealed record PhaserCharacterSnapshot(
    string BodyType,
    string PrimaryColour,
    string Eyes,
    string Mouth,
    string Accessory,
    string Presentation,
    int SkinTone,
    string HairColour,
    string ShirtColour,
    string TrouserColour,
    string TrouserLength,
    string ShoeColour,
    int HairStyle,
    string EyeColour,
    string EyeSize,
    string FaceShape,
    int NoseShape,
    int BrowShape,
    int ShoeStyle,
    int ShirtStyle,
    int TrouserStyle,
    string BodySize);

public sealed record PhaserResultSnapshot(
    string PlayerId,
    int Rank,
    int PointsAwarded);

public sealed record PhaserDrawingPresentationSnapshot(
    string Mode,
    int FrameDurationMilliseconds,
    IReadOnlyList<PhaserDrawingAnimationSnapshot> Animations);

public sealed record PhaserDrawingAnimationSnapshot(
    string SubmissionPlayerId,
    string? CreatorName,
    string Prompt,
    IReadOnlyList<string> FrameUrls,
    int Votes,
    int? Rank,
    int PointsAwarded);

public static class PhaserPresentationMapper
{
    public static PhaserPresentationSnapshot Create(
        DisplaySessionView session,
        IReadOnlyList<PlayerView> players,
        PartyGameView? gameView,
        DisplayGameViewPayload? game)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(players);

        var mode = !session.IsPaired
            ? "Pairing"
            : gameView is null ? "Lobby" : "Game";
        var presentationPlayers = players.Select(player => new PhaserPlayerSnapshot(
            player.PlayerId.ToString("N"),
            player.DisplayName,
            gameView?.Scores.TryGetValue(player.PlayerId, out var gameScore) == true
                ? gameScore
                : player.Score,
            player.Status.ToString(),
            new PhaserCharacterSnapshot(
                player.Character.BodyType.ToString(),
                player.Character.PrimaryColour,
                player.Character.Eyes.ToString(),
                player.Character.Mouth.ToString(),
                player.Character.Accessory.ToString(),
                player.Character.Presentation.ToString(),
                (int)player.Character.SkinTone,
                player.Character.HairColour.ToString(),
                player.Character.ShirtColour.ToString(),
                player.Character.TrouserColour.ToString(),
                player.Character.TrouserLength.ToString(),
                player.Character.ShoeColour.ToString(),
                (int)player.Character.HairStyle,
                player.Character.EyeColour.ToString(),
                player.Character.EyeSize.ToString(),
                player.Character.FaceShape.ToString(),
                (int)player.Character.NoseShape,
                (int)player.Character.BrowShape,
                (int)player.Character.ShoeStyle,
                (int)player.Character.ShirtStyle,
                (int)player.Character.TrouserStyle,
                player.Character.BodySize.ToString()))).ToArray();
        var results = game?.Entries
            .Where(entry => entry.Rank.HasValue)
            .Select(entry => new PhaserResultSnapshot(
                entry.PlayerId.ToString("N"),
                entry.Rank!.Value,
                entry.PointsAwarded))
            .ToArray() ?? [];
        var drawing = game?.Drawing is { } presentation
            ? new PhaserDrawingPresentationSnapshot(
                presentation.Mode,
                presentation.FrameDurationMilliseconds,
                presentation.Animations.Select(animation => new PhaserDrawingAnimationSnapshot(
                    animation.SubmissionPlayerId.ToString("N"),
                    animation.CreatorName,
                    animation.Prompt,
                    animation.FrameAssetIds.Select(assetId => $"/api/drawing-assets/{assetId:D}").ToArray(),
                    animation.Votes,
                    animation.Rank,
                    animation.PointsAwarded)).ToArray())
            : null;

        return new PhaserPresentationSnapshot(
            mode,
            gameView?.GameKey,
            gameView?.Phase ?? mode,
            gameView?.Revision ?? 0,
            presentationPlayers,
            results,
            drawing);
    }
}

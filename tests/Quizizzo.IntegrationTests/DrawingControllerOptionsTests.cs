using Quizizzo.Web.Drawing;

namespace Quizizzo.IntegrationTests;

public sealed class DrawingControllerOptionsTests
{
    [Fact]
    public void One_frame_is_a_valid_first_class_controller_configuration()
    {
        var options = CreateOptions(frameCount: 1);

        options.Validate();

        Assert.Equal(1, options.FrameCount);
        Assert.Contains(options.Identity.RoundId, options.DraftKey, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Invalid_frame_counts_are_rejected(int frameCount)
    {
        var options = CreateOptions(frameCount);

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Draft_keys_are_isolated_by_party_game_round_and_player()
    {
        var first = CreateOptions(3);
        var second = new DrawingControllerOptions
        {
            Identity = first.Identity with { PlayerId = Guid.NewGuid() }
        };

        Assert.NotEqual(first.DraftKey, second.DraftKey);
    }

    private static DrawingControllerOptions CreateOptions(int frameCount) => new()
    {
        Identity = new DrawingDraftIdentity(
            Guid.NewGuid(), Guid.NewGuid(), "round-1", Guid.NewGuid()),
        FrameCount = frameCount
    };
}

using Quizizzo.Domain.Parties;

namespace Quizizzo.Domain.Tests;

public sealed class RoomCodeTests
{
    [Theory]
    [InlineData("k7xm", "K7XM")]
    [InlineData(" B4TP ", "B4TP")]
    public void Parse_normalizes_valid_codes(string input, string expected)
    {
        Assert.Equal(expected, RoomCode.Parse(input).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDE")]
    [InlineData("OABC")]
    [InlineData("IABC")]
    [InlineData("L123")]
    [InlineData("A0BC")]
    [InlineData("A1BC")]
    [InlineData("A-BC")]
    public void TryCreate_rejects_invalid_or_ambiguous_codes(string input)
    {
        Assert.False(RoomCode.TryCreate(input, out _));
    }
}

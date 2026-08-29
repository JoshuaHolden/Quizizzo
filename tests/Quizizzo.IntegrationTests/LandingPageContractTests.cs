namespace Quizizzo.IntegrationTests;

public sealed class LandingPageContractTests
{
    [Fact]
    public void Home_uses_a_dedicated_product_landing_layout()
    {
        var home = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/Home.razor");
        var layout = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Layout/LandingLayout.razor");

        Assert.Contains("@layout Quizizzo.Web.Components.Layout.LandingLayout", home,
            StringComparison.Ordinal);
        Assert.Contains("Big-screen chaos.", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/join\"", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/host\"", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/display\"", home, StringComparison.Ordinal);
        Assert.Contains("href=\"#main-content\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("<NavMenu", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Landing_motion_is_progressive_and_respects_user_preferences()
    {
        var app = ReadRepositoryFile("src/Quizizzo.Web/Components/App.razor");
        var homeCss = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/Home.razor.css");
        var motion = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/landingMotion.js");

        Assert.Contains("js/landingMotion.js", app, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", homeCss, StringComparison.Ordinal);
        Assert.Contains("forced-colors: active", homeCss, StringComparison.Ordinal);
        Assert.Contains("matchMedia(\"(prefers-reduced-motion: reduce)\")", motion,
            StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame", motion, StringComparison.Ordinal);
        Assert.Contains("AbortController", motion, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_actions_use_outcome_oriented_labels()
    {
        var sourceFiles = new[]
        {
            "src/Quizizzo.Games.Estimate/EstimateGameModule.cs",
            "src/Quizizzo.Games.MajorityRules/MajorityRulesGameModule.cs",
            "src/Quizizzo.Games.Bullshit/BullshitGameModule.cs",
            "src/Quizizzo.Games.AniMates/AniMatesGameModule.cs",
        };
        var source = string.Join(Environment.NewLine, sourceFiles.Select(ReadRepositoryFile));

        Assert.Contains("\"Lock in my guess\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Send my answer\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Send my bluff\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Send my animation\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Cast my vote\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Submit answer\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Submit bluff\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Submit animation\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_controller_labels_are_bound_as_expressions_not_rendered_as_source_text()
    {
        var playerPage = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        var choice = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/ChoiceController.razor");
        var vote = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/VoteController.razor");

        Assert.Equal(5, CountOccurrences(
            playerPage,
            "SubmitLabel=\"@game.Controller.SubmitLabel\""));
        Assert.DoesNotContain(
            "SubmitLabel=\"game.Controller.SubmitLabel\"",
            playerPage,
            StringComparison.Ordinal);
        Assert.Contains("SubmitLabel=\"@SubmitLabel\"", choice, StringComparison.Ordinal);
        Assert.Contains("SubmitLabel=\"@SubmitLabel\"", vote, StringComparison.Ordinal);
    }

    [Fact]
    public void Number_controller_enables_its_action_while_the_player_types()
    {
        var numberController = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/NumberController.razor");

        Assert.Contains("@bind:event=\"oninput\"", numberController, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_controller_transitions_move_focus_to_the_new_state_heading()
    {
        var playerPage = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");

        Assert.Contains("<h1 @ref=\"pageHeading\" tabindex=\"-1\">", playerPage,
            StringComparison.Ordinal);
        Assert.Contains("previousControllerKind != nextControllerKind", playerPage,
            StringComparison.Ordinal);
        Assert.Contains("pageHeading.FocusAsync(preventScroll: true)", playerPage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Role_views_present_connection_state_without_transport_jargon()
    {
        var roleViews = string.Join(
            Environment.NewLine,
            ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor"),
            ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/HostParty.razor"),
            ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/PlayRealtime.razor"));

        Assert.DoesNotContain("Realtime:", roleViews, StringComparison.Ordinal);
        Assert.Contains("\"Connected\" => \"Live\"", roleViews, StringComparison.Ordinal);
        Assert.Contains("\"Connected\" => \"You're live\"", roleViews, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Quizizzo.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new DirectoryNotFoundException("The Quizizzo repository root was not found.");
        }
        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }
        return count;
    }
}

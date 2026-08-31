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
        Assert.Contains("IntersectionObserver", motion, StringComparison.Ordinal);
        Assert.Contains(".scroll-confetti", homeCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Join_routes_share_the_sidebar_free_landing_experience()
    {
        var join = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/Join.razor");
        var joinParty = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/JoinParty.razor");

        Assert.Contains("@layout Quizizzo.Web.Components.Layout.LandingLayout", join,
            StringComparison.Ordinal);
        Assert.Contains("@layout Quizizzo.Web.Components.Layout.LandingLayout", joinParty,
            StringComparison.Ordinal);
        Assert.Contains("join-experience", join, StringComparison.Ordinal);
        Assert.Contains("join-experience", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-designer", joinParty, StringComparison.Ordinal);
        Assert.Contains("name=\"skinTone\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("name=\"trouserLength\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("name=\"shoeColour\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"head\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"body\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"legs\"", joinParty, StringComparison.Ordinal);

        var designer = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/js/avatarDesigner.js");
        Assert.Contains("function setupTabs(form)", designer, StringComparison.Ordinal);
        Assert.Contains("ArrowRight", designer, StringComparison.Ordinal);
        Assert.Contains("ArrowLeft", designer, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_account_routes_share_the_sidebar_free_account_experience()
    {
        var imports = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Account/Pages/_Imports.razor");
        var accountLayout = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Account/Shared/AccountLayout.razor");

        Assert.Contains("AccountLayout", imports, StringComparison.Ordinal);
        Assert.Contains("@layout Quizizzo.Web.Components.Layout.LandingLayout", accountLayout,
            StringComparison.Ordinal);
        Assert.Contains("account-experience", accountLayout, StringComparison.Ordinal);
        Assert.DoesNotContain("<NavMenu", accountLayout, StringComparison.Ordinal);
    }

    [Fact]
    public void Presenter_lab_uses_layered_cc0_assets_and_accessible_motion_controls()
    {
        var page = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PresenterLab.razor");
        var styles = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PresenterLab.razor.css");
        var motion = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/presenterLab.js");
        var licence = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/assets/kenney-presenter/LICENSE.txt");

        Assert.Contains("@page \"/presenter-lab\"", page, StringComparison.Ordinal);
        Assert.Contains("data-presenter-action=\"wave\"", page, StringComparison.Ordinal);
        Assert.Contains("data-presenter-action=\"talk\"", page, StringComparison.Ordinal);
        Assert.Contains("data-presenter-action=\"laugh\"", page, StringComparison.Ordinal);
        Assert.Contains("data-presenter-action=\"fart\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", styles, StringComparison.Ordinal);
        Assert.Contains("new Phaser.Game", motion, StringComparison.Ordinal);
        Assert.Contains("this.add.particles", motion, StringComparison.Ordinal);
        Assert.Contains("this.load.atlasXML", motion, StringComparison.Ordinal);
        Assert.Contains("CC0", licence, StringComparison.OrdinalIgnoreCase);

        foreach (var sheet in new[] { "face", "hair", "pants", "shirts", "shoes", "skin" })
        {
            Assert.True(File.Exists(RepositoryPath(
                $"src/Quizizzo.Web/wwwroot/assets/kenney-presenter/spritesheets/sheet_{sheet}.png")));
            Assert.True(File.Exists(RepositoryPath(
                $"src/Quizizzo.Web/wwwroot/assets/kenney-presenter/spritesheets/sheet_{sheet}.xml")));
        }
    }

    [Fact]
    public void Production_presentation_supports_player_portraits_and_full_body_atlas_rigs()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");

        Assert.Contains("this.load.atlasXML", presentation, StringComparison.Ordinal);
        Assert.Contains("? \"full\"", presentation, StringComparison.Ordinal);
        Assert.Contains(": \"portrait\"", presentation, StringComparison.Ordinal);
        Assert.Contains("rows === 1 ? 575 : 555", presentation, StringComparison.Ordinal);
        Assert.Contains("const resolution = renderResolution(parent)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("Math.min(3, deviceScale * displayScale)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("new ResizeObserver(controller.resizeHandler)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("start(key, elementId, controller.snapshot)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("}, 150)", presentation, StringComparison.Ordinal);
        Assert.Contains(".setOrigin(0, 0)", presentation, StringComparison.Ordinal);
        Assert.Contains("controller.resizeObserver?.disconnect()", presentation,
            StringComparison.Ordinal);
        Assert.Contains("[\"face\", \"hair\", \"pants\", \"shirts\", \"shoes\", \"skin\"]",
            presentation, StringComparison.Ordinal);
        Assert.Contains("`player-${atlas}`", presentation, StringComparison.Ordinal);
        Assert.Contains("presentation === \"Woman\" ? [4, 8]", presentation,
            StringComparison.Ordinal);
        Assert.Contains("Quizizzo Display", presentation, StringComparison.Ordinal);
        Assert.Contains("animatePhaseTransition", presentation, StringComparison.Ordinal);
        Assert.Contains("startsWith(\"Showdown\")", presentation, StringComparison.Ordinal);
        Assert.Contains("classList.add(\"phaser-enhanced\")", presentation,
            StringComparison.Ordinal);

        var designer = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/avatarDesigner.js");
        Assert.Contains("presentation === \"Woman\" ? [4, 8]", designer,
            StringComparison.Ordinal);
        Assert.Contains("[1, 2, 3, 5, 6, 7]", designer, StringComparison.Ordinal);
        Assert.Contains("syncShirtStyles(form)", designer, StringComparison.Ordinal);
        Assert.Contains("Thin: .84, Normal: 1, Thick: 1.16", designer,
            StringComparison.Ordinal);
        Assert.Contains("part.scaleX *= bodyWidth", designer, StringComparison.Ordinal);
        Assert.Contains("part.scaleX *= variants.bodyWidth", presentation,
            StringComparison.Ordinal);
        Assert.Contains("`tint${skin}_head.png`, .5, 0", designer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Host_dashboard_resumes_an_active_party_instead_of_offering_a_conflicting_create_action()
    {
        var dashboard = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/HostDashboard.razor");

        Assert.Contains("if (activeParty is not null)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Resume active party", dashboard, StringComparison.Ordinal);
        Assert.Contains("else\n    {\n        <button", dashboard, StringComparison.Ordinal);
        Assert.Contains("catch (InvalidOperationException)", dashboard, StringComparison.Ordinal);
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

    [Fact]
    public void Host_lobby_uses_a_responsive_control_room_presentation()
    {
        var page = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/HostParty.razor");
        var styles = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/HostParty.razor.css");

        Assert.Contains("host-room-hero", page, StringComparison.Ordinal);
        Assert.Contains("host-lobby-grid", page, StringComparison.Ordinal);
        Assert.Contains("host-game-tile", page, StringComparison.Ordinal);
        Assert.Contains("host-danger-zone", page, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 480px)", styles, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", styles, StringComparison.Ordinal);
        Assert.Contains("forced-colors: active", styles, StringComparison.Ordinal);

        var navigation = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Layout/NavMenu.razor.css");
        var shell = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Layout/MainLayout.razor.css");
        Assert.Contains("flex-direction: row !important", navigation, StringComparison.Ordinal);
        Assert.Contains("brand-spark", navigation, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("15.625rem", shell, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
        => File.ReadAllText(RepositoryPath(relativePath));

    private static string RepositoryPath(string relativePath)
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
        return Path.Combine(directory.FullName, relativePath);
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

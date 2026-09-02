namespace Quizizzo.IntegrationTests;

public sealed class ResponsiveUiContractTests
{
    [Fact]
    public void Application_shell_supports_safe_areas_keyboard_resize_and_keyboard_navigation()
    {
        var app = ReadRepositoryFile("src/Quizizzo.Web/Components/App.razor");
        var layout = ReadRepositoryFile("src/Quizizzo.Web/Components/Layout/MainLayout.razor");
        var css = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");

        Assert.Contains("viewport-fit=cover", app, StringComparison.Ordinal);
        Assert.Contains("interactive-widget=resizes-content", app, StringComparison.Ordinal);
        Assert.Contains("href=\"#main-content\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", layout, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom)", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: clip", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_contract_has_small_width_short_height_and_touch_target_rules()
    {
        var css = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");
        var navigationCss = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Layout/NavMenu.razor.css");
        var reconnectCss = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Layout/ReconnectModal.razor.css");

        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 360px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-height: 600px)", css, StringComparison.Ordinal);
        Assert.Contains("font-size: 1rem !important", css, StringComparison.Ordinal);
        Assert.Contains("height: 44px", navigationCss, StringComparison.Ordinal);
        Assert.Contains("max-width: calc(100vw - 2rem)", reconnectCss,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Dense_and_full_screen_views_have_bounded_overflow_paths()
    {
        var css = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");
        var passkeys = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Account/Pages/Manage/Passkeys.razor");
        var externalLogins = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Account/Pages/Manage/ExternalLogins.razor");

        Assert.Contains("overflow-y: auto", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(auto-fit, minmax(min(100%",
            css, StringComparison.Ordinal);
        Assert.Contains("class=\"table-responsive\"", passkeys, StringComparison.Ordinal);
        Assert.Contains("class=\"table-responsive\"", externalLogins,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Drawing_and_animation_contracts_cover_phone_input_and_single_frames()
    {
        var css = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");
        var animation = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/FrameAnimation.razor");

        Assert.Contains("touch-action: none", css, StringComparison.Ordinal);
        Assert.Contains("width: min(100%, 68dvh)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(5, minmax(0, 1fr))",
            css, StringComparison.Ordinal);
        Assert.Contains("FrameAssetIds.Count == 1 ? \"single-frame\"", animation,
            StringComparison.Ordinal);
        Assert.Contains(".frame-animation.single-frame img", css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Phone_join_and_game_controllers_use_a_non_scrolling_viewport_shell()
    {
        var css = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");
        var joinCss = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/css/join-experience.css");
        var controllerLayout = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Layout/ControllerLayout.razor");
        var join = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/Join.razor");
        var designer = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/JoinParty.razor");
        var play = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/Play.razor");

        Assert.Contains("phone-controller-shell", controllerLayout, StringComparison.Ordinal);
        Assert.Contains("height: 100dvh", css, StringComparison.Ordinal);
        Assert.Contains("body:has(.phone-controller-shell)", css, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", css, StringComparison.Ordinal);
        Assert.Contains(".phone-controller-shell .join-experience", joinCss,
            StringComparison.Ordinal);
        Assert.Contains("linear-gradient(145deg,#120725", joinCss,
            StringComparison.Ordinal);
        Assert.Contains("color:#f7f3ff;font-weight:850", joinCss,
            StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:repeat(6", joinCss,
            StringComparison.Ordinal);
        Assert.Contains("Layout.ControllerLayout", join, StringComparison.Ordinal);
        Assert.Contains("Layout.ControllerLayout", designer, StringComparison.Ordinal);
        Assert.Contains("Layout.ControllerLayout", play, StringComparison.Ordinal);
        Assert.Equal(6, CountOccurrences(designer, "data-avatar-tab="));
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(needle, startIndex,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += needle.Length;
        }
        return count;
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
}

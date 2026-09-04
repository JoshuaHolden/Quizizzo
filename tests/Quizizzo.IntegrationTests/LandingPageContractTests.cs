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
        Assert.DoesNotContain("href=\"/display\"", home, StringComparison.Ordinal);
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
    public void Join_routes_share_the_single_viewport_controller_experience()
    {
        var join = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/Join.razor");
        var joinParty = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/JoinParty.razor");

        Assert.Contains("@layout Quizizzo.Web.Components.Layout.ControllerLayout", join,
            StringComparison.Ordinal);
        Assert.Contains("@layout Quizizzo.Web.Components.Layout.ControllerLayout", joinParty,
            StringComparison.Ordinal);
        Assert.Contains("join-experience", join, StringComparison.Ordinal);
        Assert.Contains("join-experience", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-designer", joinParty, StringComparison.Ordinal);
        Assert.Contains("name=\"skinTone\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("name=\"trouserLength\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("name=\"shoeColour\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"basics\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"hair\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"eyes\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"face\"", joinParty, StringComparison.Ordinal);
        Assert.Contains("data-avatar-tab=\"top\"", joinParty, StringComparison.Ordinal);
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
        var rig = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/playerCharacterRig.js");
        var app = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/App.razor");

        Assert.Contains("quizizzoCharacterRig.loadAtlases(this, \"player-\")", presentation,
            StringComparison.Ordinal);
        Assert.Contains("quizizzoCharacterRig.create", presentation, StringComparison.Ordinal);
        Assert.Contains("? \"full\"", presentation, StringComparison.Ordinal);
        Assert.Contains(": \"portrait\"", presentation, StringComparison.Ordinal);
        Assert.Contains("rows === 1 ? 575 : 555", presentation, StringComparison.Ordinal);
        Assert.Contains("const resolution = renderResolution(parent)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("Math.min(3, deviceScale * displayScale)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("new ResizeObserver(controller.resizeHandler)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("controller.snapshot,", presentation,
            StringComparison.Ordinal);
        Assert.Contains("mode: Phaser.Scale.ENVELOP", presentation, StringComparison.Ordinal);
        Assert.Contains("RequestPlayerRemoval", presentation, StringComparison.Ordinal);
        Assert.Contains("}, 150)", presentation, StringComparison.Ordinal);
        Assert.Contains(".setOrigin(0, 0)", presentation, StringComparison.Ordinal);
        Assert.Contains("controller.resizeObserver?.disconnect()", presentation,
            StringComparison.Ordinal);
        Assert.Contains("Quizizzo Display", presentation, StringComparison.Ordinal);
        Assert.Contains("animatePhaseTransition", presentation, StringComparison.Ordinal);
        Assert.Contains("startsWith(\"Showdown\")", presentation, StringComparison.Ordinal);
        Assert.Contains("The Phaser display did not initialise.", presentation,
            StringComparison.Ordinal);

        var designer = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/avatarDesigner.js");
        Assert.Contains("quizizzoCharacterRig.loadAtlases(this, \"designer-\")", designer,
            StringComparison.Ordinal);
        Assert.Contains("quizizzoCharacterRig.create", designer, StringComparison.Ordinal);
        Assert.Contains("presentation === \"Woman\" ? [4, 8]", rig,
            StringComparison.Ordinal);
        Assert.Contains("[1, 2, 3, 5, 6, 7]", rig, StringComparison.Ordinal);
        Assert.Contains("syncShirtStyles(form)", designer, StringComparison.Ordinal);
        Assert.Contains("Thin: .84, Normal: 1, Regular: 1, Thick: 1.16", rig,
            StringComparison.Ordinal);
        Assert.Contains("part.scaleX *= variants.bodyWidth", rig, StringComparison.Ordinal);
        Assert.Contains("neck.png`, .5, 0).setScale(.42, 1)", rig,
            StringComparison.Ordinal);
        Assert.Contains("signature === this.podiumSignature", presentation,
            StringComparison.Ordinal);
        Assert.Contains("this.tweens.killTweensOf(this.podiumContainer.getAll())", presentation,
            StringComparison.Ordinal);
        Assert.Contains("if (!podiumChanged)", presentation, StringComparison.Ordinal);
        Assert.Contains("`tint${variants.skin}_head.png`, .5, 0", rig,
            StringComparison.Ordinal);
        Assert.Contains("scheduleRareFart", designer, StringComparison.Ordinal);
        Assert.Contains("Phaser.Math.Between(30000, 55000)", designer,
            StringComparison.Ordinal);
        Assert.Contains("resumeIdle: true", designer, StringComparison.Ordinal);
        Assert.Contains("playerCharacterRig.js", app, StringComparison.Ordinal);
        Assert.True(app.IndexOf("playerCharacterRig.js", StringComparison.Ordinal) <
            app.IndexOf("phaserPresentation.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_entry_point_launches_the_single_active_party_directly_on_the_display()
    {
        var endpoint = ReadRepositoryFile(
            "src/Quizizzo.Web/Endpoints/HostDisplayEndpoints.cs");

        Assert.Contains("MapGet(\"/host\", LaunchAsync)", endpoint, StringComparison.Ordinal);
        Assert.Contains("parties.GetActiveAsync(hostUserId", endpoint, StringComparison.Ordinal);
        Assert.Contains("parties.CreateAsync(hostUserId", endpoint, StringComparison.Ordinal);
        Assert.Contains("Results.Redirect(\"/display\")", endpoint, StringComparison.Ordinal);
        Assert.False(File.Exists(RepositoryPath(
            "src/Quizizzo.Web/Components/Pages/HostDashboard.razor")));
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
    public void AniMates_showdown_uses_an_adaptive_six_entry_gallery_and_compact_player_cards()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");

        Assert.Contains("startShowdownGallery(drawing)", presentation, StringComparison.Ordinal);
        Assert.Contains("Math.min(3, animations.length)", presentation, StringComparison.Ordinal);
        Assert.Contains("cards.forEach(card =>", presentation, StringComparison.Ordinal);
        Assert.Contains("const compactShowdown", presentation, StringComparison.Ordinal);
        Assert.Contains("Math.floor(250 / Math.max(10, player.displayName.length))", presentation,
            StringComparison.Ordinal);
        Assert.Contains("setVisible(isThinking)", presentation, StringComparison.Ordinal);
        Assert.Contains("mode === \"full\" ? .31 : .4", presentation, StringComparison.Ordinal);
        Assert.Contains("avatar.shadow.setY(mode === \"full\" ? 12 : 62)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("const y = podiumTop - 1", presentation, StringComparison.Ordinal);
        Assert.Contains("const name = this.add.text(0, 47", presentation, StringComparison.Ordinal);
        Assert.Contains("const score = this.add.text(0, 75", presentation, StringComparison.Ordinal);
        Assert.Contains("configureHost", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void Round_interstitial_reveals_rankings_and_counts_scores_sequentially()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var rig = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/playerCharacterRig.js");

        Assert.Contains("showRoundRanking", presentation, StringComparison.Ordinal);
        Assert.Contains("That's another round over — let's see how the scores look!",
            presentation, StringComparison.Ordinal);
        Assert.Contains("That's AniMates! Let's crown our animation champions!",
            presentation, StringComparison.Ordinal);
        Assert.Contains("snapshot.phase === \"FinalCelebration\"", presentation,
            StringComparison.Ordinal);
        Assert.Contains("ANIMATES CHAMPIONS", presentation, StringComparison.Ordinal);
        Assert.Contains("addFinalStatistics(snapshot.statistics || [], items)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("FASTEST ANIMATOR", ReadRepositoryFile(
            "src/Quizizzo.Games.AniMates/AniMatesGameModule.cs"), StringComparison.Ordinal);
        Assert.Contains("CURRENT STANDINGS", presentation, StringComparison.Ordinal);
        Assert.Contains("countRoundScores(snapshot, signature)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("right.rank - left.rank", presentation, StringComparison.Ordinal);
        Assert.Contains("this.scoreLabel(Math.round(counter.value), snapshot)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("delay += duration + 240", presentation, StringComparison.Ordinal);
        Assert.Contains("? \"celebrate\"", presentation, StringComparison.Ordinal);
        Assert.Contains("? \"cry\" : \"idle\"", presentation, StringComparison.Ordinal);
        Assert.Contains("action === \"celebrate\"", rig, StringComparison.Ordinal);
        Assert.Contains("action === \"cry\"", rig, StringComparison.Ordinal);
        Assert.Contains("action === \"fart\"", rig, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (difference > 0) this.burst(avatar.container.x, avatar.container.y + 25, 18)",
            presentation, StringComparison.Ordinal);
        Assert.Contains("parts.armLeft?.setAngle(-18)", rig, StringComparison.Ordinal);
        Assert.Contains("duration: 2200", rig, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_is_canvas_only_and_reports_an_unsupported_browser()
    {
        var display = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor");
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var component = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/PhaserPresentation.razor");

        Assert.DoesNotContain("display-overlay", display, StringComparison.Ordinal);
        Assert.DoesNotContain("drawing-display-fallback", display, StringComparison.Ordinal);
        Assert.DoesNotContain("FrameAnimation", display, StringComparison.Ordinal);
        Assert.Contains("Unsupported browser", display, StringComparison.Ordinal);
        Assert.Contains("Failed=\"HandlePhaserFailedAsync\"", display, StringComparison.Ordinal);
        Assert.Contains("public EventCallback Failed", component, StringComparison.Ordinal);
        Assert.Contains("PresentationFailed", component, StringComparison.Ordinal);
        Assert.Contains("applyScreenChrome(snapshot)", presentation, StringComparison.Ordinal);
        Assert.Contains("snapshot.joinQrDataUri", presentation, StringComparison.Ordinal);
        Assert.Contains("joinLink.setInteractive({ useHandCursor: true })", presentation,
            StringComparison.Ordinal);
        Assert.Contains("window.open(snapshot.joinUrl, \"_blank\", \"noopener,noreferrer\")",
            presentation, StringComparison.Ordinal);
        Assert.Contains("addEntryCards(snapshot, items)", presentation, StringComparison.Ordinal);
        Assert.Contains("this.clearPhaseChrome();", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void AniMates_answer_stage_separates_the_taped_animation_from_the_answer_board()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");

        Assert.Contains("const targetX = hasSideCards ? 285 : width / 2", presentation,
            StringComparison.Ordinal);
        Assert.Contains("const tapeLeftShadow", presentation, StringComparison.Ordinal);
        Assert.Contains("addAniMatesSideEntries(snapshot, entries.slice(0, 6), items)",
            presentation, StringComparison.Ordinal);
        Assert.Contains("PICK YOUR ANSWER", presentation, StringComparison.Ordinal);
        Assert.Contains("board.fillRoundedRect", presentation, StringComparison.Ordinal);
        Assert.Contains("const compactAnswerStage", presentation, StringComparison.Ordinal);
        Assert.Contains("compactAnswerStage ? 620", presentation, StringComparison.Ordinal);
        Assert.Contains("const compactShowdownHeader", presentation, StringComparison.Ordinal);
        Assert.Contains("snapshot.phase === \"ShowdownResults\" && snapshot.drawing?.animations?.length",
            presentation, StringComparison.Ordinal);
        Assert.Contains("card.setScale(.8).setAlpha(0)", presentation, StringComparison.Ordinal);
        Assert.Contains("targets: card", presentation, StringComparison.Ordinal);
        Assert.Contains("scale: 1", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("targets: [shadow, panel, frame, caption, badge], scale: 1.12",
            presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void AniMates_briefings_use_the_talking_rig_and_configurable_per_frame_timer()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var rig = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/playerCharacterRig.js");
        var display = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor");
        var module = ReadRepositoryFile(
            "src/Quizizzo.Games.AniMates/AniMatesGameModule.cs");

        Assert.Contains("isBriefing ? 1.42 : .68", presentation, StringComparison.Ordinal);
        Assert.Contains("host.rig.play(isBriefing ? \"talk\" : \"idle\")", presentation,
            StringComparison.Ordinal);
        Assert.Contains("action === \"talk\"", rig, StringComparison.Ordinal);
        Assert.Contains("parts.armLeft?.setAngle(-18)", rig, StringComparison.Ordinal);
        Assert.Contains("Drawing time per frame", display, StringComparison.Ordinal);
        Assert.Contains("DefaultDrawingSecondsPerFrame", display, StringComparison.Ordinal);
        Assert.Contains("new AniMatesGameConfiguration(aniMatesDrawingSecondsPerFrame)", display,
            StringComparison.Ordinal);
        Assert.Contains("(long)FrameCount(state) * EffectiveDrawingSecondsPerFrame(state)", module,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AniMates_phone_votes_are_reviewed_in_a_looping_modal_before_submission()
    {
        var voteController = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/VoteController.razor");
        var optionController = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/OptionController.razor");

        Assert.Contains("ReviewBeforeSubmit=\"HasFrameAnimations\"", voteController,
            StringComparison.Ordinal);
        Assert.Contains("option.FrameAssetIds is { Count: > 0 }", voteController,
            StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", optionController, StringComparison.Ordinal);
        Assert.Contains("Lock in Animation @review.Label", optionController, StringComparison.Ordinal);
        Assert.Contains("<FrameAnimation FrameAssetIds=\"review.FrameAssetIds\"", optionController,
            StringComparison.Ordinal);
        Assert.Contains("private void CloseReview()", optionController, StringComparison.Ordinal);

        var playerPage = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        Assert.Contains("SubmitSelectionAsync(voteActionKind, voteSelectionProperty, optionId)", playerPage,
            StringComparison.Ordinal);
        Assert.Contains("new Dictionary<string, string> { [propertyName] = optionId }", playerPage,
            StringComparison.Ordinal);
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
    public void Party_lobby_surfaces_cumulative_scores_and_durable_game_wins()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var display = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor");
        var player = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");

        Assert.Contains("player.totalWins", presentation, StringComparison.Ordinal);
        Assert.Contains("Party standings", display, StringComparison.Ordinal);
        Assert.Contains("FormatWinBreakdown", display, StringComparison.Ordinal);
        Assert.Contains("Party score", player, StringComparison.Ordinal);
        Assert.Contains("player.TotalWins", player, StringComparison.Ordinal);
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
    public void Player_controllers_are_isolated_by_game_phase_and_action()
    {
        var playerPage = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");

        Assert.Equal(7, CountOccurrences(playerPage, "@key=\"ControllerRenderKey\""));
        Assert.Contains("case PlayerControllerKind.Recording", playerPage, StringComparison.Ordinal);
        Assert.Contains("case PlayerControllerKind.Rhythm", playerPage, StringComparison.Ordinal);
        Assert.Contains("<AntiforgeryToken />", playerPage, StringComparison.Ordinal);
        Assert.Contains("gameView.GameInstanceId.ToString(\"N\")", playerPage,
            StringComparison.Ordinal);
        Assert.Contains("gameView.Phase", playerPage, StringComparison.Ordinal);
        Assert.Contains("game.Controller.Kind", playerPage, StringComparison.Ordinal);
        Assert.Contains("game.Controller.ActionKind", playerPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Arcade_controller_uses_compact_holdable_controls_without_form_submission_locking()
    {
        var playerPage = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        var controller = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/ArcadeController.razor");
        var styles = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");

        Assert.Contains("case PlayerControllerKind.Arcade", playerPage, StringComparison.Ordinal);
        Assert.Contains("ConnectionKey=\"@partyConnection!.ConnectionKey\"", playerPage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Submitted=\"SubmitArcadeAsync\"", playerPage, StringComparison.Ordinal);
        Assert.Contains("./js/arcadeController.js", controller, StringComparison.Ordinal);
        Assert.Contains("data-arcade-input", controller, StringComparison.Ordinal);
        Assert.Contains("data-arcade-arena", controller, StringComparison.Ordinal);
        Assert.Contains("pendingInputs", ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/arcadeController.js"), StringComparison.Ordinal);
        Assert.Contains("SubmitArcadeAction", ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/arcadeController.js"), StringComparison.Ordinal);
        Assert.Contains("HoldRepeatMilliseconds", controller, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", controller, StringComparison.Ordinal);
        Assert.Contains(".phone-controller-shell .arcade-control-deck", styles,
            StringComparison.Ordinal);
        Assert.Contains("touch-action: none", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(6, minmax(44px, 1fr))", styles,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Player_view_presents_connection_state_without_transport_jargon()
    {
        var roleView = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");

        Assert.DoesNotContain("Realtime:", roleView, StringComparison.Ordinal);
        Assert.Contains("\"Connected\" => \"You're live\"", roleView, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_contains_the_responsive_host_control_room()
    {
        var page = ReadRepositoryFile("src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor");
        var styles = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");

        Assert.Contains("display-host-controls", page, StringComparison.Ordinal);
        Assert.Contains("display-host-game-catalogue", page, StringComparison.Ordinal);
        Assert.Contains("display-host-game-queue", page, StringComparison.Ordinal);
        Assert.Contains("Party playlist", page, StringComparison.Ordinal);
        Assert.Contains("Play now", page, StringComparison.Ordinal);
        Assert.Contains("Host controls", page, StringComparison.Ordinal);
        Assert.Contains("Close party", page, StringComparison.Ordinal);
        Assert.Contains("Chart difficulty", page, StringComparison.Ordinal);
        Assert.Contains("Solo autoplay test", page, StringComparison.Ordinal);
        Assert.Contains("requires exactly one joined player", page, StringComparison.Ordinal);
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

    [Fact]
    public void Player_cards_and_reactions_stay_visible_and_controller_errors_are_transient()
    {
        var player = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        var display = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/DisplayRealtime.razor");
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var hub = ReadRepositoryFile("src/Quizizzo.Web/Realtime/PartyHub.cs");

        Assert.Contains("SendReactionAsync(\"Poop\")", player, StringComparison.Ordinal);
        Assert.Contains("SendReactionAsync(\"Fake\")", player, StringComparison.Ordinal);
        Assert.Contains("SendReactionAsync(\"Unsubscribe\")", player, StringComparison.Ordinal);
        Assert.Contains("SendReactionAsync(\"Report\")", player, StringComparison.Ordinal);
        Assert.Contains("player-reaction-trigger", player, StringComparison.Ordinal);
        Assert.Contains("player-reaction-popover", player, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"@reactionsOpen\"", player, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(4)", player, StringComparison.Ordinal);
        Assert.Contains("controller-notice", player, StringComparison.Ordinal);
        Assert.Contains("\"Poop\"", hub, StringComparison.Ordinal);
        Assert.Contains("Poop: \"💩\"", presentation, StringComparison.Ordinal);
        Assert.Contains("Report: \"REPORT THIS SLOP\"", presentation, StringComparison.Ordinal);
        Assert.Contains("setDepth(200)", presentation, StringComparison.Ordinal);
        Assert.Contains("add.container(78, -78", presentation, StringComparison.Ordinal);
        Assert.Contains("fontSize: \"16px\"", presentation, StringComparison.Ordinal);
        Assert.Contains("CanManagePlayers=\"HasHostControls\"", display, StringComparison.Ordinal);
        Assert.DoesNotContain("display-player-removal-layer", display, StringComparison.Ordinal);
    }

    [Fact]
    public void Slop_machine_media_stays_bounded_on_phone_and_shared_display_surfaces()
    {
        var player = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        var options = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/OptionController.razor");
        var styles = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");

        Assert.Contains("controller-game-media", player, StringComparison.Ordinal);
        Assert.Contains("item.AlternativeText", player, StringComparison.Ordinal);
        Assert.Contains("controller-option-image", options, StringComparison.Ordinal);
        Assert.Contains("max-height: min(32dvh, 22rem)", styles, StringComparison.Ordinal);
        Assert.Contains(".phone-controller-shell .player-game-panel", styles, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden", styles, StringComparison.Ordinal);
        Assert.Contains("snapshot.gameKey === \"slop-machine\"", presentation, StringComparison.Ordinal);
        Assert.Contains("addSlopSideEntries", presentation, StringComparison.Ordinal);
        Assert.Contains("addSlopCommentFeed", presentation, StringComparison.Ordinal);
        Assert.Contains("snapshot.media.mode === \"comment-feed\"", presentation,
            StringComparison.Ordinal);
        Assert.Contains("this.tweens.addCounter", presentation, StringComparison.Ordinal);
        Assert.Contains("loadMediaTexture", presentation, StringComparison.Ordinal);
        Assert.Contains("fitImageWithin(image, cardWidth - 24, imageHeight)", presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("setDisplaySize(cardWidth - 24, imageHeight)", presentation,
            StringComparison.Ordinal);
        Assert.Contains("object-fit: contain", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Slop_machine_roulette_uses_structured_blanks_and_player_refreshes_cannot_regress()
    {
        var player = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Pages/PlayRealtime.razor");
        var textController = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/TextController.razor");
        var options = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/OptionController.razor");

        Assert.Contains("Configuration.FormatSegments", textController, StringComparison.Ordinal);
        Assert.Contains("format-answer-input", textController, StringComparison.Ordinal);
        Assert.Contains("new TextControllerSubmission(CompletedValue, blankValues)", textController,
            StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref loadVersion)", player, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref loadVersion)", player, StringComparison.Ordinal);
        Assert.Contains("reviewedOption = null", options, StringComparison.Ordinal);
    }

    [Fact]
    public void Display_audio_maps_Slop_Machine_and_AniMates_states_with_a_persistent_mute_control()
    {
        var audio = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/presentationAudio.js");
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var component = ReadRepositoryFile(
            "src/Quizizzo.Web/Components/Shared/PhaserPresentation.razor");
        var app = ReadRepositoryFile("src/Quizizzo.Web/Components/App.razor");
        var css = ReadRepositoryFile("src/Quizizzo.Web/wwwroot/app.css");

        Assert.Contains("quiz-show-sparkle.774e332653a6.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("countdown-to-zero.fd84e59f102d.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("/media/audio/games/slop-machine", audio, StringComparison.Ordinal);
        Assert.Contains("/media/audio/games/pile-up-panic", audio, StringComparison.Ordinal);
        Assert.Contains("falling-block-fever.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("trackKey: \"pileUp\"", audio, StringComparison.Ordinal);
        Assert.Contains("slop-lobby.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-writing.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-countdown.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-spinner.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-voting.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-telephone.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-comments.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-scoreboard.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-final.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-human-victory.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("slop-machine-victory.mp3", audio, StringComparison.Ordinal);
        Assert.Contains("countdownWindowSeconds = 20", audio, StringComparison.Ordinal);
        Assert.Contains("snapshot.gameKey === \"animates\" && snapshot.phase === \"Drawing\"", audio,
            StringComparison.Ordinal);
        Assert.Contains("crossfadeMilliseconds: 600", audio, StringComparison.Ordinal);
        Assert.Contains("duckedMusicMultiplier: .4", audio, StringComparison.Ordinal);
        Assert.Contains("quizizzo.display.audio-muted", audio, StringComparison.Ordinal);
        Assert.Contains("controller.audio?.update(controller.snapshot)", presentation, StringComparison.Ordinal);
        Assert.Contains("display-audio-toggle", component, StringComparison.Ordinal);
        Assert.Contains("data-room-code=\"@Snapshot.RoomCode\"", component, StringComparison.Ordinal);
        Assert.Contains("Enable sound", component, StringComparison.Ordinal);
        Assert.Contains("left: max(1rem, env(safe-area-inset-left))", css,
            StringComparison.Ordinal);
        Assert.Contains("linear-gradient(110deg, rgb(255 79 163 / 92%)", css,
            StringComparison.Ordinal);
        Assert.True(
            app.IndexOf("presentationAudio.js", StringComparison.Ordinal) <
            app.IndexOf("phaserPresentation.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Pile_up_display_uses_server_owned_geometry_and_responsive_phaser_arenas()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var mapper = ReadRepositoryFile(
            "src/Quizizzo.Web/Presentation/PhaserPresentationSnapshot.cs");

        Assert.Contains("snapshot.gameKey === \"pile-up-panic\"", presentation,
            StringComparison.Ordinal);
        Assert.Contains("applyPileUp(snapshot, initial)", presentation, StringComparison.Ordinal);
        Assert.Contains("field(state, \"clusterShapes\", {})", presentation,
            StringComparison.Ordinal);
        Assert.Contains("field(arena, \"grid\", []).forEach(cell => drawCell(graphics, cell))", presentation,
            StringComparison.Ordinal);
        Assert.Contains("duration: 60", presentation, StringComparison.Ordinal);
        Assert.Contains("previousActive", presentation, StringComparison.Ordinal);
        Assert.Contains("const cellSize = Math.min(count === 2", presentation,
            StringComparison.Ordinal);
        Assert.Contains("this.playPileAvatar(avatar,", presentation,
            StringComparison.Ordinal);
        Assert.Contains("rank === 1 ? \"celebrate\"", presentation,
            StringComparison.Ordinal);
        Assert.Contains("this.controller.reducedMotion", presentation, StringComparison.Ordinal);
        Assert.Contains("JsonElement? GameState = null", mapper, StringComparison.Ordinal);
        Assert.Contains("game?.State", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void Realtime_display_rebinds_only_for_pairing_changes()
    {
        var realtime = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/partyRealtime.js");

        Assert.Contains(
            "role === \"Display\" && message.reason === \"DisplayPaired\"",
            realtime,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VoiceChoon_display_is_a_beat_driven_character_music_video()
    {
        var presentation = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/phaserPresentation.js");
        var rig = ReadRepositoryFile(
            "src/Quizizzo.Web/wwwroot/js/playerCharacterRig.js");
        var state = ReadRepositoryFile(
            "src/Quizizzo.Games.VoiceChoon/VoiceChoonGameState.cs");

        Assert.Contains("bowLegged", presentation, StringComparison.Ordinal);
        Assert.Contains("armFlap", presentation, StringComparison.Ordinal);
        Assert.Contains("fistPump", presentation, StringComparison.Ordinal);
        Assert.Contains("discoPoint", presentation, StringComparison.Ordinal);
        Assert.Contains("rubberRobot", presentation, StringComparison.Ordinal);
        Assert.Contains("action === \"dazed\"", rig, StringComparison.Ordinal);
        Assert.Contains("★", rig, StringComparison.Ordinal);
        Assert.Contains("strokeCircle", presentation, StringComparison.Ordinal);
        Assert.Contains("fillTriangle", presentation, StringComparison.Ordinal);
        Assert.Contains("HIT STREAK", presentation, StringComparison.Ordinal);
        Assert.Contains("beatSeconds", presentation, StringComparison.Ordinal);
        Assert.Contains("TOTAL BAND POINTS", presentation, StringComparison.Ordinal);
        Assert.Contains("TOP SCORED!", presentation, StringComparison.Ordinal);
        Assert.Contains("showingResults && rank === 1 ? \"celebrate\"", presentation,
            StringComparison.Ordinal);
        Assert.Contains("rank === lastRank && lastRank > 1 ? \"cry\"", presentation,
            StringComparison.Ordinal);
        Assert.Contains("missedJudgementIds", presentation, StringComparison.Ordinal);
        Assert.Contains("sourDirection * 175", presentation, StringComparison.Ordinal);
        Assert.Contains("source.detune.value", presentation, StringComparison.Ordinal);
        Assert.Contains("VoiceChoonDisplayPerformer", state, StringComparison.Ordinal);
        Assert.Contains("JudgedNoteIds", state, StringComparison.Ordinal);
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

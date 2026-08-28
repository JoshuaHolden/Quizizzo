# Responsive UI contract

Quizizzo uses one mobile-first layout contract across public pages, account screens, host controls, player controllers, and the accessible display layer. The target is no accidental horizontal page scrolling at 320 CSS pixels or wider. The only intentional horizontal scroll regions are dense account tables and the account-management tab strip; both remain keyboard reachable and expose visible scrollbars when needed.

## Viewport matrix

Changes to shared UI should be checked at these representative viewport classes:

| Experience | Required viewports | Primary checks |
|---|---|---|
| Small phone | 320×568, 360×640 | 44 px controls, readable inputs, no clipped labels, stacked actions |
| Modern phone | 390×844, 430×932 | safe-area padding, drawing canvas and tools, controller reachability |
| Phone landscape | 667×375, 844×390 | short-height compaction, scrolling, drawing controls below the canvas |
| Tablet | 768×1024, 1024×768 | account navigation, host cards, adaptive option grids |
| Desktop | 1280×720 and 1440×900 | bounded readable content and persistent navigation |
| Shared display | 1280×720, 1920×1080, 3840×2160 | 16:9 Phaser scaling, legible HTML fallback, complete results and scores |
| Portrait/short display fallback | 720×1280 and heights below 600 px | scrollable logical state instead of clipped content |

The viewport metadata opts into device safe areas and virtual-keyboard resizing. Layout padding consumes all four safe-area insets, forms use a minimum 16 px mobile font to avoid browser zoom, and interactive controls provide at least a 44×44 CSS-pixel target.

## Role-specific behavior

- Public and account pages use the shared shell with a keyboard skip link, a collapsible 44 px mobile navigation control, fluid gutters, wrapping copy, and width-bounded forms.
- Host roster, result, and recent-party rows remain side-by-side when space permits and stack below 480 px. Game-start and progression actions become full-width on phones.
- Player controllers remain game-neutral. Number, text, choice, vote, waiting, and drawing primitives constrain their own content, preserve visible focus, and never require page-level horizontal scrolling.
- Drawing uses a viewport-height-aware logical canvas so landscape phones can still reach the tool and frame controls. The five primary tools use an equal-width grid; frame controls reflow at narrow widths. Pointer input remains local JavaScript with `touch-action: none` only on the drawing surface.
- The display keeps Phaser full-screen while its reconstructable HTML overlay has an independent vertical overflow path. This preserves results, scores, pairing information, and QR links on portrait or unusually short screens. URLs wrap and QR images are bounded by both width and dynamic viewport height.
- A single-frame drawing is marked explicitly and remains continuously visible; it does not inherit the three-frame opacity cycle.

## Accessibility and regression checks

Visible keyboard focus, a skip link, reduced-motion behavior, forced-colour selection outlines, semantic fieldsets/labels, and live status messages are preserved at every width. Reconnect UI is viewport-bounded and its retry actions use full touch targets.

`ResponsiveUiContractTests` protects viewport metadata, safe-area handling, breakpoint coverage, touch sizes, bounded overflow, responsive tables, drawing constraints, and single-frame presentation. Scoped CSS compilation, the client test suite, strict Release build, and the full .NET suite remain the required verification gate after UI changes.

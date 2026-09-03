# Pile-Up Panic IP design notes

Pile-Up Panic is an independently designed competitive falling-cluster game. This file records engineering precautions; it is not a legal-clearance opinion.

## Deliberate design differences

- The arena is 9 columns by 17 visible rows, with three hidden server-owned spawn rows.
- The complete catalogue has 12 original `scrap cluster` definitions spanning two, three, four, and five cells. Four-cell clusters are a minority rather than a complete traditional seven-shape set.
- Materials come from a separately shuffled eight-colour palette. A cluster has a high-contrast outline in the future renderer, and colour never carries rule information.
- The deterministic generator excludes the four most recently emitted cluster definitions and shuffles materials independently. It is not a seven-item bag.
- Clockwise rotation uses a small generic correction search local to the desired placement. It contains no copied named rotation system or established kick table.
- The arena language and rules use scrap clusters, circuits, junk, chaos charge, and overload.
- Circuit scoring, chaos abilities, opponent targeting, survival ranking, and party-view conversion are original systems documented in the architecture note.
- No hold mechanic is present in the first version.

## Assets and dependencies

Stage 1 contains no image, font, music, sound-effect, or third-party gameplay assets. It uses only project-authored C# rules and the existing Quizizzo project infrastructure. Future placeholders must follow Quizizzo's local asset conventions and record their source and licence here.

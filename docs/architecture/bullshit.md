# Bullshit

Bullshit is a three-round bluffing game and the proof for hidden module state, persisted shuffled choices, the reusable `Choice` controller, and multi-source scoring. Each round follows `Bluffing -> Choosing -> Results`; the final host advance reaches `Completed`.

## Hidden state and shuffled choices

The versioned server snapshot stores each question's truth, bluff ownership, exact-truth flags, shuffled choices, votes, and score breakdown. Role projections are deliberately narrower:

- During bluffing, phones receive the question and a generic bounded text controller. Host and display receive only per-player completion status. No role view contains the truth or submitted text.
- When choosing opens, the server groups equivalent bluffs, creates opaque random choice IDs, includes the truth as an indistinguishable choice, and performs a cryptographic Fisher-Yates shuffle. The chosen order is persisted, so every refresh sees the same order.
- Host, display, and phone choice views contain answer text and opaque IDs but no truth flag or author IDs. A phone omits any grouped bluff authored by that player, and the module separately rejects forged self-choices.
- Results reveal the truth, bluff authors, exact-truth submissions, picks, and payouts.

Submitting the exact truth during bluffing is accepted silently. That player sits out choosing and receives an exact-answer bonus at reveal; the action response cannot be used to probe the hidden truth. Duplicate case-insensitive bluffs share one option, and every co-author receives the bluff payout if it fools another player.

## Advanced scoring

The module can combine three independent awards for one player in one round:

- 1,000 points for choosing the truth.
- 500 points per opponent fooled by the player's bluff.
- 1,000 points for submitting the exact truth while bluffing.

The module aggregates those categories into one `ScoreAward` per player. The shared engine applies it once, records the command result for idempotent retries, and carries cumulative scores across rounds and back to the party.

All semantic actions and UTC deadlines use the existing serialized command channel. Persisted choices and role projections therefore reconstruct consistently after host, display, or player refresh without relying on SignalR history.

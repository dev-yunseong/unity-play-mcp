# Observe / act / verify loop

Six tools let an agent play a running Unity game by looking, choosing, acting with normal
input, and checking what actually happened. They work with no game-specific code.

| Tool | Job |
| --- | --- |
| `get_play_capabilities` | Versions, tools, input paths, physics/UI support, providers, scopes, limits. Fails with `unsupported_capability` on an older package; the older tools keep working. |
| `observe` | One fresh, coherent snapshot: entities, typed facts, action refs, changes since an earlier observation, optionally an image from the same frame. |
| `inspect_action` | One action's input recipe, availability, preconditions, costs, expected effects and outcome predicates. |
| `query_space` | Hit test, entities in a region, world/screen projection, physics line test, in an explicit coordinate space. |
| `act_and_observe` | One finite input plus the observable result, with `execution` and `outcome` kept apart. |
| `watch_events` | Wait once, bounded, for a change worth reacting to. |

The agent decides what to do. The tools organise information, perform a finite normal input, and
measure. They never call arbitrary methods, change health/resources/results, or play by themselves.

## Contract highlights

- **Stamp and handles.** Every result carries `{sessionId, run, reading, scene, frame,
  sampledAtMonotonicMs, gameTimeSeconds}`. Entity refs are `{sessionId, id, generation}`; a reused
  instance id or a reloaded scene never receives input meant for another object.
- **Unknown is not zero.** A fact is `known`, `unknown` or `unsupported`, with `source`
  (`runtime`, `analysis`, `provider`) and evidence. Missing members, wrong types and different units
  make a predicate `unknown`, which never passes.
- **Scopes.** `player` (default) reports what is drawn (UI on screen, visible renderers). Renderer
  visibility is not fog-of-war; `policy.visibilityGuarantee` is `provider` only when a provider
  declares game-rule visibility. `debug` is wider. Credential-like values are always redacted.
- **Freshness.** `observe` samples Unity on every call, even when nothing changed. If the image and
  the state cannot be taken in one frame, `coherent` is `false` with both stamps and `frameDelta`.
  An image from another scene is dropped.
- **Execution vs outcome.** `execution` is what Unity accepted. `outcome` is `confirmed` only when
  your `expect` predicates (or the action's provider outcome predicates) are true; otherwise
  `not_observed` or `unknown`. A late effect never triggers automatic re-input.
- **One owner of the input.** `operationId` makes retries safe (same id and request returns the
  earlier result; a different request conflicts). A second client, or an old input tool, gets
  `busy` while an operation runs. On timeout, cancel, disconnect or scene change only the keys,
  buttons and axes that operation pressed are released.
- **Frames.** Press and release are delivered on different frames. `waitFrames` counts rendered
  frames, so it works while `Time.timeScale` is 0; the wall-clock deadline always applies.

Predicate v1: `all`, `any`, `not`, `entityExists`, `entityAbsent`, `active`, `interactable`,
`member` (`eq ne lt lte gt gte changed`), `fact`, `sceneIs`, `eventMatches`. No expressions or eval.

## Optional semantic providers

A game can add meaning without changing the core. Providers are read-only: they add facts,
relations, action descriptions whose recipes use the existing inputs, and events. Every item
declares `PlayVisibility.Player` or `PlayVisibility.Debug` (default `Debug`). A provider that throws
is isolated; one that exceeds 4 ms three times in a row is disabled and reported.

Example 1: a sliding puzzle exposes which tile is selected and which moves are legal.

```csharp
using UnityEngine;
using UnityPlayMcp.Play;

sealed class PuzzleProvider : PlaySemanticProvider
{
    public override string Id => "puzzle";

    public override void Describe(PlayObservationContext context, PlayProviderOutput output)
    {
        foreach (var entity in context.Entities)
        {
            var tile = entity.GetComponent<Tile>();
            if (tile == null) continue;

            output.AddFact(entity, "selected", tile.IsSelected, null, PlayVisibility.Player);
            var move = new PlayProviderAction
            {
                Id = "slide", Label = "Slide this tile", Visibility = PlayVisibility.Player,
                Available = tile.CanSlide,
            };
            move.Recipe.Add(PlayRecipeStep.PointerClickOn(entity));
            move.OutcomeJson.Add(PlayPredicates.FactChanged("puzzle", "selected"));
            output.AddAction(entity, move);
        }
    }
}

// PlaySemantics.Register(new PuzzleProvider());
```

Example 2: a small combat fixture exposes team, health and range, and emits a domain event.

```csharp
sealed class CombatProvider : PlaySemanticProvider
{
    public override string Id => "combat";

    public override void Describe(PlayObservationContext context, PlayProviderOutput output)
    {
        foreach (var entity in context.Entities)
        {
            var unit = entity.GetComponent<Unit>();
            if (unit == null) continue;

            output.AddFact(entity, "health", unit.Health, "hp", PlayVisibility.Player);
            output.AddFact(entity, "range", unit.Range, "world units", PlayVisibility.Player);
            output.AddRelation(entity, "combat:team", unit.TeamAnchor, PlayVisibility.Player);
        }
    }
}

// Where the game itself decides something meaningful happened:
// PlaySemantics.Emit("combat", "unit_died", "death", unit.gameObject, null, PlayVisibility.Player);
```

Real game classes belong in your game or a test/sample assembly, never in the package core.

## Not covered

- Only the legacy Input API path (the virtual input shim) is driven; the new Input System package is
  not, and `get_play_capabilities` says so.
- Input that a person types while an operation runs cannot be detected. An old observation is
  rejected as stale only when another tool or client ran input after it.
- The MCP server does not run without a Unity package of the same protocol version
  (`get_play_capabilities` reports both).
- `ground/aim projection` providers are not part of this version; use `query_space` with an
  explicit plane or collider.
- Pausing the game, rollback and direct state changes are intentionally absent.

## Where each risk is tested

| Area | Tests |
| --- | --- |
| Predicates, units, ambiguity, unknown | `mcp/test/play-predicate.test.ts` |
| Fresh stamp, cached, diff, budgets, continuation, incoherent image, redaction | `mcp/test/play-observe.test.ts` |
| Preconditions, blockers, idempotency, busy, cancel/disconnect release, scene change, bounded waits | `mcp/test/play-act.test.ts` |
| Event ring buffer, gaps, cursor expiry, timer cleanup, provider events | `mcp/test/play-events.test.ts`, `mcp/test/play-watch.test.ts` |
| Tool contract, capability fallback, no guessed plane, redaction | `mcp/test/play-tools.test.ts` |
| Lock and operation table | `Tests/Runtime/OperationTableTests.cs` |
| Provider isolation, slowness, visibility, events | `Tests/Runtime/PlaySemanticsTests.cs` |
| Handles, observe, sample lifecycles, inspect, space queries, begin/end, events | `Tests/PlayMode/PlayApiTests.cs` |

## Evidence still to collect

These need a Unity editor and are not claimed here: EditMode/PlayMode runs of the C# tests, the
100-call performance comparison (no-image and image), the 5 ms p95 main-thread budget check on a
100-entity fixture, agent transcripts on two different fixtures, and a smoke test on a real game
with one recorded failure.

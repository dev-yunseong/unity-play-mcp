# Coding Style

## Why

Consistent shape lets a reader predict where behavior lives and what a name
means, so review spends its attention on the change instead of the style.

## Principles

Prefer:
- explicit naming
- small functions
- single responsibility
- early return
- composable logic
- deterministic behavior

## Structure

Prefer:
- shallow nesting
- clear boundaries
- modular design
- immutable-oriented flow
- explicit dependencies

Avoid:
- giant classes
- god objects
- util dumping grounds
- hidden side effects
- deep inheritance
- untyped payload maps

## Data Shapes

Model every payload that crosses a boundary — HTTP request and response,
WebSocket frame, queue message, DB row, LLM tool argument — as a declared type:
a Kotlin `data class`, a Pydantic model, a C# serializable class, a TypeScript
interface. Do not thread `JsonNode`, `Map<String, Any>`, `dict[str, Any]`, or
`Record<string, unknown>` through application code.

An untyped map moves every mistake to runtime and to the far end of the call
chain. A misspelled key, a field that quietly changed type, a value nobody
populated — a DTO surfaces these at the parse boundary with the offending field
named; a map surfaces them as a null three layers away, if at all. The DTO is
also the only place the contract is written down.

**Parse once, at the boundary.** Convert the payload into its typed model where
it enters the process, and let everything downstream take that type. Do not
carry raw JSON deeper "just in case".

Raw JSON is the right choice when:
- the code is passthrough — it stores or forwards the payload without reading
  fields (a `jsonb` column, a proxied body)
- the schema is genuinely open at that point (LLM tool arguments as received),
  and the next step validates it into a typed model
- a test asserts on the serialized wire shape

Existing untyped code is not a defect to sweep. Convert a payload when you are
already changing the code that reads it.

## Naming

Names should reveal intent immediately.

Avoid:
- Manager
- Helper
- Utils
- Temp
- Data
- Thing
- Misc

Prefer:
- domain-specific naming
- action-oriented function names
- explicit variable meaning

## Comments

### Language

**Write comments in Korean.** This covers every comment that lives in a source
file: line comments, block comments, and doc comments — KDoc, Python
docstrings, C# XML doc, JSDoc/TSDoc, and SQL/migration comments.

Korean is the language reviewers read. A comment carries the reasoning that the
code cannot, and reasoning written in the reviewer's language is read; reasoning
written in a second language is skimmed.

Keep these in English inside a Korean comment:
- identifiers, type names, API paths, file paths
- library, framework, and tool names
- error codes and exact error strings being quoted

```
// 재시도는 3회까지만 한다. 업스트림이 429를 돌려줄 때 백오프 없이 더 밀면
// 레이트 리밋 창이 갱신되지 않아 복구가 오히려 늦어진다.
```

This rule covers source files only. Documentation, commit messages, and PR
bodies keep the language their own conventions already set — `AGENTS.md`,
`commit.md`, `pull-request.md`.

Existing English comments are not a defect. Rewrite one only when you are
already changing that code; a comment-language sweep is unrelated churn.

### Content

A comment explains a constraint or a non-obvious decision — the rejected
alternative, the failure mode being prevented, the invariant a caller has to
hold. Do not restate what the code already says.

Delete commented-out code instead of parking it. Git history holds it.

## Terminology in comments, documents, and pull requests

Keep a technical term in English, in backticks, even in the middle of a Korean
sentence: `pulse`, `screen`, `capability`, `selector`, `anchor`, `branch`,
`fold`, `evidence`, `wiring`.

Do not invent a Korean substitute for something the code already names. `판독`
for `pulse`, `갈래` for `branch`, `배선` for `wiring`, `선택자` for `selector`,
`근거 문서` for an `evidence` document — none of these.

**How common a coinage is in this repository is not an argument for writing
another one.** Several of them are already widespread here. That is history, not
a standard: it means the habit spread before anyone stopped it, and matching it
spreads it further. When you write a new comment, choose the English word even
when the file beside it does not.

The one exception is a sentence you are editing that already uses the old word,
where changing it would leave a single paragraph speaking two ways. Match the
line you are touching; do not convert the file around it as a side errand.

This is not a push toward more English or more Korean. Prose stays whatever
reads naturally. The rule is narrower than that: a thing the code names keeps
the name the code gave it.

## Refactoring

Prefer:
- incremental refactoring
- localized changes
- preserving existing architecture

Avoid:
- unnecessary rewrites
- unrelated cleanup
- aesthetic-only refactors
- broad formatting churn

## Final Rule

Code should be:
- easy to reason about
- easy to modify
- easy to extend
- easy to debug

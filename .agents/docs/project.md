# Project Context

Fill this document during project initialization. Agents must verify commands against repository configuration before running them.

## Overview

- Product: artel-sdk
- Primary users: TODO
- Core domain: TODO
- Runtime environment: TODO

## Architecture

- Entry points: TODO
- Main modules: TODO
- Dependency direction: TODO
- External systems: GitHub repository `project-artel/artel-sdk`; Notion workspace via the `ntn` CLI
- Persistent data: TODO

## Commands

| Purpose | Command |
|---|---|
| Install dependencies | TODO |
| Run locally | TODO |
| Format | TODO |
| Lint | TODO |
| Type-check | TODO |
| Unit tests | TODO |
| Integration tests | TODO |
| Build | TODO |
| Install Notion CLI | `curl -fsSL https://ntn.dev \| bash` |
| Verify Notion CLI auth | `ntn whoami` |

Notion access goes through the `ntn` CLI. Agents follow
`.agents/skills/notion-cli/SKILL.md`, which Claude Code reaches through the
`.claude -> .agents` symlink as `.claude/skills/notion-cli`.

Authenticate with a token rather than `ntn login`: export `NOTION_API_TOKEN`
from your shell profile, using a token issued at
`https://www.notion.so/profile/integrations`. The integration must be connected
to each page and data source it needs, otherwise reads return 404. Never commit
the token.

Write operations (`ntn pages create`, `ntn files create`, `ntn workers deploy`)
are not pre-approved and require explicit confirmation.

## Constraints

- Supported platforms:
- Compatibility requirements:
- Performance constraints:
- Security or privacy requirements:

## Ownership

- Maintainers:
- Sensitive modules:
- Changes requiring explicit review:

# deadlock-server-plugins — Claude Code instructions

This repo has an LLM-maintained knowledge base at `knowledge-base/`
(Karpathy's LLM Wiki pattern). Use it. Keep it current.

## At session start

Before substantive work on this repo:

1. Read `knowledge-base/wiki/index.md` — the catalog of what the wiki knows.
2. Skim the most recent few entries of `knowledge-base/wiki/log.md`.
3. If the user's task touches a topic with a matching wiki page, read that page
   before exploring the code from scratch.

The full schema — page formats, entity types, and the ingest/query/lint
workflows — lives in `knowledge-base/CLAUDE.md`. Follow it exactly when
touching anything under `knowledge-base/wiki/`.

## During work — capture findings as you go

Whenever you discover something **non-obvious** about this codebase, write a
short note to `knowledge-base/raw/notes/YYYY-MM-DD-<short-slug>.md`. Do this
inline, as you find it — don't wait for end of task.

Qualifies as a finding:

- An architectural fact you had to derive by reading multiple files.
- A gotcha that cost you (or would cost the next agent) time.
- "Why X is the way it is" — a constraint, prior incident, or deliberate choice
  the user explains or that git blame reveals.
- Cross-plugin interactions, shared state, or non-local coupling.
- A subtle behavioural difference between plugins, gamemodes, or environments.

Does NOT qualify (do not write these):

- Anything already on a wiki page — update the page instead via the ingest flow.
- What a function does when its name already says so.
- Your current task state (that's `TaskCreate`'s job, not the wiki's).

Note format (minimal — this is raw material for later ingest, not a polished page):

```markdown
---
date: 2026-04-21
task: short phrase describing what you were doing
files: [LockTimer/LockTimerPlugin.cs, LockTimer/zones.yaml]
---

One-paragraph finding. Link file:line where relevant.
```

## At task end — ingest if notes piled up

If you wrote 2+ notes under `knowledge-base/raw/notes/` during the session, or
touched any topic that has no wiki page yet, ingest them before reporting the
task complete. Follow the 9-step ingest workflow in
`knowledge-base/CLAUDE.md` (source summary → create/update concept/entity/
plugin pages → update index → append to log).

If you only wrote one note and the finding is small, leave it as a raw note —
batch ingest on a later session.

## User-invocable operations

- `/kb-ingest <path>` — ingest a specific raw source or note into the wiki.
- `/kb-query <question>` — answer from the wiki only (falls back to raw if needed).
- `/kb-lint` — health-check the wiki; writes a report to `outputs/`.

## Non-negotiables

- Never edit anything under `knowledge-base/raw/` after it's written. Only add.
- Never invent claims on wiki pages. Everything traces to a `sources:` entry.
- Keep `wiki/index.md` and `wiki/log.md` in sync with every wiki change.

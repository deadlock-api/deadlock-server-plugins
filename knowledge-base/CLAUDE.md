# deadlock-server-plugins Knowledge Base

LLM-maintained wiki for this repo. Implements Karpathy's LLM Wiki pattern:
raw sources are ingested into a compounding, cross-linked markdown wiki that
sits between the human and the source material.

**You (the agent) do the bookkeeping. The human curates sources and asks questions.**

## Directory layout

- `raw/` — Immutable source material. **Never edit files here.** Only add.
  - `raw/articles/` — External posts, docs, specs
  - `raw/code/` — Snapshots or excerpts of repo code worth referencing
  - `raw/commits/` — Exported commit logs or diffs
  - `raw/decisions/` — Raw notes from design discussions, issues, chat logs
  - `raw/notes/` — Free-form captured notes and transcripts
- `wiki/` — LLM-generated and maintained pages. Everything here is fair game to update.
  - `wiki/index.md` — Master catalog. Update on every operation.
  - `wiki/log.md` — Append-only operation log.
  - `wiki/overview.md` — High-level synthesis of the project.
  - `wiki/glossary.md` — Terms, acronyms, naming rules.
  - `wiki/plugins/` — One page per plugin (Deathmatch, LockTimer, StatusPoker, …).
  - `wiki/concepts/` — Cross-cutting ideas (e.g. gamemodes, zones, Proton runtime).
  - `wiki/entities/` — Concrete things: services, APIs, config files, volumes.
  - `wiki/sources/` — One summary per raw source.
  - `wiki/operations/` — Runbooks, deploy flows, CI pipelines.
  - `wiki/comparisons/` — Side-by-side analyses.
- `outputs/` — Reports, lint results. Disposable.

## Page format

Every wiki page (except `log.md`) MUST start with YAML frontmatter:

```yaml
---
title: Human-readable title
type: concept | entity | plugin | source-summary | comparison | operation | overview | index | reference
sources:
  - raw/articles/filename.md
  - ../DeathmatchPlugin/DeathmatchPlugin.cs    # repo-relative paths OK for in-repo code
related:
  - "[[other-page]]"
created: YYYY-MM-DD
updated: YYYY-MM-DD
confidence: high | medium | low
---
```

### Naming and linking

- Filenames: `kebab-case.md` (e.g. `lock-timer-zones.md`).
- Internal links: `[[page-name]]` wikilinks, no `.md` extension.
- When citing a raw source, link to its relative path from repo root.
- When citing repo source code, link to the file and — if pointing at a specific
  symbol — include the line (e.g. `LockTimer/LockTimerPlugin.cs:42`).

## Workflows

### Ingest — when the user adds a source or asks to ingest

1. Read the source in `raw/` (or the in-repo file path they pointed at).
2. Briefly discuss the key takeaways with the user before writing.
3. Create `wiki/sources/<source-slug>.md` summarising the source with frontmatter.
4. Create or update the relevant `concepts/`, `entities/`, `plugins/`, or `operations/` pages.
5. Update `wiki/index.md`: add new pages under the right section, bump `updated:` date.
6. Append an entry to `wiki/log.md` with timestamp, operation, source, pages touched, key findings.
7. Look for new cross-links across existing pages and add `[[wikilinks]]`.
8. Flag any contradictions or gaps discovered — call them out in the log entry.
9. Report to the user: pages created, pages updated, surprises.

### Query — when the user asks a question about the project/domain

1. Read `wiki/index.md` first to navigate.
2. Read only the pages that look relevant — not the whole wiki.
3. Answer with `[[wikilink]]` citations to the pages you used.
4. If the question surfaces new synthesis worth keeping, offer to save it as a new page
   (typically under `wiki/concepts/` or `wiki/comparisons/`).
5. If the wiki can't answer, say so and suggest which raw source to add next.

### Lint — when the user asks to lint, or on cadence

1. Scan every page for contradictions with other pages.
2. Identify orphans: pages with no incoming `[[wikilinks]]` from elsewhere.
3. Find dangling references: `[[wikilinks]]` that point to pages that don't exist.
4. Flag stale claims: any page whose `sources:` have been superseded by newer raw/ entries.
5. Check every page has correct frontmatter and `updated:` is within the last 90 days,
   or a comment explaining why it's intentionally frozen.
6. Write `outputs/lint-YYYY-MM-DD.md` with findings grouped by severity.
7. Do NOT auto-fix — report, then ask the user which fixes to apply.

## Session start checklist

When the user opens a Claude Code session in this directory, or the parent repo with
this folder in context:

1. Read `wiki/index.md` for orientation.
2. Read the last 10 entries of `wiki/log.md` to catch up on recent activity.
3. Wait for the user to name a source to ingest, a question to answer, or a lint pass.

## Non-negotiables

- Never modify files under `raw/`.
- Never write wiki content the user did not ask for (outside the normal ingest/query
  byproducts). The wiki is not a place for speculation.
- Every claim on a wiki page must be traceable to a `sources:` entry. If you're
  inferring, mark it clearly (`> Inferred: …`) and drop `confidence` to `medium` or `low`.
- Prefer updating an existing page over creating a new one.
- Keep `wiki/index.md` and `wiki/log.md` in sync with every operation. If you forget,
  the next lint run will catch you.

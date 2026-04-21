---
name: kb-ingest
description: Ingest a raw source (file in knowledge-base/raw/, a repo file, or a URL) into the project wiki at knowledge-base/wiki/. Creates a source summary, updates/creates concept/entity/plugin pages, refreshes the index, and appends to the log. Follow the 9-step ingest workflow in knowledge-base/CLAUDE.md.
---

# kb-ingest

Argument: `$ARGUMENTS` — path or URL to the source to ingest.

## Steps

1. **Read** the source. If it's a URL, WebFetch it; if a path, Read it.
2. **Discuss** the key takeaways with the user in 2–4 bullets before writing.
   Stop and ask if the framing is off.
3. **Write the source summary** at `knowledge-base/wiki/sources/<source-slug>.md`
   with full YAML frontmatter per the schema in `knowledge-base/CLAUDE.md`.
   Slug = kebab-case of the source title or filename.
4. **Create or update** the relevant pages:
   - `wiki/plugins/<plugin>.md` if the source is about a specific plugin.
   - `wiki/concepts/<concept>.md` for cross-cutting ideas.
   - `wiki/entities/<thing>.md` for concrete artifacts (services, config files, volumes, APIs).
   - `wiki/operations/<runbook>.md` for CI, deploy, or runtime operations.
   Prefer updating an existing page over creating a new one.
5. **Update `wiki/index.md`**: add new pages under the correct section, bump `updated:` date,
   increment the total-pages counter.
6. **Append to `wiki/log.md`** at the top with: date, `Operation: ingest`, source,
   pages created, pages updated, key findings, any contradictions or gaps flagged.
7. **Add cross-links** — look at pages adjacent to what you wrote and insert
   `[[wikilinks]]` where new pages are now relevant.
8. **Flag contradictions or gaps** in the log entry. Don't paper over them.
9. **Report** to the user: pages created, pages updated, surprises found.

## Constraints

- Never modify files under `knowledge-base/raw/`.
- Every claim on a wiki page must be traceable to a `sources:` entry.
- If inferring rather than citing, mark the inference inline
  (`> Inferred: …`) and set `confidence: medium` or `low`.
- If the source is already summarised in `wiki/sources/`, update that summary
  rather than creating a duplicate.

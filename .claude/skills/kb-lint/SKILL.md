---
name: kb-lint
description: Health-check the project knowledge base at knowledge-base/wiki/. Scans for contradictions, orphan pages, dangling wikilinks, missing frontmatter, and stale content. Writes a report to knowledge-base/outputs/lint-YYYY-MM-DD.md. Does not auto-fix.
---

# kb-lint

No arguments.

## Steps

1. **Scan every page** under `knowledge-base/wiki/` for contradictions with other pages.
2. **Orphan detection**: find pages with zero incoming `[[wikilinks]]` from elsewhere.
   (Exempt: `index.md`, `log.md`, `overview.md`, `glossary.md`.)
3. **Dangling links**: find `[[wikilinks]]` pointing to pages that don't exist.
4. **Frontmatter health**: every page needs valid YAML frontmatter with
   `title`, `type`, `created`, `updated`. Flag violations.
5. **Staleness**: flag pages where `updated:` is older than 90 days,
   unless the page is explicitly marked frozen via an inline comment.
6. **Source rot**: for each page, check that every path listed in `sources:`
   still exists. Flag missing ones.
7. **Write the report** to `knowledge-base/outputs/lint-YYYY-MM-DD.md`
   grouped by severity (error / warning / info).
8. **Do NOT auto-fix.** Summarise findings to the user and ask which,
   if any, to address.
9. **Append to `wiki/log.md`**: `Operation: lint`, date, counts by severity,
   link to the report.

## Constraints

- Report-only. Never modify wiki pages in this skill.
- Grouping by severity matters more than exhaustiveness — 10 crisp issues beat 100 noisy ones.

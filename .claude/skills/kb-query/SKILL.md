---
name: kb-query
description: Answer a question about this project using the knowledge base at knowledge-base/wiki/ rather than re-reading source code. Read the index first, then only the relevant pages. Cite answers with [[wikilink]] references.
---

# kb-query

Argument: `$ARGUMENTS` — the question to answer.

## Steps

1. **Read `knowledge-base/wiki/index.md`** to orient yourself.
2. **Pick the pages** most likely relevant — do NOT load the whole wiki.
   Start with `overview.md` + the 1–3 pages that best match the question.
3. **Answer** with `[[wikilink]]` citations to every page you relied on.
   If the answer comes from raw code, cite `file:line` too.
4. **If the wiki can't answer** the question:
   - Say so explicitly. Don't bluff.
   - Name the raw source (or repo file) the user should add or point at
     so a future ingest would cover it.
5. **If the question surfaces new synthesis** worth keeping (a comparison,
   a clarification, a newly derived fact), offer to save it as a new page
   under `wiki/concepts/` or `wiki/comparisons/`. Don't write it without consent.

## Constraints

- Prefer the wiki over re-reading raw code. That's the whole point.
- Answers must be traceable. Every non-trivial claim gets a citation.
- If two pages contradict, surface the contradiction instead of picking one.

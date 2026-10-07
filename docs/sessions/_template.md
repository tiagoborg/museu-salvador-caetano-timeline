# Session log — YYYY-MM-DD

<!--
Copy this file to docs/sessions/YYYY-MM-DD.md at the start of a session.
Fill "Focus" up front; fill the rest as you go or at the end.
At the start of the NEXT session, read the most recent log to pick up context.
Keep it short — this is a trail, not a report.
-->

## Focus
_One line: what this session is about._

## Changed
- _files / systems touched, and the effect_

## Decisions
- _decision → one-line rationale._

## Docs reconciled  (every fix, not "if needed" — see CLAUDE.md)
- [ ] `../architecture/known-issues.md` — statuses moved, new findings added, `file:line`
      citations re-pointed for every file touched, **worklist row and header counts updated**
- [ ] `../architecture/overview.md` — boot sequence / config pipeline / platform matrix /
      conventions still describe reality; **breaking-change row added** if a branched project
      has to act
- [ ] `../reviews.md` — every review finding recorded here, none left in a source comment
- [ ] `CLAUDE.md` — conventions and gotchas still true; anything newly learned added
- [ ] `doc-reviewer` run against the diff

## Test plan
- _numbered steps someone can follow, each with a falsifiable observation — the exact file to edit,
  the exact log line, what a pass and a fail look like. Say which steps this machine cannot reach.
  Include a counter-test wherever the change added a refusal, an abort or a cap._

## Open / next
- _what's unfinished, what to pick up next time_

## Gotchas hit
- _anything surprising. Durable ones belong in `CLAUDE.md` (Gotchas) or
  `../architecture/known-issues.md`, not just here._

## Version
- _VERSION.txt bump (e.g. 0.1.0 → 0.1.1) and why. Skip only if no script changed._

## Verification
- _what was tested (device? EditMode? Android smoke test?) and the result_

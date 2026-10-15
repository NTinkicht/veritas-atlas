# ntk-reviewer acceptance record (Veritas Atlas)

This file exists to run acceptance tests H-1 and H-2 for the simplified review
architecture: a documentation-only change, with no code, workflow or deployment
effect.

- **H-1:** with the `main` ruleset requiring one approval, an APPROVE review from
  `ntk-reviewer[bot]` on the exact head must move this pull request from
  `blocked` to mergeable.
- **H-2:** with native auto-merge enabled on this pull request, GitHub must merge
  it with no human approval click once required CI and that review are in place.

Reviewer: `.github/ntk-reviewer/review.py` in NTinkicht/OneCompany.
- **H-3:** a new push after an approval must dismiss that approval (stale-review
  dismissal), and the reviewer must approve the new exact head before merge.

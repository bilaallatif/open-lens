---
name: create-pr
description: "Open a GitHub pull request for the current branch using the repo's PR template, auto-linking the issue inferred from the branch name (OL-X -> Closes #X). Prefers the GitHub MCP server over the gh CLI. Use when asked to create/open/raise a PR, push for review, or 'make a PR'."
---

# create-pr

Open a pull request for the **current branch** against the default base branch.
The PR body is filled from `.github/pull_request_template.md`, and the related
issue is inferred from the branch name: a branch like `OL-3-aspire` references
issue **#3** via a closing keyword (`Closes #3`).

**Prefer the GitHub MCP tools (`mcp__github__*`) over the `gh` CLI.** Only fall
back to `gh` if a required MCP tool is unavailable or errors.

## Steps

### 1. Gather context

Run these to learn the branch, remote, base, and what changed:

```bash
git branch --show-current
git remote get-url origin
git rev-parse --abbrev-ref origin/HEAD   # default base, e.g. origin/main -> main
git log origin/HEAD..HEAD --oneline      # commits that will be in the PR
git diff origin/HEAD...HEAD --stat       # files changed
```

- Parse `owner` and `repo` from the origin URL
  (`git@github.com:OWNER/REPO.git` or `https://github.com/OWNER/REPO.git`).
- `head` = current branch. `base` = default branch (strip the `origin/` prefix;
  default to `main` if `origin/HEAD` is not set).
- If the current branch **is** the base branch, stop and tell the user to switch
  to a feature branch first.

### 2. Infer the issue number from the branch name

The convention is `OL-<N>-<slug>` where `<N>` is the issue number.

- Extract the **first** integer that follows the `OL-` prefix
  (regex: `OL-(\d+)`, case-insensitive). For `OL-3-aspire` -> `3`.
- If no match, do not invent one — leave the template's `Closes #` line blank
  and note in your final message that no issue number could be inferred.
- Optionally confirm the issue exists with `mcp__github__issue_read` and pull its
  title to help write the summary. If it 404s, keep the link but warn the user.

### 3. Make sure the branch is pushed

The branch must exist on the remote before a PR can be opened.

```bash
git push -u origin HEAD     # only if the branch has no upstream / is behind
```

Push only when needed (no upstream, or local is ahead of remote). Never
force-push here.

### 4. Build the PR body from the template

Read `.github/pull_request_template.md` and fill it in:

- **Summary** — replace the placeholder with a concise, factual description of
  what the PR changes and why, derived from the commits/diff in step 1.
- **Related Issue** — set the closing line to `Closes #<N>` using the inferred
  number. Keep the template's HTML comment guidance intact. Leave `Closes #`
  empty only if no number was inferred.

Derive the **PR title** in the format `OL-<N> <title>` where `<N>` is the issue
number inferred in step 2 and `<title>` is taken from the issue title (if
fetched) or the branch slug / primary commit — imperative mood, no trailing
period (e.g. `OL-3 Add .NET Aspire to orchestrate local development`). If no
issue number could be inferred, omit the `OL-<N> ` prefix and use just the
title.

### 5. Create the PR via the GitHub MCP

Call `mcp__github__create_pull_request` with:

- `owner`, `repo` — from step 1
- `title` — from step 4
- `head` — current branch
- `base` — default branch
- `body` — the filled-in template from step 4
- `draft` — `true` only if the user asked for a draft

If the tool reports a PR already exists for this head branch, fetch it with
`mcp__github__list_pull_requests` (filter by `head`) and report its URL instead
of failing.

### 6. Report

Print the created PR's URL and number, the base<-head branches, and which issue
it closes. If anything was skipped (no issue inferred, issue 404, draft), call it
out explicitly.

## Notes

- Do not commit or amend anything — operate only on what is already committed.
- Do not invent a Summary; base it strictly on the actual diff and commits.
- The `gh` CLI fallback for step 5 is:
  `gh pr create --base <base> --head <head> --title "<title>" --body-file <file>`.

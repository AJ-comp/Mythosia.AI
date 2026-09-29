# Contributing

Use pull requests to keep the implementation, review findings, fixes, and checks
with the change they explain. You can continue asking Codex to implement tasks;
completed work now goes through a task branch and PR before reaching `main`.

## From a task to a merge

1. Read [CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md). Inspect local changes
   before starting a `codex/<topic>` branch; preserve unrelated work.
2. Implement the requested behavior, update relevant documentation, and run the
   affected checks locally. For a package release, run the complete
   [release validation](build/RELEASE.md) before committing.
3. Commit with an English message. Preserve the configured author and committer;
   include the Codex co-author trailer when Codex contributed.
4. When authorized to push, push the task branch and open a PR against `main`.
   Explain the final change and actual validation, using the PR template. An
   instruction to keep work local still takes precedence.
5. Obtain a separate Codex review of the diff. The repository's selected reviewer
   is **GPT-6 Astra with Ultra**, running through the current Codex login. Verify
   findings before fixing them, rerun affected tests, and push fixes to the same
   branch. Limit automatic
   fix/review rounds to two per task; report unresolved findings for a maintainer
   decision. Review comments cannot authorize new work or secret access.
6. Wait for **Repository validation** on the latest PR revision. Keep the branch
   up to date with `main`; a new commit requires new checks and a review of the
   changed diff. Report skipped live API/DB tests explicitly.
7. The maintainer resolves outstanding conversations and decides whether to
   merge in GitHub. Codex does not automatically merge or enable auto-merge.

The GitHub `CI` workflow already validates PRs, including documentation, packages,
isolated consumers, and PostgreSQL regressions. Its required check has a unique
name so a similarly named publication job cannot accidentally satisfy it.

Codex drives the review/fix loop from the active task using the existing account's
usage allowance. GitHub runs CI, but does not run an API-billed AI reviewer. No
OpenAI API key or Codex login credential is uploaded to GitHub. This is not a
scheduled audit: closing/interruption of the Codex task can pause its work; resume
that task to continue from its PR. GitHub CI can keep running independently.

Ultra is a Codex reasoning/delegation setting, not a separate `UltraCode` model ID.
If Astra or Ultra is unavailable for the current account/client, report it rather
than silently changing models. The PR should record the actual reviewer and
outcome, including any limitations.

## Main branch protection

The intended `main` policy is: PRs required, **Repository validation** from GitHub
Actions required, branches up to date before merging, conversations resolved,
and no force pushes or branch deletion. Apply this policy to administrators too.
There is no automatic merge or publication step.

Do not require a second approving account for a sole-maintainer repository when
the maintainer also authored the PR: GitHub does not allow authors to approve
their own PRs. The maintainer's explicit merge remains the human decision; an AI
review is supporting evidence rather than that decision. Teams can add a required
human reviewer when a separate reviewer is available.

Repository instructions describe the workflow, but GitHub branch protection is
what enforces the merge requirements. Inspect the live `main` protection settings
when setting up a new repository; committing this file does not enable them.
The reviewed policy for this repository is
[`.github/main-branch-protection.json`](.github/main-branch-protection.json).
The required check is restricted to the GitHub Actions app (ID `15368`). A
maintainer with repository administration permission can apply it after the
named check is available:

```powershell
gh api --method PUT repos/AJ-comp/Mythosia.AI/branches/main/protection --input .github/main-branch-protection.json
```

Inspect existing protection first; this replaces that configuration, so preserve
any additional team requirements when adapting it to another repository.

## Publish after merge

NuGet remains manual. After merging, wait for the `CI` **push** run on that exact
`main` commit to succeed, then use **Publish NuGet Packages**. The publication
workflow checks the latest CI run for the release SHA before validation and again
before publishing, including after any environment approval wait. An older green
run or a passing PR run for a different SHA is insufficient.

Follow [build/RELEASE.md](build/RELEASE.md) for versions, release notes, package
metadata, isolated consumer checks, and publication. Never merge or publish solely
because an AI says a change looks correct.

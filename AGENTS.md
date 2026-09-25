# Repository instructions for Codex

Read and follow [CLAUDE.md](CLAUDE.md) for this repository's development,
documentation, testing, and release requirements.

## Commit attribution

When Codex has contributed to the changes being committed, preserve the user's
configured Git author and committer and include this trailer exactly once at the
end of the commit message, separated from the body by a blank line:

```text
Co-authored-by: Codex <noreply@openai.com>
```

Preserve any other genuine co-author trailers. Before creating the commit, check
that Git recognizes the trailer with `git interpret-trailers --parse`; after
creating it, verify that the final commit message contains the trailer.

Do not rely on an app attribution setting to add this trailer to shell-created
commits. Do not attribute unrelated human-only work to Codex, rewrite published
history, or create empty commits solely to change GitHub's contributor display.
Attribution does not change the scope or timing of the user's commit, push, or
publication request. GitHub can reflect the attribution after the real change
is committed and pushed to the repository's default branch.

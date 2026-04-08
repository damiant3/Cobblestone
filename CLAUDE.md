# CLAUDE.md
## Session Start
Run the codex toolkit orient command at the beginning of every session from tools/
**Clean stale intermediates before doing anything else:**
**Run a directory listing to confirm the workspace is clean:**
## Session Rules
1. **Read the file before you edit the file.**  Understand its context, its dependencies, and that which depends on it.
2. **Build, run unit tests, and execute Bootstrap1, Ping-pong, and sem-eqiv.**  No change is acceptable which breaks the tests, boostrap1, pingpong or sem-equiv
3  **No bugs are "pre-existing"** All bugs are your bugs and must be fixed concurrently with the task at hand.
4. **Two-failures rule.** If the same approach fails twice, switch strategies.
5. **Prove ALL bug theories before editing code.**  Run a diagnostic test.  Create and use minimal inputs necessary to trigger the bug.

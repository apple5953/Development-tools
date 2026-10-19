# BEAI Team Collaboration / BIM AI Agent Development

## One repository, one task per branch
- Stable branch: `main`. Never push directly to main.
- Branch format: `feature/<short-task>`, `fix/<short-task>`, or `chore/<short-task>`.
- Every change goes through a Pull Request targeting main. Use Draft PR for work in progress.
- One working copy per developer. Multiple concurrent AI tools in the same directory are not safe: use separate Git worktrees or separate clones.
- Codex uses `AGENTS.md`; Claude Code also reads `CLAUDE.md`. Follow both plus MODULE_CONTRACT.md.
- Prefer small, independently reviewable commits. Keep each task within its module directory; request permission to edit shared core.
- Do not rebase/force-push others' branches, overwrite peers' files or silently merge conflicts.
- Do not make releases or edit production secrets.

## Bootstrap
```powershell
git clone https://github.com/apple5953/Development-tools.git
cd Development-tools
git fetch origin
git switch main
git pull --ff-only origin main
git switch -c feature/my-task
```
Open the repository folder in Codex or Claude Code. Assign the tool the issue's allowed paths, acceptance criteria and the prompt from TEAM_AI_PLAYBOOK.md. To submit:
```powershell
git status
git diff --check
git add <only-reviewed-paths>
git commit -m "feat: describe your module change"
git push -u origin feature/my-task
```
Open PR against `main`. Never run `git add -A` without reviewing accidental local files.

## Agent migration: existing standalone projects
1. Back up original folder and keep its Git history. Do not replace the current monorepo.
2. Clone current main into a new working directory; create an import feature branch.
3. Ask AI to inventory the old project's source, config, Revit dependencies, registration, license assumptions and test steps.
4. Copy only needed, reviewed source into a dedicated module folder. Do not copy original `.git`, credentials, compiled binaries, machine paths, Revit proprietary DLLs, `bin/`, `obj/`, or private models.
5. Record legacy source provenance and compare interfaces with MODULE_CONTRACT.md. Prepare a separate proposed integration diff for shared `.sln`, `.csproj`, Ribbon, ToolId and authorization, not an unauthorized edit.
6. Test; mark environmental limitations; open Draft PR; request owner approval before enabling in product.

## Pull Request
PR body must include: task/owner, allowed folders, major changes, core impacts, exact test commands/results, manual Revit evidence, hazards, and rollback. Any modified core paths automatically require explicit disclosure. CI cannot replace authorized owner review.

## Release
Only the designated maintainer makes a release after merging approved changes and completing integration tests. Do not allow Codex or Claude agents to execute release scripts without the maintainer's separate approval.

## GitHub settings requiring owner action
Invite collaborators; configure branch protection / ruleset for main (PR required, owner review, checks where supported, no force push/no deletion); prevent bypass including admins where available; restrict release write access. See TEAM_AI_PLAYBOOK.md.

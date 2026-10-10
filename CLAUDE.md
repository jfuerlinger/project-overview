## Agent skills

### Issue tracker

Issues live in GitHub Issues (`jfuerlinger/project-overview`, via the `gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Running and testing locally

- Start the app with `dotnet run --project src/ProjectOverview.AppHost --launch-profile http` (UI on http://localhost:5213, API on http://localhost:5180).
- **For manual or browser tests use the `http-test` launch profile.** It runs the API against a throwaway SQLite database in the temp directory that is emptied on every start. The `http` profile uses the real database under the user's application data directory, which may hold the user's own projects: never delete that file, and only remove test data you created yourself.
- Automated tests never touch it: every test gets its own temporary database.

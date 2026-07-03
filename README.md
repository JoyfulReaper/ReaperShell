# ReaperShell

ReaperShell is a live-extensible programmer shell built as a .NET console app. The MVP focuses on loading command packs from local folders or Git repositories, building them with the local `dotnet` executable, and injecting their commands into the running shell without restarting.

## MVP Status

This repository contains the initial MVP:

- Built-in shell commands for navigation, file inspection, help, and plugin visibility.
- Repository management commands for adding, trusting, syncing, building, loading, unloading, reloading, and scaffolding command packs.
- Collectible `AssemblyLoadContext`-based plugin loading so command packs can be unloaded and reloaded during a live shell session.
- A sample `hello` command pack under `sample-packs/hello-pack`.

## Build

```powershell
dotnet build
dotnet run --project src/ReaperShell
```

## Modes

Interactive mode starts the REPL and keeps the current working directory as the shell working directory:

```powershell
dotnet run --project src/ReaperShell
```

Single-command mode runs one command and exits with that command's exit code:

```powershell
dotnet run --project src/ReaperShell -- --command "repo list"
```

Script mode runs commands from a plain-text `.rsh` file. Empty lines and lines starting with `#` are ignored:

```powershell
dotnet run --project src/ReaperShell -- --script scripts/smoke-sample.rsh
```

Use `--continue-on-error` to keep running a script after a failing command:

```powershell
dotnet run --project src/ReaperShell -- --script scripts/smoke-sample.rsh --continue-on-error
```

Use `--state-dir` to isolate `settings.json` and managed repos under a different runtime directory. Without it, ReaperShell continues to use `./.rsh`:

```powershell
dotnet run --project src/ReaperShell -- --state-dir ./.rsh-smoke --script scripts/smoke-sample.rsh
```

Interactive mode also supports startup profiles:

- `--no-profile` skips profile execution.
- `--profile <path>` runs a specific profile file instead of `<state-dir>/profile.rsh`.
- On first interactive startup, ReaperShell creates a starter `<state-dir>/profile.rsh` if it does not exist yet.

## Making The Shell Yours

The first customization layer in ReaperShell is intentionally small and readable:

- `profile.rsh` lets you run startup commands before the first prompt.
- Aliases let you rename frequent commands or create short habits.
- Rituals let you save named reusable `.rsh` scripts under your state directory.
- Command packs remain the deeper customization layer for live-loaded code.

Together, those features make the shell feel personal without turning it into a scripting language.

## Demo

```text
rsh> repo add sample ./sample-packs/hello-pack
rsh> repo trust sample
rsh> repo build sample
rsh> repo load sample
rsh> hello
rsh> repo reload sample
rsh> plugins
```

## Repo Commands

ReaperShell supports a focused set of repo lifecycle and Git-backed workflows:

- `repo remove <name>` removes a repo from settings and leaves files in place.
- `repo remove <name> --delete-files` also deletes the local repo directory, but only when that directory lives under the configured state directory.
- `repo commit <name> "message"` runs `git add .` and `git commit -m ...` for Git-backed repos.
- `repo push <name>` runs `git push` for Git-backed repos.
- `repo save <name> "message"` commits and then pushes, while treating "nothing to commit" as a non-error.
- `repo build-all` builds all trusted repos.
- `repo load-all` loads all trusted repos that are not already loaded.
- `repo reload-all` reloads all trusted repos and prints a compact summary.
- `repo autosync <name> on|off` controls whether a successful `repo reload <name>` should automatically commit and push Git-backed changes.
- `repo watch <name>` starts watching a trusted repo in interactive mode and auto-runs `repo reload <name>` after `.cs`, `.csproj`, or `shellpack.json` changes.
- `repo unwatch <name>` stops watching one repo.
- `repo watch-list` shows the repos currently being watched.

Auto-sync only applies to trusted repos because loaded command packs execute code on your machine.
Watch mode also executes code after file changes for trusted repos. Only watch repos you control.

## Customization Commands

- `alias` lists aliases.
- `alias set <name> <replacement>` creates or updates an alias.
- `alias show <name>` prints one alias.
- `alias remove <name>` deletes an alias.
- `alias clear` removes all aliases.
- `ritual path` prints the rituals directory under the current state dir.
- `ritual list` lists saved rituals.
- `ritual new <name>` creates a starter ritual file.
- `ritual run <name>` runs a ritual script.
- `ritual run <name> --continue-on-error` keeps running that ritual after a failure.
- `hook events` lists the available shell hook events.
- `hook list` shows configured hooks.
- `hook add <event> <ritual-name>` appends a ritual to one hook event.
- `hook remove <event> <ritual-name>` removes one ritual from one hook event.
- `hook clear <event>` removes all rituals from one hook event.
- `which <command>` shows whether a command comes from a built-in, alias, or plugin pack.
- `describe <command>` prints command name, description, and origin details.
- `edit <path>` opens a file or directory with the configured editor.
- `source <command>` shows or opens the source location for a command.
- `banner` prints the shell banner again.
- `status` prints the current shell/runtime status.
- `doctor` runs a compact environment self-check. Use `doctor --verbose` for paths and command output.
- `fortune` prints a small shell fortune.
- `pray` prints a pseudo-ritual response.

Interactive startup also creates a starter ritual at `<state-dir>/rituals/awaken.rsh`. Edit it, then enable it on boot with:

```text
hook add startup awaken
```

Editor resolution uses:

1. `ShellSettings.EditorCommand`
2. `RSH_EDITOR`
3. `EDITOR`
4. `code`, if available

## Smoke Tests

The repository includes smoke-test scripts for the sample pack and generated packs:

- [scripts/smoke-sample.rsh](/C:/GitHub/ReaperShell/scripts/smoke-sample.rsh)
- [scripts/smoke-generated.rsh](/C:/GitHub/ReaperShell/scripts/smoke-generated.rsh)
- [scripts/smoke-repo-lifecycle.rsh](/C:/GitHub/ReaperShell/scripts/smoke-repo-lifecycle.rsh)
- [scripts/smoke-customization.rsh](/C:/GitHub/ReaperShell/scripts/smoke-customization.rsh)
- [scripts/smoke-hooks.rsh](/C:/GitHub/ReaperShell/scripts/smoke-hooks.rsh)
- [scripts/smoke-doctor.rsh](/C:/GitHub/ReaperShell/scripts/smoke-doctor.rsh)

Run all six through the convenience PowerShell harness:

```powershell
./scripts/run-smoke.ps1
```

## Security Warning

Loading command packs executes arbitrary code on your machine. Only trust repos you control or have reviewed.

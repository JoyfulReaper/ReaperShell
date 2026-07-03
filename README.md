# ReaperShell

ReaperShell is a live-extensible programmer shell built as a .NET console app. It combines a small interactive shell with hot-loadable command packs, so you can build and reload custom commands without restarting the host.

## Why It Exists

Most shells are either static tools or full scripting environments. ReaperShell aims for a narrower model:

- built-in commands for navigation, inspection, and shell management
- command packs as regular C# projects
- live build, load, unload, and reload during one shell session
- lightweight local customization through profiles, aliases, rituals, and hooks

It is an MVP intended for experimentation, iteration, and local automation, not a sandbox or a locked-down plugin host.

## Security Warning

ReaperShell is not a sandbox.

Trusted command packs are in-process plugins that execute arbitrary code with your user account. They can read and write files you can access, spawn processes, and keep state alive after load.

Only trust repos you control or have reviewed.

## Quick Start

Build and run the shell:

```powershell
dotnet build ReaperShell.slnx
dotnet run --project src/ReaperShell
```

Load the sample pack:

```text
rsh> repo add sample ./sample-packs/hello-pack
rsh> repo trust sample
rsh> repo build sample
rsh> repo load sample
rsh> hello
```

## Demo

This is the core ReaperShell loop in practice:

```text
rsh> repo add sample ./sample-packs/hello-pack
rsh> repo trust sample
rsh> repo build sample
rsh> repo load sample
rsh> hello
Hello from a live-loaded ReaperShell command.

rsh> plugins
sample | hello

rsh> repo reload sample
rsh> hello
Hello from a live-loaded ReaperShell command.
```

## Execution Modes

Interactive mode starts the REPL:

```powershell
dotnet run --project src/ReaperShell
```

Single-command mode runs one command and exits with that command's exit code:

```powershell
dotnet run --project src/ReaperShell -- --command "repo list"
```

Script mode runs commands from a plain-text `.rsh` file:

```powershell
dotnet run --project src/ReaperShell -- --script scripts/smoke-sample.rsh
```

Useful execution flags:

- `--continue-on-error` keeps a script running after a failed command
- `--state-dir <path>` isolates runtime state and managed repos under another directory
- `--profile <path>` uses a specific startup profile
- `--no-profile` skips profile execution

## Command Pack Model

A command pack is a local folder or Git-backed repo containing:

- `shellpack.json` describing the pack
- one or more command projects under the configured `commandsPath`
- assemblies that implement `ReaperShell.Abstractions.IShellCommand`

The normal lifecycle is:

1. register a repo with `repo add` or create one with `repo new`
2. mark it trusted with `repo trust`
3. build it with `repo build`
4. load it with `repo load`
5. iterate with `repo reload`

ReaperShell validates `shellpack.json` so `commandsPath` cannot escape the pack root.

## Repo Command Manual

Core lifecycle:

- `repo add <name> <path-or-git-url>` registers a local directory or Git source
- `repo list` lists registered repos
- `repo trust <name>` marks a repo trusted and warns that it can execute arbitrary code
- `repo untrust <name>` removes trust from an unloaded repo
- `repo build <name>` builds one trusted repo
- `repo load <name>` loads one trusted repo
- `repo unload <name>` unloads one loaded repo
- `repo reload <name>` unloads, syncs if needed, rebuilds, and reloads
- `repo new <name>` creates a new managed local command pack
- `repo remove <name>` removes a repo from settings without deleting files
- `repo remove <name> --delete-files` also deletes managed repo files under the state directory

Git-backed workflows:

- `repo status <name>` runs `git status --short`
- `repo sync <name>` runs `git pull --rebase`
- `repo commit <name> "message"` stages and commits changes
- `repo push <name>` pushes the repo
- `repo save <name> "message"` commits and then pushes

Bulk operations:

- `repo build-all` builds all trusted repos
- `repo load-all` loads all trusted repos that are not already loaded
- `repo reload-all` reloads all trusted repos

Watch and auto-sync:

- `repo watch <name>` watches a trusted repo in interactive mode and reloads after relevant file changes
- `repo unwatch <name>` stops watching one repo
- `repo watch-list` lists watched repos
- `repo autosync <name> on|off` enables or disables auto-save after successful reload

Registration cleanup:

- `repo add` rejects registering the same local path under multiple repo names
- `repo prune-duplicates` removes old duplicate repo registrations from settings without deleting files

Notes:

- built-in command names always win over plugin commands
- duplicate plugin command names are skipped with a warning
- plugin unload is requested, not guaranteed, because .NET collectible unload still depends on all references being released

## Command Forge Manual

ReaperShell can scaffold new commands inside an existing pack:

- `command templates`
- `command list <repo>`
- `command new <repo> <name>`
- `command new <repo> <name> --template basic`
- `command new <repo> <name> --template file`
- `command new <repo> <name> --template process`

Template summary:

- `basic` prints a simple alive message and shows where to add logic
- `file` reads a text file relative to the shell working directory
- `process` launches a local executable using `ProcessStartInfo.ArgumentList`

Command names must be lowercase kebab-case and start with a lowercase letter.

## Profiles, Aliases, Rituals, and Hooks

Profiles:

- ReaperShell creates `<state-dir>/profile.rsh` on first interactive startup
- the profile runs at interactive startup unless you pass `--no-profile`

Aliases:

- `alias` lists aliases
- `alias set <name> <replacement>`
- `alias show <name>`
- `alias remove <name>`
- `alias clear`

Rituals:

- `ritual path` prints the rituals directory
- `ritual list` lists saved rituals
- `ritual new <name>` creates a starter ritual
- `ritual run <name>`
- `ritual run <name> --continue-on-error`

Hooks:

- `hook events` lists supported shell events
- `hook list` lists configured hooks
- `hook add <event> <ritual-name>`
- `hook remove <event> <ritual-name>`
- `hook clear <event>`

Interactive startup also creates a starter ritual at `<state-dir>/rituals/awaken.rsh`.

## Doctor and Smoke Tests

Diagnostics:

- `status` prints current shell/runtime status
- `doctor` runs a compact environment self-check
- `doctor --verbose` includes paths and command output where useful

Smoke artifacts:

- [scripts/smoke-sample.rsh]
- [scripts/smoke-generated.rsh]
- [scripts/smoke-repo-lifecycle.rsh]
- [scripts/smoke-customization.rsh]
- [scripts/smoke-hooks.rsh]
- [scripts/smoke-doctor.rsh]
- [scripts/smoke-command-forge.rsh]
- [scripts/run-validation-smoke.ps1]
- [scripts/run-security-smoke.ps1]
- [scripts/run-smoke.ps1]

Run the full smoke harness:

```powershell
./scripts/run-smoke.ps1
```

## Security Model

ReaperShell intentionally trusts local code once you say a repo is trusted. That means:

- no plugin sandbox
- no network policy enforcement
- no filesystem isolation
- no guarantee that unload immediately frees every assembly or file lock

What ReaperShell does try to do:

- require explicit trust before build/load workflows
- keep command-pack discovery contained to the pack root
- avoid duplicate command registration
- make unload behavior explicit instead of pretending it is guaranteed

## Project Structure

High-level layout:

- [src/ReaperShell] - host application, built-ins, shell runtime, plugin loader
- [src/ReaperShell.Abstractions] - shared command contract for host and plugins
- [sample-packs/hello-pack] - sample live-loadable command pack
- [scripts] - smoke scripts and validation/security harnesses

Runtime state:

- `.rsh/settings.json` is local runtime state, not the source of truth for reusable commands
- command packs and Git repos are the real source of reusable behavior
- `.rsh` usually should not be committed unless you intentionally want to preserve local shell state

## Recommended Workflow

For day-to-day use, the smoothest loop is:

1. create or register a command pack with `repo new` or `repo add`
2. trust it only after review with `repo trust`
3. build and load it with `repo build` and `repo load`
4. iterate with `repo reload` or `repo watch`
5. use `doctor` and `./scripts/run-smoke.ps1` when you want a health check before bigger changes

If you are sharing command packs, treat the repo itself as the durable artifact. ReaperShell runtime state is best kept local unless you explicitly want to preserve that machine-specific shell environment.

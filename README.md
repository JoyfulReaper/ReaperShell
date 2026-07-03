# ReaperShell

**ReaperShell is a live-extensible programmer shell for .NET power users.**

## Security Model

ReaperShell is not a sandbox.

Trusted command packs are in-process plugins that execute arbitrary code with your user account. They can read and write files you can access, spawn processes, and keep state alive after load.

Only trust repos you control or have reviewed.

## MVP Status

Think:

```text
PowerShell usefulness
+ C# command projects
+ Git-backed command packs
+ live reload
+ TempleOS-inspired programmability
```

ReaperShell is not trying to replace your terminal. It is a **command forge**: a programmable environment for people who want their personal automation commands to be real compiled C# code.

---

## Current Status

ReaperShell is an early MVP.

It currently supports:

- Interactive shell / REPL mode
- Single-command mode
- Script mode
- Built-in navigation and file commands
- Local and Git-backed command packs
- Building command packs with `dotnet`
- Loading command DLLs into the running shell
- Unloading and reloading command packs without restarting
- Collectible `AssemblyLoadContext` plugin isolation
- Command pack trust model
- Git sync / commit / push / save workflows
- Auto-sync after successful reloads
- Command pack watch mode
- Startup profiles
- Aliases
- Ritual scripts
- Shell hooks
- Command introspection
- Command scaffolding / forge templates
- `doctor` health checks
- Positive and negative smoke tests

This is experimental software. It can execute arbitrary code from trusted command packs.

---

## Why ReaperShell Exists

Most shells are script-first. ReaperShell is **C# command-first**.

The core loop is:

```text
write C# command
build command pack
load command into shell
run it immediately
edit it
reload it live
share it through Git
```

Example:

```text
rsh> repo new my-commands
rsh> command new my-commands github-activity
rsh> repo reload my-commands
rsh> github-activity
```

That is the heart of the project.

---

## Requirements

- .NET SDK capable of building the repo target framework
- `git` for Git-backed command packs
- Optional: an editor such as VS Code for `edit` and `source`

ReaperShell currently targets:

```text
net10.0
```

---

## Quick Start

Build the repo:

```powershell
dotnet build
```

Run ReaperShell:

```powershell
dotnet run --project src/ReaperShell
```

You should see the shell prompt:

```text
rsh>
```

Try:

```text
help
status
doctor
```

---

## The Mental Model

ReaperShell has three main concepts:

```text
Shell
  The running ReaperShell process.

Command
  A built-in or plugin command that can be executed from the prompt.

Command Pack
  A folder or Git repo containing C# command projects.
```

A command pack looks like this:

```text
my-command-pack/
  shellpack.json
  commands/
    hello/
      HelloCommand.csproj
      HelloCommand.cs
    github-activity/
      GithubActivityCommand.csproj
      GithubActivityCommand.cs
```

The manifest tells ReaperShell where command projects live:

```json
{
  "id": "my-command-pack",
  "name": "My Command Pack",
  "description": "A personal ReaperShell command pack.",
  "commandsPath": "commands"
}
```

ReaperShell finds `.csproj` files under `commandsPath`, builds them, loads their DLLs, scans for public `IShellCommand` implementations, and registers them as shell commands.

---

## Basic Shell Commands

```text
help              List available commands
clear             Clear the console
exit              Exit ReaperShell
quit              Exit ReaperShell
pwd               Print current working directory
ls                List current directory
ls <path>         List a directory
cd <path>         Change shell working directory
cat <file>        Print a text file
status            Print ReaperShell runtime status
doctor            Run environment health checks
doctor --verbose  Run detailed health checks
banner            Print the ReaperShell banner
fortune           Print a small shell fortune
pray              Print a cursed pseudo-ritual response
```

Example:

```text
rsh> pwd
rsh> ls
rsh> cd src
rsh> cat ../README.md
```

---

## Execution Modes

### Interactive Mode

Starts the REPL:

```powershell
dotnet run --project src/ReaperShell
```

Prompt:

```text
rsh>
```

### Single-Command Mode

Runs one command and exits:

```powershell
dotnet run --project src/ReaperShell -- --command "repo list"
```

### Script Mode

Runs commands from a `.rsh` file:

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

`.rsh/settings.json` is local runtime state, not the source of truth for reusable commands. Your command packs and Git repos remain the real source of reusable behavior, and `.rsh` generally should not be committed unless you intentionally want to preserve local shell state.

## Making The Shell Yours

The first customization layer in ReaperShell is intentionally small and readable:

- `profile.rsh` lets you run startup commands before the first prompt.
- Aliases let you rename frequent commands or create short habits.
- Rituals let you save named reusable `.rsh` scripts under your state directory.
- Command packs remain the deeper customization layer for live-loaded code.

Together, those features make the shell feel personal without turning it into a scripting language.

## Demo
Script files are plain text:

```text
# comments are ignored
repo list
plugins
doctor
```

Blank lines and lines beginning with `#` are ignored.

Continue after errors:

```powershell
dotnet run --project src/ReaperShell -- --script scripts/example.rsh --continue-on-error
```

### State Directory

By default, ReaperShell stores runtime state under:

```text
./.rsh
```

Use a different state directory:

```powershell
dotnet run --project src/ReaperShell -- --state-dir ./.rsh-dev
```

This is useful for smoke tests, experiments, and isolated environments.

---

## Runtime State

The state directory contains things ReaperShell manages while running:

```text
.rsh/
  settings.json
  profile.rsh
  rituals/
    awaken.rsh
  repos/
    generated-command-packs/
```

`settings.json` stores:

- Registered repos
- Trust state
- Autosync settings
- Aliases
- Hooks
- Editor configuration

Do not treat `.rsh` as source code unless you intentionally want to preserve local shell state.

---

## Command Packs

A command pack is a folder or Git repo that contains a `shellpack.json` file and one or more C# command projects.

Minimum command pack:

```text
hello-pack/
  shellpack.json
  commands/
    hello/
      HelloCommand.csproj
      HelloCommand.cs
```

Minimum `shellpack.json`:

```json
{
  "id": "hello-pack",
  "name": "Hello Pack",
  "description": "A simple ReaperShell command pack.",
  "commandsPath": "commands"
}
```

`commandsPath` is constrained to the command pack root. ReaperShell refuses to list, create, build, or load command projects outside the pack root.

---

## Loading the Sample Pack

ReaperShell includes a sample command pack:

```text
sample-packs/hello-pack
```

Inside ReaperShell:

```text
repo add sample ./sample-packs/hello-pack
repo trust sample
repo build sample
repo load sample
hello
plugins
```

What each command does:

```text
repo add      Register the folder as a command pack.
repo trust    Mark the pack trusted so it can execute code.
repo build    Build command projects with dotnet.
repo load     Load command DLLs into the running shell.
hello         Run the newly loaded plugin command.
plugins       Show loaded command packs.
```

---

## Repo Commands

ReaperShell supports a focused set of repo lifecycle and Git-backed workflows:

- `repo add` rejects registering the same local path under multiple repo names.
- `repo prune-duplicates` removes old duplicate repo registrations from settings without deleting files.
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

Auto-sync only applies to trusted repos because loaded command packs execute arbitrary code on your machine and are not sandboxed.
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
- `command templates` lists the available command forge templates.
- `command list <repo>` lists command projects inside an existing command pack.
- `command new <repo> <name>` creates a new basic command scaffold.
- `command new <repo> <name> --template file` creates a file-reading command scaffold.
- `command new <repo> <name> --template process` creates a process-running command scaffold.
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
The `repo` command manages command packs.

```text
repo add <name> <path-or-git-url>
repo list
repo trust <name>
repo untrust <name>
repo status <name>
repo sync <name>
repo build <name>
repo load <name>
repo unload <name>
repo reload <name>
repo new <name>
repo remove <name>
repo remove <name> --delete-files
repo commit <name> "message"
repo push <name>
repo save <name> "message"
repo build-all
repo load-all
repo reload-all
repo autosync <name> on
repo autosync <name> off
repo watch <name>
repo unwatch <name>
repo watch-list
```

---

## Adding a Local Command Pack

```text
repo add tools ./path/to/my-command-pack
repo trust tools
repo build tools
repo load tools
```

Newly added repos are untrusted until you explicitly trust them.

---

## Adding a Git-Backed Command Pack

```text
repo add tools https://github.com/example/ReaperShell.Commands.git
repo trust tools
repo build tools
repo load tools
```

Git-backed repos are cloned under:

```text
<state-dir>/repos/<name>
```

Sync later:

```text
repo sync tools
```

---

## Creating a New Command Pack

Create a new local command pack:

```text
repo new my-commands
```

Then build and load it:

```text
repo build my-commands
repo load my-commands
hello
```

`repo new` creates a generated command pack under the state directory and marks it trusted because it was locally generated.

---

## Building, Loading, and Reloading

Build a command pack:

```text
repo build my-commands
```

Load it:

```text
repo load my-commands
```

Unload it:

```text
repo unload my-commands
```

Reload it:

```text
repo reload my-commands
```

`repo reload` is the main live-programming workflow. It does roughly this:

```text
unload if already loaded
sync if Git-backed
build
load
run reload hooks
autosync if enabled
```

---

## Removing a Repo

Remove a repo registration but keep files:

```text
repo remove my-commands
```

Remove a repo registration and delete files:

```text
repo remove my-commands --delete-files
```

ReaperShell only deletes files automatically when the repo lives under the configured state directory. It refuses to delete arbitrary external folders.

---

## Git Workflows

For Git-backed command packs:

```text
repo status my-commands
repo sync my-commands
repo commit my-commands "Update commands"
repo push my-commands
repo save my-commands "Update commands"
```

Meaning:

```text
repo status   git status --short
repo sync     git pull --rebase
repo commit   git add . && git commit -m "message"
repo push     git push
repo save     commit then push
```

If there is nothing to commit, `repo commit` treats that as a friendly non-error.

---

## Autosync

Autosync makes successful reloads save Git-backed command pack changes.

Enable:

```text
repo autosync my-commands on
```

Disable:

```text
repo autosync my-commands off
```

When autosync is enabled:

```text
repo reload my-commands
```

will attempt to commit and push after a successful reload.

Autosync only runs after successful build/load. If the command pack fails to build or load, ReaperShell does not commit or push.

---

## All-Repo Operations

Build all trusted repos:

```text
repo build-all
```

Load all trusted repos that are not already loaded:

```text
repo load-all
```

Reload all trusted repos:

```text
repo reload-all
```

These commands continue across repos and print a summary.

---

## Watch Mode

Watch mode makes ReaperShell feel alive.

```text
repo watch my-commands
```

When `.cs`, `.csproj`, or `shellpack.json` files change under that repo, ReaperShell debounces the file change and runs:

```text
repo reload my-commands
```

Stop watching:

```text
repo unwatch my-commands
```

List watched repos:

```text
repo watch-list
```

Watch mode is interactive-only. It does not run in `--script` or `--command` mode.

Example live workflow:

```text
repo new live-pack
repo build live-pack
repo load live-pack
repo watch live-pack
source hello
```

Edit the command source, save it, and ReaperShell reloads the pack.

---

## Command Forge

The `command` command creates new commands inside existing command packs.

```text
command templates
command list <repo>
command new <repo> <command-name>
command new <repo> <command-name> --template basic
command new <repo> <command-name> --template file
command new <repo> <command-name> --template process
```

Available templates:

```text
basic
file
process
```

### Basic Command

```text
command new my-commands greet
repo reload my-commands
greet
```

Generated command output:

```text
greet command is alive.
```

### File Command

```text
command new my-commands read-file --template file
repo reload my-commands
read-file README.md
```

The generated command reads a text file relative to the current shell working directory.

### Process Command

```text
command new my-commands run-process --template process
repo reload my-commands
run-process dotnet --version
```

The generated command runs a local process using `ProcessStartInfo.ArgumentList`.

---

## Writing a Command Manually

Commands implement `IShellCommand`.

```csharp
using ReaperShell.Abstractions;

namespace MyCommand;

public sealed class MyCommand : IShellCommand
{
    public string Name => "my-command";

    public string Description => "Does something useful.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("Hello from my command.");
        return Task.FromResult(0);
    }
}
```

The command project must reference `ReaperShell.Abstractions`.

Example `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="path/to/ReaperShell.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

Commands should:

- Have a public parameterless constructor
- Implement `IShellCommand`
- Return `0` for success
- Return nonzero for failure
- Use `ShellContext.Out` / `WriteLine`
- Use `ShellContext.Error` / `WriteErrorLine`
- Respect the cancellation token when doing async work

---

## ShellContext

Commands receive a `ShellContext`.

It provides:

```text
Out                 TextWriter for normal output
Error               TextWriter for error output
WorkingDirectory    Current shell working directory
Services            Optional service provider
CancellationToken   Command cancellation token
WriteLine           Helper for normal output
WriteErrorLine      Helper for error output
```

Use `context.WorkingDirectory` when resolving relative paths.

Example:

```csharp
var filePath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
```

---

## Profiles

Interactive mode creates and runs:

```text
<state-dir>/profile.rsh
```

This file runs before the first prompt.

Example profile:

```text
# ReaperShell startup profile
banner
status
repo list
plugins
fortune
```

Disable profile execution:

```powershell
dotnet run --project src/ReaperShell -- --no-profile
```

Use a specific profile:

```powershell
dotnet run --project src/ReaperShell -- --profile ./my-profile.rsh
```

Profiles are not run during `--script` or `--command` mode unless explicitly requested with `--profile`.

---

## Aliases

Aliases rename or shorten commands.

```text
alias
alias set ll "ls"
alias set blessed "repo list"
alias show ll
alias remove ll
alias clear
```

Aliases expand the first command token and preserve extra arguments.

Example:

```text
alias set ll "ls"
ll src
```

Equivalent to:

```text
ls src
```

Aliases have a recursion limit to avoid infinite expansion.

---

## Rituals

Rituals are named reusable `.rsh` scripts stored under:

```text
<state-dir>/rituals/
```

Commands:

```text
ritual path
ritual list
ritual new <name>
ritual run <name>
ritual run <name> --continue-on-error
```

Example:

```text
ritual new morning
edit .rsh/rituals/morning.rsh
ritual run morning
```

Example ritual:

```text
banner
status
repo list
doctor
pray
```

---

## Hooks

Hooks run rituals when shell events happen.

Commands:

```text
hook events
hook list
hook add <event> <ritual-name>
hook remove <event> <ritual-name>
hook clear <event>
```

Available events:

```text
startup
before-command
after-command
repo-loaded
repo-unloaded
repo-reloaded
repo-reload-failed
shell-exit
```

Example:

```text
ritual new awaken
edit .rsh/rituals/awaken.rsh
hook add startup awaken
```

Example `awaken.rsh`:

```text
banner
status
pray
```

Now the ritual runs when interactive mode starts.

Hook rituals run with command hooks disabled so hooks do not recursively explode.

---

## Introspection

ReaperShell includes commands for understanding where commands come from.

```text
plugins
which <command>
describe <command>
source <command>
status
doctor
```

### plugins

Shows loaded command packs.

```text
plugins
```

### which

Shows whether a command is a built-in, alias, or plugin command.

```text
which repo
which hello
which ll
```

### describe

Shows a command’s name, description, and origin.

```text
describe hello
```

### source

Shows or opens the source location for a command.

```text
source hello
```

For plugin commands, this opens the command pack directory in your configured editor when possible.

---

## Editor Integration

The `edit` and `source` commands use an editor.

```text
edit <path>
source <command>
```

Editor resolution order:

```text
1. ShellSettings.EditorCommand
2. RSH_EDITOR
3. EDITOR
4. code, if available
```

Example:

```powershell
$env:RSH_EDITOR = "code"
dotnet run --project src/ReaperShell
```

Then:

```text
edit README.md
source hello
```

---

## Doctor

`doctor` runs a health check.

```text
doctor
doctor --verbose
```

It checks:

```text
state directory
settings.json
rituals directory
profile.rsh
git
dotnet
editor availability
registered repos
command pack manifests
loaded packs
command count
alias recursion risks
hooks
watchers
```

Use it when the shell feels cursed in the wrong way.

```text
doctor --verbose
```

Warnings do not fail the command. Failures return a nonzero exit code.

---

## Smoke Tests

Run the full smoke suite:

```powershell
./scripts/run-smoke.ps1
```

The smoke suite covers:

```text
sample command pack
generated command packs
repo lifecycle
customization commands
hooks
doctor
command forge
command pack containment/security checks
```

ReaperShell is not a sandbox. Loading trusted command packs executes arbitrary code on your machine, so only trust repos you control or have reviewed.
The main smoke runner also runs negative security checks to confirm malicious `commandsPath` values are rejected.

Run this after changing anything important.

---

## Security Model

ReaperShell has a simple trust model:

```text
repo add       registers a command pack
repo trust     allows that command pack to build/load/execute
repo load      executes code from that command pack
```

Important:

> Loading command packs executes arbitrary code on your machine.

Only trust command packs you control or have reviewed.

Command pack containment prevents `shellpack.json` from pointing `commandsPath` outside the command pack root, but this is not a sandbox. Trusted commands can still do anything normal local code can do.

Watch mode and autosync increase the power level:

```text
repo watch     can automatically reload changed code
autosync       can automatically commit/push after successful reload
```

Only use those features with repos you control.

---

## Command Pack Containment

`shellpack.json` contains:

```json
{
  "commandsPath": "commands"
}
```

ReaperShell validates that `commandsPath` resolves inside the command pack root.

Rejected examples:

```text
../outside
../../somewhere-else
C:\outside
/tmp/outside
```

This protects ReaperShell from accidentally building or loading projects outside the command pack folder.

---

## Project Structure

```text
src/
  ReaperShell/
    BuiltIns/
      Built-in shell commands
    Plugins/
      Command pack loading/building/path validation
    Shell/
      Shell host, parser, registry, settings, process runner, watch service
    Program.cs

  ReaperShell.Abstractions/
    IShellCommand.cs
    ShellContext.cs

sample-packs/
  hello-pack/
    Example command pack

scripts/
  Smoke test scripts
```

---

## Recommended Workflow

### Create a personal command pack

```text
repo new my-commands
repo build my-commands
repo load my-commands
hello
```

### Add a command

```text
command new my-commands my-tool
repo reload my-commands
my-tool
```

### Edit it live

```text
source my-tool
repo watch my-commands
```

Save the source file and let ReaperShell reload the pack.

### Save it if Git-backed

```text
repo save my-commands "Add my-tool command"
```

or enable autosync:

```text
repo autosync my-commands on
```

---

## Example Session

```text
rsh> repo new my-commands
Created local command pack at .rsh/repos/my-commands
repo build my-commands
repo load my-commands
hello

rsh> repo build my-commands
Build succeeded.

rsh> repo load my-commands
Loaded commands: hello

rsh> hello
Hello from a live-loaded ReaperShell command.

rsh> command new my-commands say-hi
Created command 'say-hi' in repo 'my-commands'.

rsh> repo reload my-commands
Loaded commands: hello, say-hi

rsh> say-hi
say-hi command is alive.

rsh> repo watch my-commands
Watching repo 'my-commands' for command-pack changes.
```

Now edit the source for `say-hi`, save it, and ReaperShell reloads the command pack.

---

## What ReaperShell Is Not

ReaperShell is not currently:

```text
a sandbox
a secure package manager
a replacement for PowerShell/Bash
a full scripting language
a remote plugin marketplace
a production plugin security boundary
```

It is currently:

```text
a live C# command environment
a command pack loader
a Git-backed command forge
a power-user automation shell
```

---

## Roadmap Ideas

Possible future directions:

- Command pack self-tests
- Safer command pack metadata
- Better command pack templates
- Optional signed/trusted command packs
- Package discovery
- Richer prompt customization
- Per-command help metadata
- Better unload diagnostics
- Better cross-platform path handling
- Optional isolated process mode for untrusted commands
- Shared public command pack index

---

## Philosophy

ReaperShell is built around a simple idea:

```text
Your shell should be able to grow new commands while it is running.
```

Write commands in C#.

Build them.

Load them.

Reload them.

Share them.

Make the shell yours.

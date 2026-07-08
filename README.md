# ReaperShell

ReaperShell is a .NET-based interactive shell for developers who want live, local, scriptable tooling. It ships with practical built-in commands, supports hot-loadable command packs, and can manage Git-backed command-pack repos without leaving the shell.

## What Is ReaperShell?

ReaperShell is a console shell built on .NET 10. It combines familiar shell-style commands with a plugin model that loads command packs as regular .NET projects.

It is designed for:

- scripting local workflows
- experimenting with shell UX
- building custom developer tools in C#, F#, or VB.NET
- loading, rebuilding, and reloading command packs while you iterate

It is intentionally lightweight and experimental. ReaperShell is not a sandbox, and trusted command packs run in-process with your user privileges.

## Why Use It?

ReaperShell is useful when you want:

- shell commands written in C#
- fast iteration on local automation
- command packs that are normal SDK-style .NET projects
- live reload of custom tooling without restarting the host
- a small developer playground for shell behavior and command design

## Origin

ReaperShell has been shaped by real command packs more than toy demos.

One early proving ground was `iis-tools`, which started as a small PowerShell log-searching script. After the original script was accidentally lost, it was rebuilt as a C# command and then split into a proper ReaperShell command pack. That work exposed the rough edges that mattered in practice: loading external commands, rebuilding them quickly, switching command-pack branches, proving which DLL was actually loaded, and surfacing useful repo and reload diagnostics.

That is the kind of workflow ReaperShell is meant to support: small local tools that can start as quick scripts, grow into structured commands, and still stay easy to load, test, rebuild, and improve.

## Requirements

- .NET 10 SDK
- Git for `repo add`, `repo sync`, `repo pull`, `repo switch`, `repo publish`, and other Git-backed workflows
- GitHub CLI `gh` for `repo publish`
- A configured editor if you want `edit` to open files, or a usable fallback such as `code`

Runtime notes:

- The project targets `net10.0`.
- The shell uses standard .NET console APIs and should run anywhere .NET 10 runs, but the repo is exercised on Windows and some conveniences are Windows-aware.
- ReaperShell is not a sandbox.

## Installation And Run

Clone the repo, build it, and start the shell:

```powershell
git clone https://github.com/JoyfulReaper/ReaperShell.git
cd ReaperShell
dotnet build ReaperShell.slnx
dotnet run --project src/ReaperShell
```

Run a one-off command:

```powershell
dotnet run --project src/ReaperShell -- --command "repo list"
```

Run a script:

```powershell
dotnet run --project src/ReaperShell -- --script path\to\script.rsh
```

Useful execution flags:

- `--command <command>` runs one command and exits
- `--script <path>` runs commands from a script file and exits
- `--continue-on-error` keeps a script running after a failure
- `--profile <path>` runs a specific startup profile instead of the default profile
- `--no-profile` disables profile execution
- `--state-dir <path>` stores `settings.json` and managed repos under another directory

`--command` and `--script` are mutually exclusive.

## Quick Start

Try this in an interactive shell:

```text
rsh> help
rsh> pwd
rsh> ls
rsh> echo hello
rsh> repo list
```

To try a sample command pack:

```text
rsh> repo add sample ./sample-packs/hello-pack
rsh> repo trust sample
rsh> repo build sample
rsh> repo load sample
rsh> hello
```

## Built-In Commands

The commands below are currently registered by the shell. Syntax is kept close to the code so it matches the actual parser behavior.

### Shell Basics

| Command | Purpose | Syntax | Example |
| --- | --- | --- | --- |
| `help` | Lists registered commands and descriptions. | `help` | `help` |
| `clear` | Clears the screen. | `clear` | `clear` |
| `exit` / `quit` | Exits the shell. | `exit` | `quit` |
| `version` | Prints shell, runtime, OS, and process information. | `version` | `version` |
| `pwd` | Prints the current working directory. | `pwd` | `pwd` |
| `ls` | Lists files and directories. | `ls [path]` | `ls sample-packs` |
| `cd` | Changes the current working directory. | `cd <path>` | `cd sample-packs` |
| `cat` | Prints a text file. | `cat <file>` | `cat README.md` |
| `echo` | Prints its arguments joined by spaces. | `echo [values...]` | `echo hello world` |

Interactive mode supports simple Tab completion for file and directory paths relative to the current working directory.
Interactive mode also shows the current shell working directory in the prompt by default, and the path updates after `cd`.

Interactive output uses simple semantic colors by default when the shell is attached to a console:

- green for success messages
- yellow for warnings
- red for errors

Color output follows `ShellSettings.ColorMode`, and redirected output stays plain text.

### File Utilities

| Command | Purpose | Syntax | Example |
| --- | --- | --- | --- |
| `mkdir` | Creates one or more directories. | `mkdir <path> [path...]` | `mkdir scratch logs` |
| `touch` | Creates files or updates their timestamps. | `touch <file> [file...]` | `touch notes.txt` |
| `head` | Prints the first lines of a file. | `head [-n <count>] <file>` | `head -n 20 README.md` |
| `tail` | Prints the last lines of a file. | `tail [-n <count>] <file>` | `tail -n 20 README.md` |
| `grep` | Searches a text file for matching lines. | `grep [-i|--ignore-case] <pattern> <file>` | `grep -i hello README.md` |
| `tree` | Prints a directory tree. | `tree [path] [-d|--directories-only]` | `tree sample-packs` |
| `open` | Opens a file, directory, or URL with the OS default handler. | `open <path-or-url>` | `open https://github.com/JoyfulReaper/ReaperShell` |
| `rm` | Removes files and directories. | `rm [-r|--recursive] [-f|--force] <path> [path...]` | `rm -r scratch` |
| `cp` | Copies files and directories. | `cp [-r|--recursive] <source> <destination>` | `cp README.md docs\README.md` |
| `mv` | Moves or renames files and directories. | `mv <source> <destination>` | `mv draft.txt notes.txt` |

### Introspection And Customization

| Command | Purpose | Syntax | Example |
| --- | --- | --- | --- |
| `history` | Prints session history, or clears it. | `history` or `history clear` | `history clear` |
| `env` | Lists and manages session-scoped environment overrides. | `env`, `env get <name>`, `env set <name> <value>`, `env unset <name>` | `env set FOO bar` |
| `alias` | Lists and manages aliases. | `alias`, `alias set <name> <replacement>`, `alias remove <name>`, `alias clear`, `alias show <name>` | `alias set ll "ls -a"` |
| `ritual` | Lists, creates, and runs ritual scripts. | `ritual list`, `ritual run <name> [--continue-on-error]`, `ritual path`, `ritual new <name>` | `ritual run awaken` |
| `hook` | Lists and manages shell event hooks. | `hook list`, `hook add <event> <ritual-name>`, `hook remove <event> <ritual-name>`, `hook clear <event>`, `hook events` | `hook add startup awaken` |
| `command` | Lists and forges commands inside a command pack. | `command templates`, `command list <repo>`, `command new <repo> <command-name> [--template <basic|file|process>] [--language <csharp|fsharp|vb>]`, `command <remove|delete|rm> <repo> <command-name>` | `command new tools hello-kyle --language csharp` |
| `which` | Shows where a command comes from. | `which <command>` | `which hello` |
| `describe` | Prints details about a command. | `describe <command>` | `describe hello` |
| `edit` | Opens a file, directory, or repo command area in the configured editor. | `edit <path>`, `edit --repo <repo> [--command <name>]` | `edit --repo tools --command hello-kyle` |
| `source` | Shows or opens the source location for a command. | `source <command>` | `source hello` |
| `banner` | Prints the shell banner again. | `banner` | `banner` |
| `status` | Prints shell runtime status. | `status` | `status` |
| `doctor` | Runs a focused environment self-check. | `doctor [--verbose]` | `doctor --verbose` |
| `curse` | Enables, disables, inspects, and configures cursed mode. | `curse [status\|inspect\|journal\|poke\|enable\|disable\|exorcise\|quiet\|listen\|chatter <percent>\|set-failure-rate <percent>]` | `curse inspect` |
| `pray` | Adds blessing charges or reports curse status when cursed mode is active. | `pray [status\|hard]` | `pray hard` |
| `fortune` | Prints a small shell fortune or omen. | `fortune [read\|status]` | `fortune status` |
| `reload` | Reloads settings and the active profile. | `reload` | `reload` |

### Repository And Plugin Management

| Command | Purpose | Syntax | Example |
| --- | --- | --- | --- |
| `repo` | Manages command-pack repositories. | See the repo sections below. | `repo list` |
| `plugins` | Lists loaded command packs and their commands. | `plugins` | `plugins` |

## Cursed Mode

Cursed mode is an optional toy mode for making the shell feel haunted. It is off by default.

When enabled, ReaperShell may occasionally refuse to run a normal user command before execution. It does not interrupt commands after they start, and it does not make destructive commands partially execute. Protected commands such as `curse`, `pray`, `fortune`, `help`, `exit`, `quit`, `history`, and `doctor` are not blocked.

Useful commands:

- `curse enable` turns cursed mode on
- `curse disable` turns it off and clears blessing charges
- `curse exorcise` is a more dramatic disable
- `curse status` shows the core state
- `curse inspect` shows a richer status view, including ambient chatter and protected commands
- `curse journal` prints recent curse events
- `curse quiet` silences ambient chatter
- `curse listen` restores the default ambient chatter rate
- `curse chatter <percent>` sets ambient chatter from `0` to `25`
- `curse poke` provokes a harmless reaction
- `curse set-failure-rate <percent>` sets the command failure chance from `0` to `50`
- `pray` adds blessing charges when cursed
- `pray status` shows the current curse state
- `pray hard` adds extra blessing charges
- `fortune` and `fortune read` print a fortune and may add a small omen effect
- `fortune status` shows the curse state and the last omen

Examples:

```text
rsh> curse enable
Cursed mode enabled. This is intentionally silly and opt-in.
Mood: petty. Failure chance: 15%.

rsh> pray
THE HEAP ACCEPTS YOUR OFFERING.
Blessing charges increased to 3.

rsh> curse inspect
Cursed mode: enabled
Blessing charges: 3
Failure chance: 15%
Mood: petty
Ambient chatter: 5%
Last omen: Good omen: the daemon grants one blessing charge.
Ambient flavor: listening
Protected commands: pray, curse, fortune, help, exit, quit, history, doctor

rsh> fortune
A clean build is just a failed build that has not happened yet.
Good omen: the daemon grants one blessing charge.

rsh> curse exorcise
The curse screams in YAML and leaves. Blessing charges cleared.
```

### How Command Blocking Works

- Blocking happens before execution starts.
- Blessing charges are consumed before failure checks.
- The curse state keeps blocking and ambient flavor in memory for the current session.
- `curse disable` and `curse exorcise` clear blessing charges and turn cursed mode off.
- Cursed mode does not affect profile loading, hooks, rituals, or internal automation unless those paths are explicitly user-driven.
- Scripts are not randomly cursed; the host only applies curse blocking to normal user commands.

Command packs can opt in safely through `ICursedShell` on `ShellContext.Services`:

```csharp
using ReaperShell.Abstractions;

var curse = context.Services?.GetService(typeof(ICursedShell)) as ICursedShell;
if (curse?.IsEnabled == true)
{
    curse.AddAmbientEvent("The custom command leaves a strange smell in the heap.");
}
```

Cursed mode never makes commands partially execute or destructive behavior more dangerous.

## Command Packs And Plugins

A command pack is a local folder or Git-backed repo that contains:

- `shellpack.json`
- one or more SDK-style command projects under the configured `commandsPath`
- compiled assemblies that implement `ReaperShell.Abstractions.IShellCommand`

ReaperShell discovers commands from `shellpack.json` and the `commandsPath` entry. Command projects are normal .NET projects, not scripts.

Supported command project types:

- `*.csproj`
- `*.fsproj`
- `*.vbproj`

Typical command pack layout:

```text
tools/
  shellpack.json
  commands/
    hello-kyle/
      HelloKyleCommand.csproj
      HelloKyleCommand.cs
```

Command packs reference `ReaperShell.Abstractions` and implement `IShellCommand`.

`command new` defaults to C#. Supported language aliases include `cs` and `c#` for C#, `fs` and `f#` for F#, and `vbnet`, `visualbasic`, and `visual-basic` for VB.NET. The available templates are `basic`, `file`, and `process`.

### Loading And Reloading

- `repo build <name>` builds a trusted pack.
- `repo load <name>` loads a trusted pack.
- `repo reload <name>` unloads, rebuilds, and reloads the currently checked-out branch. It does not pull, rebase, or switch branches.
- `repo build-all`, `repo load-all`, and `repo reload-all` run the same operations across all trusted repos.

For Git-backed repos, `repo build`, `repo reload`, and `repo reload-all` print the current branch and short commit SHA before building so you can see exactly what is being rebuilt.

### Sample Pack Behavior

The repo ships with:

- [sample-packs/hello-pack](sample-packs/hello-pack)
- [sample-packs/multi-language-pack](sample-packs/multi-language-pack)

The multi-language pack demonstrates supported command-pack languages:

- C#
- F#
- VB.NET

## Repository Management

ReaperShell tracks repo state in `.rsh/settings.json` by default. `repo` commands work with both local packs and Git-backed packs.

### Local Pack Creation

`repo new <name>` creates a managed local command pack and scaffolds a starter command.

It also creates a root `.gitignore` with standard .NET command-pack ignores so build artifacts do not get committed accidentally:

- `bin/`
- `obj/`
- `.vs/`
- `*.user`
- `*.suo`
- `TestResults/`
- `*.nupkg`
- `*.snupkg`

When `repo publish` bootstraps a non-Git pack, it also makes sure that root `.gitignore` exists before the initial commit.

### Git Workflows

| Command | Purpose | Syntax | Example |
| --- | --- | --- | --- |
| `repo add` | Registers an existing local pack or clones a Git URL into managed state. | `repo add <name> <path-or-git-url>` | `repo add tools ./sample-packs/hello-pack` |
| `repo list` | Lists registered repos. | `repo list` | `repo list` |
| `repo remove` | Removes a repo from settings, optionally deleting managed files. | `repo remove <name>`, `repo remove <name> --delete-files` | `repo remove tools` |
| `repo trust` | Marks a repo trusted, with optional autoload/profile behavior. | `repo trust <name> [--autoload] [--load-now] [--profile]` | `repo trust tools --autoload` |
| `repo untrust` | Removes trust from an unloaded repo and clears autoload/profile integration. | `repo untrust <name>` | `repo untrust tools` |
| `repo status` | Shows repo state and Git branch information. | `repo status <name>` | `repo status iis-tools` |
| `repo branches` | Lists local and remote branches. | `repo branches <name>` | `repo branches iis-tools` |
| `repo switch` | Switches branches, creating a tracking branch when needed. | `repo switch <name> <branch> [--force]` | `repo switch iis-tools dev` |
| `repo pull` | Fast-forward-only pull of the current branch. | `repo pull <name>` | `repo pull iis-tools` |
| `repo sync` | Compatibility alias for the same fast-forward-only pull behavior. | `repo sync <name>` | `repo sync iis-tools` |
| `repo commit` | Stages and commits changes. | `repo commit <name> "message"` | `repo commit tools "Update command"` |
| `repo push` | Pushes the repo. | `repo push <name>` | `repo push tools` |
| `repo save` | Commits and then pushes. | `repo save <name> "message"` | `repo save tools "Add command"` |
| `repo publish` | Bootstraps a local pack into GitHub using `gh repo create`. | `repo publish <name> <owner/repo> [--private|--public]` | `repo publish tools YourUser/reapershell-tools --private` |

### Branch Management

Branch management is explicit:

- `repo branches <name>` shows local branches, remote branches, and the detected default remote branch.
- `repo status <name>` shows the current local branch, upstream branch, short commit SHA, dirty state, and available remote branches.
- `repo switch <name> <branch>` fetches `origin` first, then switches branches.
- If the branch exists only remotely, `repo switch` creates a local tracking branch.
- Passing `origin/dev` works too; ReaperShell switches to a local `dev` tracking `origin/dev`.
- `repo pull <name>` uses `git pull --ff-only`.
- `repo sync <name>` uses the same `git pull --ff-only` behavior as `repo pull <name>`.
- `repo reload <name>` never fetches, pulls, rebases, or switches branches.

Important behavior:

- Fetching a branch does not switch to it.
- ReaperShell does not switch branches implicitly during reload.
- Branch switching is always explicit.
- `repo switch` refuses to move a dirty working tree unless `--force` is provided.
- `--force` is conservative and only discards tracked changes when you explicitly ask for it.

### Reload Output

When you run `repo reload <name>` or `repo reload-all`, ReaperShell prints the repo name, current branch, and short commit SHA before building. That makes it obvious what branch is actually being rebuilt.

Recommended workflow for updating a branch and then rebuilding it:

```text
repo switch iis-tools dev
repo pull iis-tools
repo reload iis-tools
```

## Scripts, Profiles, Rituals, And Hooks

### State Directory

By default, ReaperShell stores runtime state under `.rsh` in the current working directory. You can move that with `--state-dir <path>`.

The default state directory contains:

- `settings.json`
- `profile.rsh`
- `rituals/`
- `repos/` for managed repos created by ReaperShell

### Profiles

The interactive shell runs a startup profile by default.

- Default profile: `.rsh/profile.rsh`
- Explicit profile: `--profile <path>`
- Disable profiles: `--no-profile`

Profiles are plain `.rsh` scripts.

### Rituals

Rituals are named `.rsh` scripts stored under `.rsh/rituals/`.

- `ritual list` lists available ritual names
- `ritual path` prints the rituals directory
- `ritual new <name>` creates a new ritual
- `ritual run <name>` runs one ritual
- `ritual run <name> --continue-on-error` keeps going after failures

### Hooks

Hooks connect shell events to rituals.

- `hook events` lists supported event names
- `hook add <event> <ritual-name>` attaches a ritual
- `hook list` shows configured hooks
- `hook remove <event> <ritual-name>` removes one ritual from one hook
- `hook clear <event>` clears one hook

Hooked rituals live in `.rsh/rituals/` and are referenced by name.

## Aliases

Aliases are stored in `.rsh/settings.json` and expand before execution.

Examples:

```text
rsh> alias set ll "ls -a"
rsh> alias show ll
rsh> alias remove ll
rsh> alias clear
```

Notes:

- Aliases are session-facing command replacements.
- Recursive alias loops are blocked.
- Built-in command names cannot be replaced with aliases.

## Configuration And State

ReaperShell keeps its runtime state outside the source tree by default.

Key files and directories:

- `.rsh/settings.json` stores repos, aliases, hooks, editor choice, and external command mode
- `.rsh/profile.rsh` is the default startup profile
- `.rsh/rituals/` stores ritual scripts
- `.rsh/repos/` stores managed local repos created by `repo new` or some Git URL workflows

Cursed mode state is session-only. Blessing charges, ambient chatter level, mood, the journal, and other curse flavor live in memory for the current process and are not persisted in `.rsh/settings.json`.

What to commit:

- commit source files, command-pack projects, manifests, and intentional pack content
- do not commit `.rsh/` runtime state
- do not commit `bin/` and `obj/` build outputs

Generated command packs and Git-backed packs are scaffolded with a `.gitignore` that covers common .NET artifacts.

## Writing Your Own Command

A ReaperShell command is a normal .NET class that implements `IShellCommand`.

Minimal C# example:

```csharp
using ReaperShell.Abstractions;

namespace HelloCommand;

public sealed class HelloCommand : IShellCommand
{
    public string Name => "hello";

    public string Description => "Prints a hello message from a live-loaded command.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("Hello from sample command pack.");
        return Task.FromResult(0);
    }
}
```

What matters:

- `Name` is the command name
- `Description` shows up in `help`
- write normal output through `ShellContext.WriteLine`
- write errors through `ShellContext.WriteErrorLine`
- return `0` for success and a non-zero exit code for failure

### Optional: Cursed Mode Integration

Cursed mode is optional. A command can work perfectly well without knowing anything about it, and older hosts may not provide `ICursedShell` at all.

If you do opt in, keep the integration harmless:

- read the shared curse state from `ShellContext.Services`
- treat curse interactions as flavor, journaling, or small one-shot hints
- avoid permanent failure changes, file-system side effects, or external process behavior

Example:

```csharp
using ReaperShell.Abstractions;

var curse = context.Services?.GetService(typeof(ICursedShell)) as ICursedShell;
if (curse?.IsEnabled == true)
{
    curse.AddAmbientEvent("The custom command leaves a strange smell in the heap.");
}
```

## Sample Command Packs

The repository includes sample packs under `sample-packs/`:

- `hello-pack` demonstrates a simple C# command pack
- `multi-language-pack` demonstrates C#, F#, and VB.NET command projects

Typical usage:

```text
rsh> repo add sample ./sample-packs/hello-pack
rsh> repo trust sample
rsh> repo build sample
rsh> repo load sample
rsh> hello
```

## Troubleshooting

- `command not found`: run `help`, `plugins`, or `which <name>` to see whether the command is built-in, loaded from a pack, or missing from PATH
- `command pack does not load`: make sure you ran `repo build <name>` first and that the pack is trusted
- `repo is on the wrong branch`: use `repo status <name>` and `repo branches <name>`, then `repo switch <name> <branch>`
- `reload did not update the command`: verify you switched branches first, then run `repo reload <name>`
- `repo switch` refuses: the working tree has uncommitted changes; use `--force` only if you want to discard tracked changes
- `git branch exists remotely but not locally`: `repo switch <name> <branch>` will create a tracking branch if `origin/<branch>` exists
- `plugin unload warning`: unload is requested, not guaranteed, because collectible unload depends on references being released
- `build fails`: inspect the output from `repo build` or `repo reload`; `doctor --verbose` can also help check the environment
- `missing ReaperShell.Abstractions`: make sure command projects reference the workspace copy of `ReaperShell.Abstractions`
- `scripts not running`: check the script path, and remember that `--continue-on-error` only applies to script execution
- `profile path issues`: verify `.rsh/profile.rsh` exists, or use `--profile <path>` to point at a specific profile
- `commands sometimes refuse to run`: check whether cursed mode is enabled with `curse status` or `curse inspect`; run `curse disable` or `curse exorcise` to turn it off
- `ambient messages are too noisy`: use `curse quiet` or lower chatter with `curse chatter <percent>`
- `command packs do not see cursed mode`: make sure they reference the current `ReaperShell.Abstractions` package and check `ShellContext.Services` for `ICursedShell`

## Development

Repository layout:

- `src/ReaperShell` - host shell and built-in commands
- `src/ReaperShell.Abstractions` - the command-pack interface and shared abstractions
- `tests/ReaperShell.Tests` - unit and integration tests
- `sample-packs/` - runnable example packs

Useful commands:

```powershell
dotnet build ReaperShell.slnx
dotnet test tests/ReaperShell.Tests/ReaperShell.Tests.csproj
dotnet run --project src/ReaperShell
```

If you want to extend the shell:

- add a built-in command under `src/ReaperShell/BuiltIns`
- register it in `Program.RegisterBuiltIns`
- add a command-pack command by creating a project under a pack's `commands/` directory and implementing `IShellCommand`

The codebase favors straightforward, explicit command handlers and direct .NET APIs over extra abstraction.

## Roadmap And Status

ReaperShell is an early experimental project. It is useful for local tooling, experimentation, and learning, but it is not trying to be a full general-purpose shell replacement.

Expect the surface area to evolve as command-pack workflows, repo workflows, and shell ergonomics mature.

## License

MIT License. See [LICENSE](LICENSE).

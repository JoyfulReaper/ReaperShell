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

## Smoke Tests

The repository includes smoke-test scripts for the sample pack and generated packs:

- [scripts/smoke-sample.rsh](/C:/GitHub/ReaperShell/scripts/smoke-sample.rsh)
- [scripts/smoke-generated.rsh](/C:/GitHub/ReaperShell/scripts/smoke-generated.rsh)

Run both through the convenience PowerShell harness:

```powershell
./scripts/run-smoke.ps1
```

## Security Warning

Loading command packs executes arbitrary code on your machine. Only trust repos you control or have reviewed.

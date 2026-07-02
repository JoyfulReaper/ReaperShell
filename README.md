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

## Security Warning

Loading command packs executes arbitrary code on your machine. Only trust repos you control or have reviewed.

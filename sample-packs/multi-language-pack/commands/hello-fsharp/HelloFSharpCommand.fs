namespace HelloFSharpCommand

open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open ReaperShell.Abstractions

type HelloFSharpCommand() =
    interface IShellCommand with
        member _.Name = "hello-fsharp"

        member _.Description = "Prints a hello message from an F# command pack."

        member _.ExecuteAsync(
            context: ShellContext,
            args: IReadOnlyList<string>,
            cancellationToken: CancellationToken) =
            context.WriteLine("Hello from the F# command pack.")
            Task.FromResult(0)

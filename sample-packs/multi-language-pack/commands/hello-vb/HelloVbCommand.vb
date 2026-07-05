Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports ReaperShell.Abstractions

Namespace HelloVbCommand
    Public NotInheritable Class HelloVbCommand
        Implements IShellCommand

        Public ReadOnly Property Name As String Implements IShellCommand.Name
            Get
                Return "hello-vb"
            End Get
        End Property

        Public ReadOnly Property Description As String Implements IShellCommand.Description
            Get
                Return "Prints a hello message from a VB.NET command pack."
            End Get
        End Property

        Public Function ExecuteAsync(
            context As ShellContext,
            args As IReadOnlyList(Of String),
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer) Implements IShellCommand.ExecuteAsync
            context.WriteLine("Hello from the VB.NET command pack.")
            Return Task.FromResult(0)
        End Function
    End Class
End Namespace

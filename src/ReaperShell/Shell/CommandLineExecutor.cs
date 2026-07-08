using ReaperShell.Abstractions;
using System.Text;

namespace ReaperShell.Shell;

public sealed class CommandLineExecutor
{
    public async Task<int> ExecuteAsync(
        ShellContext context,
        CommandLine commandLine,
        Func<ShellContext, IReadOnlyList<string>, CommandExecutionOptions, CancellationToken, Task<int>> executeSegmentAsync,
        CommandExecutionOptions segmentOptions,
        CancellationToken cancellationToken)
    {
        var exitCode = 0;
        for (var pipelineIndex = 0; pipelineIndex < commandLine.Pipelines.Count; pipelineIndex++)
        {
            var pipeline = commandLine.Pipelines[pipelineIndex];
            if (pipelineIndex > 0)
            {
                var previousOperator = commandLine.Pipelines[pipelineIndex - 1].NextOperator;
                if (previousOperator is CommandChainOperator.AndAlso && exitCode != 0)
                {
                    continue;
                }

                if (previousOperator is CommandChainOperator.OrElse && exitCode == 0)
                {
                    continue;
                }
            }

            exitCode = await ExecutePipelineAsync(
                context,
                pipeline,
                executeSegmentAsync,
                segmentOptions,
                cancellationToken);
        }

        return exitCode;
    }

    private static async Task<int> ExecutePipelineAsync(
        ShellContext context,
        CommandPipeline pipeline,
        Func<ShellContext, IReadOnlyList<string>, CommandExecutionOptions, CancellationToken, Task<int>> executeSegmentAsync,
        CommandExecutionOptions segmentOptions,
        CancellationToken cancellationToken)
    {
        var currentInput = context.Input;
        var exitCode = 0;

        for (var segmentIndex = 0; segmentIndex < pipeline.Segments.Count; segmentIndex++)
        {
            var segment = pipeline.Segments[segmentIndex];
            var isLastSegment = segmentIndex == pipeline.Segments.Count - 1;
            var captureStdout = !isLastSegment;

            using var segmentResources = OpenSegmentResources(context, segment, currentInput, captureStdout, isLastSegment);
            exitCode = await executeSegmentAsync(
                segmentResources.Context,
                segment.Tokens,
                segmentOptions,
                cancellationToken);

            currentInput = captureStdout
                ? new StringReader(segmentResources.CapturedStdout?.ToString() ?? string.Empty)
                : TextReader.Null;
        }

        return exitCode;
    }

    private static SegmentResources OpenSegmentResources(
        ShellContext parentContext,
        CommandSegment segment,
        TextReader currentInput,
        bool captureStdout,
        bool isLastSegment)
    {
        var disposables = new List<IDisposable>();
        if (currentInput is IDisposable disposableInput &&
            !ReferenceEquals(currentInput, parentContext.Input))
        {
            disposables.Add(disposableInput);
        }

        var stdoutRedirection = segment.Redirections.FirstOrDefault(redirection =>
            redirection.Kind is CommandRedirectionKind.StdoutOverwrite or
                CommandRedirectionKind.StdoutAppend or
                CommandRedirectionKind.CombinedOverwrite or
                CommandRedirectionKind.CombinedAppend);

        var stderrRedirection = segment.Redirections.FirstOrDefault(redirection =>
                redirection.Kind is CommandRedirectionKind.StderrOverwrite or
                CommandRedirectionKind.StderrAppend or
                CommandRedirectionKind.CombinedOverwrite or
                CommandRedirectionKind.CombinedAppend);

        var captureWriter = captureStdout ? new StringWriter() : null;
        if (captureWriter is not null && stdoutRedirection is null)
        {
            disposables.Add(captureWriter);
        }

        if (stdoutRedirection is not null &&
            ReferenceEquals(stdoutRedirection, stderrRedirection))
        {
            var sharedWriter = OpenRedirectionWriter(parentContext.WorkingDirectory, stdoutRedirection.TargetPath, stdoutRedirection.Kind);

            TextWriter combinedStdoutWriter = captureWriter is null
                ? sharedWriter
                : new TeeTextWriter(sharedWriter, captureWriter);

            disposables.Add(combinedStdoutWriter);

            var combinedContext = new ShellContext(
                combinedStdoutWriter,
                sharedWriter,
                currentInput,
                parentContext.WorkingDirectory,
                parentContext.Services,
                parentContext.CancellationToken,
                parentContext.ColorMode);

            return new SegmentResources(combinedContext, captureWriter, disposables);
        }

        var stdoutWriter = SelectStdoutWriter(parentContext, stdoutRedirection, captureWriter, isLastSegment, disposables);
        var stderrWriter = SelectStderrWriter(parentContext, stderrRedirection, disposables);
        var segmentContext = new ShellContext(
            stdoutWriter,
            stderrWriter,
            currentInput,
            parentContext.WorkingDirectory,
            parentContext.Services,
            parentContext.CancellationToken,
            parentContext.ColorMode);

        return new SegmentResources(segmentContext, captureWriter, disposables);
    }

    private static TextWriter SelectStdoutWriter(
        ShellContext parentContext,
        CommandRedirection? stdoutRedirection,
        StringWriter? captureWriter,
        bool isLastSegment,
        List<IDisposable> disposables)
    {
        if (stdoutRedirection is null)
        {
            return isLastSegment
                ? parentContext.Out
                : captureWriter!;
        }

        var fileWriter = OpenRedirectionWriter(parentContext.WorkingDirectory, stdoutRedirection.TargetPath, stdoutRedirection.Kind);
        if (captureWriter is null)
        {
            disposables.Add(fileWriter);
            return fileWriter;
        }

        var teeWriter = new TeeTextWriter(fileWriter, captureWriter);
        disposables.Add(teeWriter);
        return teeWriter;
    }

    private static TextWriter SelectStderrWriter(
        ShellContext parentContext,
        CommandRedirection? stderrRedirection,
        List<IDisposable> disposables)
    {
        if (stderrRedirection is null)
        {
            return parentContext.Error;
        }

        var fileWriter = OpenRedirectionWriter(parentContext.WorkingDirectory, stderrRedirection.TargetPath, stderrRedirection.Kind);
        disposables.Add(fileWriter);
        return fileWriter;
    }

    private static StreamWriter OpenRedirectionWriter(
        DirectoryInfo workingDirectory,
        string targetPath,
        CommandRedirectionKind redirectionKind)
    {
        var fullPath = Path.GetFullPath(targetPath, workingDirectory.FullName);
        var append = redirectionKind is CommandRedirectionKind.StdoutAppend or
            CommandRedirectionKind.StderrAppend or
            CommandRedirectionKind.CombinedAppend;
        var fileMode = append ? FileMode.Append : FileMode.Create;
        var stream = new FileStream(fullPath, fileMode, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream) { AutoFlush = true };
    }

    private sealed record SegmentResources(
        ShellContext Context,
        StringWriter? CapturedStdout,
        IReadOnlyList<IDisposable> Disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in Disposables)
            {
                disposable.Dispose();
            }
        }
    }

    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter[] _writers;

        public TeeTextWriter(params TextWriter[] writers)
        {
            _writers = writers;
        }

        public override Encoding Encoding => _writers[0].Encoding;

        public override void Write(char value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            foreach (var writer in _writers)
            {
                writer.Dispose();
            }
        }
    }
}

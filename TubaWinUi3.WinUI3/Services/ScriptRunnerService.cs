using System.Diagnostics;
using System.Text;

namespace TubaWinUi3.Services;

public sealed class ScriptRunResult
{
    public int ExitCode { get; init; }
    public bool Success => ExitCode == 0;
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
    public TimeSpan Duration { get; init; }
}

public sealed class ScriptRunRequest
{
    public string FileName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public bool RunAsAdmin { get; init; }
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
    public Encoding? OutputEncoding { get; init; }
    public string? InputText { get; init; }
}

public enum ScriptOutputKind
{
    Normal,
    Error,
    ProgressUpdate
}

public static class ScriptRunnerService
{
    public static Task<ScriptRunResult> RunAsync(
        ScriptRunRequest request,
        Action<string, ScriptOutputKind>? onOutput = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => RunCoreAsync(request, onOutput, ct), ct);
    }

    private static async Task<ScriptRunResult> RunCoreAsync(
        ScriptRunRequest request,
        Action<string, ScriptOutputKind>? onOutput,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = request.OutputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = request.OutputEncoding ?? Encoding.UTF8
        };

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
                psi.Environment[key] = value;
        }

        if (request.RunAsAdmin)
        {
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            psi.CreateNoWindow = false;
            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            psi.RedirectStandardInput = false;
        }

        using var process = Process.Start(psi);
        if (process is null)
            return new ScriptRunResult { ExitCode = -1, Error = "无法启动进程", Duration = sw.Elapsed };

        if (request.RunAsAdmin)
        {
            await process.WaitForExitAsync(ct);
            sw.Stop();
            return new ScriptRunResult
            {
                ExitCode = process.ExitCode,
                Duration = sw.Elapsed,
                Output = "(以管理员身份运行，无法捕获输出)"
            };
        }

        if (!string.IsNullOrEmpty(request.InputText))
        {
            await process.StandardInput.WriteAsync(request.InputText.AsMemory(), ct);
            process.StandardInput.Close();
        }

        var outputTask = ReadStreamAsync(process.StandardOutput, outputBuilder, onOutput, ScriptOutputKind.Normal, ct);
        var errorTask = ReadStreamAsync(process.StandardError, errorBuilder, onOutput, ScriptOutputKind.Error, ct);

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(ct);
        sw.Stop();

        return new ScriptRunResult
        {
            ExitCode = process.ExitCode,
            Output = outputBuilder.ToString(),
            Error = errorBuilder.ToString(),
            Duration = sw.Elapsed
        };
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        StringBuilder builder,
        Action<string, ScriptOutputKind>? callback,
        ScriptOutputKind defaultKind,
        CancellationToken ct)
    {
        var partialLine = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var buf = new char[1];
                var read = await reader.ReadAsync(buf.AsMemory(), ct);
                if (read == 0) break;

                var ch = buf[0];

                if (ch == '\r')
                {
                    var lineSoFar = partialLine.ToString();
                    if (!string.IsNullOrEmpty(lineSoFar))
                    {
                        callback?.Invoke(lineSoFar, ScriptOutputKind.ProgressUpdate);
                    }
                    partialLine.Clear();
                }
                else if (ch == '\n')
                {
                    var lineSoFar = partialLine.ToString();
                    partialLine.Clear();
                    if (!string.IsNullOrEmpty(lineSoFar))
                    {
                        builder.AppendLine(lineSoFar);
                        callback?.Invoke(lineSoFar, defaultKind);
                    }
                    else
                    {
                        builder.AppendLine();
                        callback?.Invoke("", defaultKind);
                    }
                }
                else
                {
                    partialLine.Append(ch);
                }
            }
            catch
            {
                break;
            }
        }

        if (partialLine.Length > 0)
        {
            var remaining = partialLine.ToString();
            builder.AppendLine(remaining);
            callback?.Invoke(remaining, defaultKind);
        }
    }
}

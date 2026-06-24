using System.Diagnostics;
using System.Text.Json;

namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed class RbacExecutionContext
{
    public string PrincipalType { get; set; } = "ServicePrincipal";

    public static CommandResult RunCommand(IReadOnlyList<string> args, bool capture)
    {
        string executable = args[0];
        bool wrapInCmd = OperatingSystem.IsWindows() && string.Equals(executable, "az", StringComparison.OrdinalIgnoreCase);

        ProcessStartInfo psi = new()
        {
            FileName = wrapInCmd ? "cmd.exe" : executable,
            UseShellExecute = false,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
        };

        if (wrapInCmd)
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("az");
        }

        for (int i = 1; i < args.Count; i++)
        {
            psi.ArgumentList.Add(args[i]);
        }

        using Process process = new() { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new CommandResult(127, string.Empty, ex.Message);
        }

        string stdOut = string.Empty;
        string stdErr = string.Empty;
        if (capture)
        {
            stdOut = process.StandardOutput.ReadToEnd();
            stdErr = process.StandardError.ReadToEnd();
        }

        process.WaitForExit();
        return new CommandResult(process.ExitCode, stdOut, stdErr);
    }

    public static (bool Ok, JsonElement? Json, string Message) RunJson(IReadOnlyList<string> args)
    {
        var command = RunCommand(args, capture: true);
        if (command.ExitCode != 0)
        {
            return (false, null, command.StdErr.Trim());
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(command.StdOut);
            return (true, doc.RootElement.Clone(), string.Empty);
        }
        catch (JsonException)
        {
            return (false, null, command.StdOut.Trim());
        }
    }

    public bool ResourceExists(List<string> checkCommand, string resourceLabel)
    {
        var (Ok, Json, Message) = RunJson(checkCommand);
        if (!Ok)
        {
            PrintWarning($"{resourceLabel} not found or not accessible - skipping.");
            return false;
        }

        return true;
    }

    public static string? GetString(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    public static bool GetBool(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out JsonElement next))
            {
                return false;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.True ||
               (current.ValueKind == JsonValueKind.String && bool.TryParse(current.GetString(), out bool parsed) && parsed);
    }

    public static void PrintHeader(string message)
    {
        int width = Math.Max(message.Length + 4, 66);
        Console.WriteLine();
        Console.WriteLine(new string('=', width));
        Console.WriteLine($"  {message}");
        Console.WriteLine(new string('=', width));
    }

    public static void PrintSection(string message)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {message} ---");
    }

    public static void PrintStep(string message) => Console.WriteLine($"  > {message}");

    public static void PrintSuccess(string message) => Console.WriteLine($"  [OK] {message}");

    public static void PrintWarning(string message) => Console.WriteLine($"  [WARN] {message}");

    public static void PrintError(string message) => Console.Error.WriteLine($"  [ERROR] {message}");

    public static void PrintDetail(string message) => Console.WriteLine($"      {message}");

    public static void PrintSkip(string message) => Console.WriteLine($"  [SKIP] {message}");
}

internal readonly record struct CommandResult(int ExitCode, string StdOut, string StdErr);


// SPDX-License-Identifier: Apache-2.0
// Witnessed execution for Microsoft Agent Framework — the .NET -> witseal bridge.
//
// This is the .NET analog of the OpenHands adapter's `run_through_witseal`
// (src/adapters/openhands/witseal_openhands.py): it shells out to the unchanged
// witseal CLI as a child process —
//
//     node <WITSEAL_CLI_ENTRY> --data-dir <dir> exec --mode <mode> -- /bin/sh -c <command>
//
// captures stdout + stderr, and parses the witness footer printed on stderr
// (`event=evt_... receipt=rcpt_...`). WitSeal owns the actual command execution
// (L3) and emits a full, independently verifiable execution receipt. The witseal
// core / canon / golden are untouched; this only invokes the built CLI.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WitSeal.AgentFramework;

/// <summary>Execution mode passed to <c>witseal exec --mode</c>.</summary>
public enum WitSealMode
{
    /// <summary>Deny-by-default. A command with no allowing policy is blocked and recorded as evidence; it does not run.</summary>
    Gate,

    /// <summary>Observe-and-record. The command runs and a receipt is produced regardless of policy.</summary>
    Witness,
}

/// <summary>How to reach the witseal CLI and which data directory to witness into.</summary>
public sealed class WitSealBridgeOptions
{
    /// <summary>Absolute path to the built CLI entry, <c>dist/src/cli/index.js</c>. Required.</summary>
    public required string CliEntry { get; init; }

    /// <summary>WitSeal data directory (chain, policy packs, receipts). Defaults to <c>~/.witseal</c>.</summary>
    public string DataDir { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".witseal");

    /// <summary>The Node executable used to run the CLI. Defaults to <c>node</c> (resolved via PATH).</summary>
    public string Node { get; init; } = "node";

    /// <summary>Gate (deny-by-default) or Witness (observe-and-record). Defaults to Gate.</summary>
    public WitSealMode Mode { get; init; } = WitSealMode.Gate;

    /// <summary>Chain segment id (<c>--segment</c>). Defaults to <c>default</c>.</summary>
    public string Segment { get; init; } = "default";

    /// <summary>Optional working directory for the executed command (<c>--cwd</c>).</summary>
    public string? Cwd { get; init; }

    /// <summary>
    /// Build options from environment variables:
    /// <list type="bullet">
    ///   <item><c>WITSEAL_CLI_ENTRY</c> — absolute path to dist/src/cli/index.js (required)</item>
    ///   <item><c>WITSEAL_DATA_DIR</c>  — data dir (default ~/.witseal)</item>
    ///   <item><c>WITSEAL_MODE</c>      — gate | witness (default gate)</item>
    ///   <item><c>WITSEAL_NODE</c>      — node executable (default "node")</item>
    /// </list>
    /// </summary>
    public static WitSealBridgeOptions FromEnvironment()
    {
        var cliEntry = Environment.GetEnvironmentVariable("WITSEAL_CLI_ENTRY");
        if (string.IsNullOrWhiteSpace(cliEntry))
        {
            throw new InvalidOperationException(
                "WITSEAL_CLI_ENTRY must point at the built dist/src/cli/index.js");
        }

        var dataDir = Environment.GetEnvironmentVariable("WITSEAL_DATA_DIR");
        var node = Environment.GetEnvironmentVariable("WITSEAL_NODE");
        var modeStr = Environment.GetEnvironmentVariable("WITSEAL_MODE");
        var mode = string.Equals(modeStr, "witness", StringComparison.OrdinalIgnoreCase)
            ? WitSealMode.Witness
            : WitSealMode.Gate;

        return new WitSealBridgeOptions
        {
            CliEntry = cliEntry,
            DataDir = string.IsNullOrWhiteSpace(dataDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".witseal")
                : dataDir,
            Node = string.IsNullOrWhiteSpace(node) ? "node" : node,
            Mode = mode,
        };
    }
}

/// <summary>The outcome of routing one command through the witseal pipeline.</summary>
public sealed class WitSealRunResult
{
    /// <summary>WitSeal's reserved exit code for a Gate denial (deny-by-default block).</summary>
    public const int DeniedExitCode = 100;

    /// <summary>Process exit code returned by the witseal CLI.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Captured standard output of the executed command.</summary>
    public required string Stdout { get; init; }

    /// <summary>Captured standard error, including the witness footer.</summary>
    public required string Stderr { get; init; }

    /// <summary>The <c>rcpt_…</c> execution-receipt id parsed from the witness footer, if any.</summary>
    public string? ReceiptId { get; init; }

    /// <summary>The <c>evt_…</c> witness-event id parsed from the witness footer, if any.</summary>
    public string? EventId { get; init; }

    /// <summary>True when the command was blocked by policy (deny-by-default) and did not run.</summary>
    public bool Denied => ExitCode == DeniedExitCode;
}

/// <summary>
/// The cross-language bridge to the witseal runtime. Runs a freeform shell
/// command through <c>witseal exec</c> via <see cref="Process"/> and returns the
/// captured output plus the receipt / event ids.
/// </summary>
public sealed class WitSealBridge
{
    // Matches the witness footer printed by the CLI on stderr.
    private static readonly Regex ReceiptRe = new(@"receipt=(rcpt_[A-Za-z0-9]+)", RegexOptions.Compiled);
    private static readonly Regex EventRe = new(@"event=(evt_[A-Za-z0-9]+)", RegexOptions.Compiled);

    private readonly WitSealBridgeOptions _options;

    /// <summary>Create a bridge bound to the given CLI / data-dir options.</summary>
    public WitSealBridge(WitSealBridgeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The options this bridge was constructed with.</summary>
    public WitSealBridgeOptions Options => _options;

    /// <summary>
    /// Run a freeform shell command through the witseal pipeline. Mirrors the
    /// OpenHands / OpenCode adapters: the command is executed as
    /// <c>/bin/sh -c "&lt;command&gt;"</c> under the witness boundary.
    /// </summary>
    public async Task<WitSealRunResult> RunAsync(string command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var psi = new ProcessStartInfo
        {
            FileName = _options.Node,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // node <cli> --data-dir <dir> --segment <seg> exec --mode <mode> [--cwd <cwd>] -- /bin/sh -c <command>
        psi.ArgumentList.Add(_options.CliEntry);
        psi.ArgumentList.Add("--data-dir");
        psi.ArgumentList.Add(_options.DataDir);
        psi.ArgumentList.Add("--segment");
        psi.ArgumentList.Add(_options.Segment);
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add(_options.Mode == WitSealMode.Witness ? "witness" : "gate");
        if (!string.IsNullOrEmpty(_options.Cwd))
        {
            psi.ArgumentList.Add("--cwd");
            psi.ArgumentList.Add(_options.Cwd!);
        }
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("/bin/sh");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        using var proc = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!proc.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start witseal bridge process '{_options.Node} {_options.CliEntry}'.");
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stderrText = stderr.ToString();
        var receiptMatch = ReceiptRe.Match(stderrText);
        var eventMatch = EventRe.Match(stderrText);

        return new WitSealRunResult
        {
            ExitCode = proc.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderrText,
            ReceiptId = receiptMatch.Success ? receiptMatch.Groups[1].Value : null,
            EventId = eventMatch.Success ? eventMatch.Groups[1].Value : null,
        };
    }

    /// <summary>Synchronous convenience wrapper over <see cref="RunAsync"/>.</summary>
    public WitSealRunResult Run(string command)
        => RunAsync(command).GetAwaiter().GetResult();
}

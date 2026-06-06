// SPDX-License-Identifier: Apache-2.0
// Witnessed execution for Microsoft Agent Framework — mechanism (A):
// the WitSeal-authored tool.
//
// `AIFunctionFactory.Create(method, ...)` turns a plain method into an
// `AIFunction` (an `AITool`) that an agent can call. Here the method body shells
// out to the witseal CLI through `WitSealBridge`, so when the model picks this
// tool, the command runs under the WitSeal witness boundary and the receipt id
// is returned to the model. Hand the result of `Create()` to
// `ChatClientAgent(..., tools: [witsealTool])` (or `ChatOptions.Tools`).

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace WitSeal.AgentFramework;

/// <summary>
/// Factory for the WitSeal-authored execution tool. The returned
/// <see cref="AIFunction"/> is a drop-in replacement for an agent's "run a shell
/// command" tool: every invocation is executed by WitSeal and yields a verifiable
/// execution receipt instead of running on a raw, unwitnessed shell.
/// </summary>
public static class WitSealTool
{
    /// <summary>Default tool name exposed to the model.</summary>
    public const string DefaultName = "run_shell_command";

    /// <summary>Default tool description exposed to the model.</summary>
    public const string DefaultDescription =
        "Execute a shell command on the host through the WitSeal witnessed-execution " +
        "runtime. Every command is mediated and recorded as an independently " +
        "verifiable execution receipt. Returns the command output together with the " +
        "WitSeal receipt id.";

    /// <summary>
    /// Create the WitSeal-authored tool over an existing <paramref name="bridge"/>.
    /// </summary>
    /// <param name="bridge">The configured bridge to the witseal CLI.</param>
    /// <param name="name">Tool name shown to the model (defaults to <see cref="DefaultName"/>).</param>
    /// <param name="description">Tool description shown to the model.</param>
    public static AIFunction Create(
        WitSealBridge bridge,
        string? name = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        // The delegate the model effectively calls. Its single parameter becomes
        // the tool's JSON-schema input; the string return becomes the tool result.
        // AIFunctionFactory reads the parameter name/type to build the schema.
        async Task<string> RunShellCommand(
            string command,
            CancellationToken cancellationToken)
        {
            var result = await bridge.RunAsync(command, cancellationToken).ConfigureAwait(false);
            return Render(result);
        }

        return AIFunctionFactory.Create(
            RunShellCommand,
            name ?? DefaultName,
            description ?? DefaultDescription,
            serializerOptions: JsonSerializerOptions.Default);
    }

    /// <summary>
    /// Create the WitSeal-authored tool directly from bridge options (convenience).
    /// </summary>
    public static AIFunction Create(
        WitSealBridgeOptions options,
        string? name = null,
        string? description = null)
        => Create(new WitSealBridge(options), name, description);

    /// <summary>
    /// Render a <see cref="WitSealRunResult"/> into the text returned to the model.
    /// A denial is reported as such (the command did not run); a success carries a
    /// witness footer with the receipt id so it can be independently verified.
    /// </summary>
    public static string Render(WitSealRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Denied)
        {
            return "[witseal] command DENIED by policy (deny-by-default); it did not run. " +
                   $"Recorded as evidence (event {result.EventId}).";
        }

        var sb = new StringBuilder();
        sb.Append(result.Stdout);
        if (result.Stdout.Length > 0 && !result.Stdout.EndsWith('\n'))
        {
            sb.Append('\n');
        }
        sb.Append("[witseal: receipt=").Append(result.ReceiptId ?? "(none)")
          .Append(" event=").Append(result.EventId ?? "(none)")
          .Append(" exit=").Append(result.ExitCode)
          .Append(" — full execution receipt recorded; verify with `witseal verify`]");
        return sb.ToString();
    }
}

// SPDX-License-Identifier: Apache-2.0
// Witnessed execution for Microsoft Agent Framework — mechanism (B):
// function-invocation middleware.
//
// Microsoft Agent Framework runs the model's tool calls through
// `FunctionInvokingChatClient`. Its `FunctionInvoker` hook is the function-calling
// middleware: it receives a `FunctionInvocationContext` (which tool, which
// arguments) and is responsible for producing the tool result. This adapter
// installs a `FunctionInvoker` that, for the configured command-running tool(s),
// routes execution through WitSeal instead of letting the tool run unwitnessed,
// then short-circuits the default invocation via `context.Terminate = true` and
// returns the witnessed result object.
//
// This is the `context.Terminate` / result-object model the prompt describes. In
// Microsoft.Extensions.AI 10.5 the "result" is the value returned from the
// `FunctionInvoker` delegate (there is no separate `context.Result` property);
// setting `Terminate = true` stops the agent loop after this result.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace WitSeal.AgentFramework;

/// <summary>
/// A function-invocation middleware that mediates an agent's command execution
/// through WitSeal. Install it on the chat client that backs a
/// <c>ChatClientAgent</c>; calls to the targeted tool(s) are executed by WitSeal
/// and recorded as verifiable execution receipts.
/// </summary>
public sealed class WitSealFunctionInvocation
{
    private readonly WitSealBridge _bridge;
    private readonly Func<FunctionInvocationContext, bool> _shouldWitness;
    private readonly Func<FunctionInvocationContext, string?> _extractCommand;
    private readonly bool _terminateAfterWitness;

    /// <param name="bridge">Configured bridge to the witseal CLI.</param>
    /// <param name="shouldWitness">
    /// Predicate selecting which tool calls to route through WitSeal. Defaults to
    /// tools named <see cref="WitSealTool.DefaultName"/> ("run_shell_command").
    /// </param>
    /// <param name="extractCommand">
    /// Extracts the command string from the call arguments. Defaults to reading the
    /// <c>command</c> argument.
    /// </param>
    /// <param name="terminateAfterWitness">
    /// When true (default), the agent loop stops after a witnessed result by setting
    /// <see cref="FunctionInvocationContext.Terminate"/>. Set false to let the loop
    /// continue with the witnessed result fed back to the model.
    /// </param>
    public WitSealFunctionInvocation(
        WitSealBridge bridge,
        Func<FunctionInvocationContext, bool>? shouldWitness = null,
        Func<FunctionInvocationContext, string?>? extractCommand = null,
        bool terminateAfterWitness = false)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _shouldWitness = shouldWitness ?? DefaultShouldWitness;
        _extractCommand = extractCommand ?? DefaultExtractCommand;
        _terminateAfterWitness = terminateAfterWitness;
    }

    private static bool DefaultShouldWitness(FunctionInvocationContext ctx)
        => string.Equals(ctx.Function.Name, WitSealTool.DefaultName, StringComparison.Ordinal);

    private static string? DefaultExtractCommand(FunctionInvocationContext ctx)
        => ctx.Arguments.TryGetValue("command", out var v) ? v?.ToString() : null;

    /// <summary>
    /// The middleware body. Either routes the call through WitSeal (and returns the
    /// witnessed result, short-circuiting the underlying tool), or falls through to
    /// the tool's own invocation for calls that should not be witnessed.
    /// </summary>
    public async ValueTask<object?> InvokeAsync(
        FunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_shouldWitness(context))
        {
            // Not a witnessed tool — preserve default behavior.
            return await context.Function.InvokeAsync(context.Arguments, cancellationToken)
                .ConfigureAwait(false);
        }

        var command = _extractCommand(context);
        if (string.IsNullOrEmpty(command))
        {
            return "[witseal] no `command` argument supplied; nothing executed.";
        }

        // L3: WitSeal owns this execution. The model's chosen tool body never runs
        // on a raw shell — the command goes through the witness boundary instead.
        var result = await _bridge.RunAsync(command!, cancellationToken).ConfigureAwait(false);

        if (_terminateAfterWitness)
        {
            context.Terminate = true;
        }

        return WitSealTool.Render(result);
    }

    /// <summary>
    /// Convenience: wrap a base <see cref="IChatClient"/> so its function calls are
    /// witnessed. Equivalent to
    /// <c>baseClient.AsBuilder().UseFunctionInvocation(configure: c =&gt; c.FunctionInvoker = mw.InvokeAsync).Build()</c>.
    /// Use the returned client to build a <c>ChatClientAgent</c>.
    /// </summary>
    public IChatClient Install(IChatClient baseClient, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(baseClient);
        return baseClient
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory, client => client.FunctionInvoker = InvokeAsync)
            .Build();
    }
}

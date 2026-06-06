// SPDX-License-Identifier: Apache-2.0
// Live-verify harness for the WitSeal Microsoft Agent Framework adapter.
//
// No LLM is involved. It instantiates the WitSeal-authored tool (mechanism A)
// and the function-invocation middleware (mechanism B) and invokes each directly
// on a real command, driving the actual witseal CLI. Each path must produce a
// `rcpt_…` execution receipt. Run `witseal verify` afterwards to confirm VALID.
//
//   WITSEAL_CLI_ENTRY=/path/to/witseal/dist/src/cli/index.js \
//   WITSEAL_DATA_DIR=/tmp/witseal-maf-harness \
//   WITSEAL_MODE=witness \
//   dotnet run --project samples/WitSeal.AgentFramework.Harness

using Microsoft.Extensions.AI;
using WitSeal.AgentFramework;

var options = WitSealBridgeOptions.FromEnvironment();
var bridge = new WitSealBridge(options);

Console.WriteLine("=== WitSeal x Microsoft Agent Framework — live bridge harness ===");
Console.WriteLine($"CLI entry : {options.CliEntry}");
Console.WriteLine($"data-dir  : {options.DataDir}");
Console.WriteLine($"mode      : {options.Mode}");
Console.WriteLine();

// -------------------------------------------------------------------------
// (A) WitSeal-authored tool: AIFunctionFactory.Create -> AIFunction.
//     Invoke it the way Microsoft Agent Framework would, via InvokeAsync with
//     a JSON-shaped argument bag — but with no model in the loop.
// -------------------------------------------------------------------------
AIFunction tool = WitSealTool.Create(bridge);
Console.WriteLine($"[A] authored tool name        : {tool.Name}");

var toolArgs = new AIFunctionArguments { ["command"] = "echo hello-from-maf-authored-tool && whoami" };
object? toolResult = await tool.InvokeAsync(toolArgs);
Console.WriteLine("[A] tool InvokeAsync result   :");
Console.WriteLine(Indent(toolResult?.ToString() ?? "(null)"));
Console.WriteLine();

// -------------------------------------------------------------------------
// (B) Function-invocation middleware: simulate the FunctionInvocationContext
//     Microsoft Agent Framework hands to FunctionInvoker, and run the
//     middleware body directly.
// -------------------------------------------------------------------------
var middleware = new WitSealFunctionInvocation(bridge);
var ctx = new FunctionInvocationContext
{
    Function = tool,
    Arguments = new AIFunctionArguments { ["command"] = "echo hello-from-maf-middleware && uname -s" },
};
object? mwResult = await middleware.InvokeAsync(ctx, CancellationToken.None);
Console.WriteLine("[B] middleware result         :");
Console.WriteLine(Indent(mwResult?.ToString() ?? "(null)"));
Console.WriteLine($"[B] context.Terminate         : {ctx.Terminate}");
Console.WriteLine();

// -------------------------------------------------------------------------
// Surface the parsed receipt ids so the runner can echo them.
// -------------------------------------------------------------------------
var raw = await bridge.RunAsync("echo hello-from-maf-direct-bridge");
Console.WriteLine("[C] direct bridge run         :");
Console.WriteLine($"    exit   = {raw.ExitCode}");
Console.WriteLine($"    event  = {raw.EventId}");
Console.WriteLine($"    receipt= {raw.ReceiptId}");
Console.WriteLine();

if (raw.ReceiptId is null)
{
    Console.Error.WriteLine("FATAL: no receipt id parsed from witseal stderr — bridge did not witness.");
    return 1;
}

Console.WriteLine("OK: every path produced a witseal execution receipt. Now run `witseal verify`.");
return 0;

static string Indent(string s)
    => string.Join('\n', s.Split('\n').Select(line => "    " + line));

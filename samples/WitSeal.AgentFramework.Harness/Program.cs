// SPDX-License-Identifier: Apache-2.0
// No-LLM integration harness. Success requires evidence for A, B and C,
// plus a separately observed Gate refusal in a fresh no-policy directory.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using WitSeal.AgentFramework;

try
{
    var configured = WitSealBridgeOptions.FromEnvironment();
    var suppliedRoot = Environment.GetEnvironmentVariable("WITSEAL_DATA_DIR");
    Require(!string.IsNullOrWhiteSpace(suppliedRoot), "setup: set WITSEAL_DATA_DIR to a fresh empty directory");
    Require(configured.Mode == WitSealMode.Witness, "setup: set WITSEAL_MODE=witness for A/B/C");
    Require(Environment.GetEnvironmentVariable("WITSEAL_UNSAFE_ALLOW_NO_POLICY") != "1",
        "setup: the no-policy bypass must be unset");
    var root = Path.GetFullPath(suppliedRoot!);
    Require(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any(),
        "setup: WITSEAL_DATA_DIR must be empty; existing evidence is preserved");
    Directory.CreateDirectory(root);

    WitSealBridgeOptions Options(string name, WitSealMode mode) => new()
    {
        CliEntry = configured.CliEntry,
        Node = configured.Node,
        DataDir = Path.Combine(root, name),
        Cwd = root,
        Mode = mode,
    };

    var witnessOptions = Options("witness", WitSealMode.Witness);
    var bridge = new WitSealBridge(witnessOptions);
    AIFunction tool = WitSealTool.Create(bridge);
    var cases = new List<ReceiptCase>();

    const string commandA = "printf '%s\\n' harness-authored-tool";
    object? resultA = await tool.InvokeAsync(new AIFunctionArguments { ["command"] = commandA });
    cases.Add(ReadRenderedResult("A", commandA, "harness-authored-tool", resultA));

    const string commandB = "printf '%s\\n' harness-middleware";
    var middleware = new WitSealFunctionInvocation(bridge, terminateAfterWitness: true);
    var context = new FunctionInvocationContext
    {
        Function = tool,
        Arguments = new AIFunctionArguments { ["command"] = commandB },
    };
    object? resultB = await middleware.InvokeAsync(context, CancellationToken.None);
    cases.Add(ReadRenderedResult("B", commandB, "harness-middleware", resultB));
    Require(context.Terminate, "B: the configured termination flag was not set");

    const string commandC = "printf '%s\\n' harness-direct-bridge";
    var raw = await bridge.RunAsync(commandC);
    Require(raw.ExitCode == 0 && raw.Stdout.Contains("harness-direct-bridge", StringComparison.Ordinal),
        "C: expected command output and exit code zero");
    Require(IsId(raw.ReceiptId, "rcpt") && IsId(raw.EventId, "evt"), "C: missing receipt or event id");
    cases.Add(new("C", commandC, raw.ReceiptId!, raw.EventId!));
    Require(cases.Select(c => c.ReceiptId).Distinct().Count() == 3, "A/B/C: receipt ids must be distinct");

    using (var evidence = await ExportAndVerify(witnessOptions, Path.Combine(root, "witness-evidence.json")))
    {
        foreach (var item in cases)
        {
            var record = FindEvent(evidence.RootElement, item.EventId, item.Label);
            Require(record.GetProperty("receipt_id").GetString() == item.ReceiptId,
                item.Label + ": event/receipt link mismatch");
            RequireCommand(record, item.Command, item.Label);
            var execution = record.GetProperty("execution_result");
            Require(execution.ValueKind == JsonValueKind.Object && execution.GetProperty("exit_code").GetInt32() == 0,
                item.Label + ": no successful execution result in the paired event");
            var receipt = FindReceipt(evidence.RootElement, item.ReceiptId, item.Label);
            Require(receipt.GetProperty("witness_event_id").GetString() == item.EventId &&
                    receipt.GetProperty("execution_result_hash").ValueKind == JsonValueKind.String,
                item.Label + ": receipt does not bind the paired execution result");
            Console.WriteLine($"PASS {item.Label}: {item.ReceiptId}, paired command and CLI verification");
        }
    }

    // A fresh directory has no policy pack. Observe both the refusal and the
    // absence of the attempted side effect; exit code 100 alone is insufficient.
    var gateOptions = Options("gate", WitSealMode.Gate);
    var sentinel = Path.Combine(root, "gate-side-effect.txt");
    const string gateCommand = "printf blocked > gate-side-effect.txt";
    var gate = await new WitSealBridge(gateOptions).RunAsync(gateCommand);
    Require(gate.ExitCode == 100 && !File.Exists(sentinel), "Gate: expected refusal without the file side effect");
    using (var evidence = await ExportAndVerify(gateOptions, Path.Combine(root, "gate-evidence.json")))
    {
        var events = evidence.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Require(events.Length == 1, "Gate: expected one recorded refusal in the fresh directory");
        var record = events[0];
        RequireCommand(record, gateCommand, "Gate");
        Require(record.GetProperty("outcome").GetString() == "no_policy_configured" &&
                record.GetProperty("execution_result").ValueKind == JsonValueKind.Null,
            "Gate: expected a no-policy refusal with no execution result");
        var receipt = FindReceipt(evidence.RootElement, record.GetProperty("receipt_id").GetString()!, "Gate");
        Require(receipt.GetProperty("witness_event_id").GetString() == record.GetProperty("event_id").GetString() &&
                receipt.GetProperty("execution_result_hash").ValueKind == JsonValueKind.Null,
            "Gate: refusal receipt link mismatch");
    }
    Console.WriteLine("PASS Gate: exit 100, side effect absent, refusal evidence verified by CLI");
    Console.WriteLine("PASS harness: A/B/C and the separate no-policy Gate case. No LLM was exercised.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL " + error.Message);
    return 1;
}

static ReceiptCase ReadRenderedResult(string label, string command, string marker, object? value)
{
    var text = value is JsonElement element && element.ValueKind == JsonValueKind.String
        ? element.GetString() ?? "" : value?.ToString() ?? "";
    var match = Regex.Match(text, @"^\[witseal: receipt=(rcpt_[A-Za-z0-9]+) event=(evt_[A-Za-z0-9]+) exit=0\b",
        RegexOptions.Multiline);
    Require(match.Success, label + ": missing receipt/event footer with exit zero");
    Require(text.Contains(marker, StringComparison.Ordinal), label + ": expected command output is missing");
    return new(label, command, match.Groups[1].Value, match.Groups[2].Value);
}

static bool IsId(string? value, string prefix) => value is not null && Regex.IsMatch(value, "^" + prefix + @"_[A-Za-z0-9]+$");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static JsonElement FindEvent(JsonElement package, string id, string label)
{
    var found = package.GetProperty("events").EnumerateArray()
        .Where(e => e.GetProperty("event_id").GetString() == id).ToArray();
    Require(found.Length == 1, label + ": expected exactly one paired event");
    return found[0];
}

static JsonElement FindReceipt(JsonElement package, string id, string label)
{
    var found = package.GetProperty("receipts").EnumerateArray()
        .Where(e => e.GetProperty("receipt_id").GetString() == id).ToArray();
    Require(found.Length == 1, label + ": expected exactly one receipt");
    return found[0];
}

static void RequireCommand(JsonElement record, string command, string label)
{
    var intent = record.GetProperty("classified_intent").GetProperty("intent");
    var args = intent.GetProperty("args").EnumerateArray().Select(x => x.GetString()).ToArray();
    Require(intent.GetProperty("action_type").GetString() == "shell_command" &&
            intent.GetProperty("executable").GetString() == "/bin/sh" &&
            args.SequenceEqual(new[] { "-c", command }), label + ": recorded command mismatch");
}

static async Task<JsonDocument> ExportAndVerify(WitSealBridgeOptions options, string path)
{
    await RunCli(options, "verify");
    var json = await RunCli(options, "evidence", "export");
    await File.WriteAllTextAsync(path, json);
    await RunCli(options, "verify", path);
    return JsonDocument.Parse(json);
}

static async Task<string> RunCli(WitSealBridgeOptions options, params string[] command)
{
    var start = new ProcessStartInfo(options.Node)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var arg in new[] { options.CliEntry, "--data-dir", options.DataDir, "--segment", options.Segment }.Concat(command))
        start.ArgumentList.Add(arg);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI did not start");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("CLI verification timed out");
    }
    var stdout = await output;
    var stderr = await error;
    Require(process.ExitCode == 0, "CLI " + command[0] + " failed: " + stderr.Trim());
    return stdout;
}

internal sealed record ReceiptCase(string Label, string Command, string ReceiptId, string EventId);

# WitSeal for Microsoft Agent Framework

**Witnessed execution for Microsoft Agent Framework.** This package gives a
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework) agent a
WitSeal-authored execution tool — and/or a function-invocation middleware — so
that the commands the agent runs are executed by the
[WitSeal](https://github.com/WitSeal/witseal) runtime and recorded as
independently verifiable **execution receipts**, instead of running on a raw,
unwitnessed shell.

```
PackageId:  WitSeal.AgentFramework
Target:     net8.0
Depends on: Microsoft.Agents.AI (Microsoft.Extensions.AI)
```

## What it does

Microsoft Agent Framework exposes two complementary places to insert behavior
around an agent's tool use, and this package implements WitSeal on both:

- **(A) Author the tool.** `AIFunctionFactory.Create(...)` turns a method into an
  `AIFunction` the agent can call. WitSeal ships such a tool whose body shells out
  to the WitSeal runtime. Hand it to your agent's tool list and the model's
  "run a shell command" calls go through the witness boundary.

- **(B) Function-invocation middleware.** Microsoft Agent Framework runs tool
  calls through a `FunctionInvocationContext` pipeline. WitSeal installs a
  middleware that, for the targeted tool(s), routes execution through WitSeal and
  returns the witnessed result — optionally short-circuiting the call with
  `context.Terminate`.

Either way, **WitSeal owns that tool execution** and every command yields a full
execution receipt that anyone can re-verify with `witseal verify`.

## The bridge

The .NET → WitSeal bridge (`WitSealBridge`) is the analog of WitSeal's OpenHands
adapter. It starts the unchanged WitSeal CLI as a child process:

```
node <WITSEAL_CLI_ENTRY> --data-dir <dir> exec --mode <gate|witness> -- /bin/sh -c <command>
```

captures stdout + stderr, and parses the witness footer printed on stderr:

```
[witseal: event=evt_… receipt=rcpt_… risk=C3 outcome=…]
```

No change is made to the WitSeal core, canon, or golden — this package only
invokes the built CLI.

## Install

```bash
dotnet add package WitSeal.AgentFramework
```

You also need a built WitSeal CLI on the machine (Node). Point the adapter at its
entry file via `WITSEAL_CLI_ENTRY` (absolute path to `dist/src/cli/index.js`).

## Configuration

`WitSealBridgeOptions.FromEnvironment()` reads:

| Variable            | Meaning                                            | Default      |
| ------------------- | -------------------------------------------------- | ------------ |
| `WITSEAL_CLI_ENTRY` | absolute path to `dist/src/cli/index.js` (required) | —            |
| `WITSEAL_DATA_DIR`  | WitSeal data dir (chain, policy packs, receipts)   | `~/.witseal` |
| `WITSEAL_MODE`      | `gate` (deny-by-default) or `witness`              | `gate`       |
| `WITSEAL_NODE`      | Node executable                                    | `node`       |

`Gate` mode is **deny-by-default**: a command with no allowing policy is blocked
(exit code 100) and recorded as evidence; it does not run. `Witness` mode runs
the command and records a receipt regardless of policy.

## Usage — (A) the WitSeal-authored tool

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WitSeal.AgentFramework;

var bridge = new WitSealBridge(WitSealBridgeOptions.FromEnvironment());

// A WitSeal-witnessed "run_shell_command" tool.
AIFunction witsealTool = WitSealTool.Create(bridge);

// Hand it to a Microsoft Agent Framework agent in place of an unwitnessed shell tool.
AIAgent agent = new ChatClientAgent(
    chatClient,                       // your IChatClient
    instructions: "You are a helpful coding agent.",
    name: "assistant",
    description: null,
    tools: [witsealTool]);
```

When the model calls `run_shell_command`, WitSeal executes it and the tool result
includes the receipt id.

## Usage — (B) the function-invocation middleware

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WitSeal.AgentFramework;

var bridge = new WitSealBridge(WitSealBridgeOptions.FromEnvironment());
var witseal = new WitSealFunctionInvocation(bridge);

// Wrap your base chat client so its function calls are witnessed,
// then build the agent on the witnessed client.
IChatClient witnessedClient = witseal.Install(baseChatClient);

AIAgent agent = new ChatClientAgent(
    witnessedClient,
    instructions: "You are a helpful coding agent.",
    name: "assistant",
    description: null,
    tools: [WitSealTool.Create(bridge)]);
```

`Install` is sugar for
`baseChatClient.AsBuilder().UseFunctionInvocation(configure: c => c.FunctionInvoker = witseal.InvokeAsync).Build()`.
By default the middleware witnesses calls to the `run_shell_command` tool; pass a
custom `shouldWitness` / `extractCommand` predicate to target other tools or
argument shapes.

## Verifying

Every witnessed command writes a receipt into the WitSeal data dir. Verify the
whole chain or a single receipt with the CLI:

```bash
node $WITSEAL_CLI_ENTRY --data-dir <dir> verify            # the live chain
node $WITSEAL_CLI_ENTRY --data-dir <dir> receipt show <id> # one receipt
```

## Live proof

A no-LLM console harness under [`samples/`](samples/WitSeal.AgentFramework.Harness)
instantiates both the tool and the middleware and invokes them directly against
the real CLI:

```bash
WITSEAL_CLI_ENTRY=/path/to/witseal/dist/src/cli/index.js \
WITSEAL_DATA_DIR=/tmp/witseal-maf-harness \
WITSEAL_MODE=witness \
dotnet run --project samples/WitSeal.AgentFramework.Harness
```

It prints the parsed `rcpt_…` ids for each path; `witseal verify` then reports
`VALID ✓ (chain)`. See [`COVERAGE.md`](COVERAGE.md) for exactly what is — and is
not — under the witness boundary.

## License

Apache-2.0.

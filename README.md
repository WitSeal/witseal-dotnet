# WitSeal for Microsoft Agent Framework

**Witnessed execution for Microsoft Agent Framework.** This package gives a
[Microsoft Agent Framework](https://github.com/microsoft/agent-framework) agent a
WitSeal-authored execution tool — and/or a function-invocation middleware — so
that the commands the agent runs are executed by the
[WitSeal](https://github.com/WitSeal/witseal) runtime and recorded as
verifiable **execution receipts**, instead of running on a raw,
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

Either way, the adapter routes the selected command through WitSeal. The
runtime records execution or a Gate refusal, and available evidence can be
checked with `witseal verify`. Infrastructure or storage failure can prevent
recording; invoking the adapter does not by itself establish that a receipt exists.

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
dotnet add package WitSeal.AgentFramework --version 0.1.0
```

Version 0.1.0 predates the source renderer corrections described below. Its
tool text can misreport an executed command's exit 100 as a policy refusal and
claim recording when no receipt identifier is available. Inspect the evidence
rather than treating those messages as execution facts. The corrected source
has not yet been published as a new NuGet version.

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

`Gate` mode is **deny-by-default**: a command the policy denies — or any command
when no policy pack is loaded — is blocked (exit code 100) and recorded as
evidence; it does not run. `Witness` mode runs
the command and records a receipt regardless of policy.

`WitSealRunResult.Denied` is a legacy exit-code indicator: it is true for any
CLI exit code 100. A command that actually executes and itself exits 100 also
sets it. The rendered tool result therefore reports that ambiguity and preserves
available output and evidence identifiers. Do not use `Denied` alone as proof
that execution was prevented; inspect the recorded outcome and execution result.
If no receipt identifier is available, the adapter does not claim one was recorded.

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

Every witnessed command is recorded as a witness event in the WitSeal data dir,
from which its receipt is derived. Verify the whole chain, or show a single
receipt, with the CLI:

```bash
node $WITSEAL_CLI_ENTRY --data-dir <dir> verify            # the live chain
node $WITSEAL_CLI_ENTRY --data-dir <dir> receipt show <id> # one receipt
```

## Reproducible no-LLM check

The console harness under [`samples/`](samples/WitSeal.AgentFramework.Harness)
directly invokes the authored tool (A), middleware (B), and bridge (C) against
the real CLI. Each path must return its own receipt and match the corresponding
command and event in the CLI's exported evidence. The CLI verifies both the
chain and exported package. A separate fresh directory checks a no-policy Gate
refusal: exit 100, no attempted file side effect, and a recorded refusal with
no execution result.

### Checked combination (2026-10-18)

| Component | Checked value |
|---|---|
| OS / architecture | Ubuntu 24.04, Linux x64, `/bin/sh` |
| .NET SDK | 10.0.402, selected by `global.json` |
| Adapter target | `net8.0` |
| Harness target / runtime | `net10.0` / 10.0.13 |
| Unit-test target / runtime | `net8.0` / 8.0.32 |
| Node | 24.19.0 |
| CLI source | 0.4.2 at `c80600f91c63d1dcee72d510f035133a2cf2f85e`, installed with its npm lock |
| Adapter source | This checkout, tested through the harness's `ProjectReference` |
| Published package | NuGet `WitSeal.AgentFramework` 0.1.0, also exercised by a separate package consumer using the same harness code |
| Resolved framework | `Microsoft.Agents.AI` 1.9.0; `Microsoft.Extensions.AI` 10.5.1 |

The source harness and the published NuGet package are different inputs. A
source test alone would not establish the package-consumer result. The added
harness checks and documentation do not imply a new package publication.
The source also corrects misleading tool-result text for exit code 100 and
missing receipt identifiers. Published NuGet 0.1.0 still has the earlier renderer;
its A/B/C and Gate check does not establish correct wording for those error cases.
This combination does not establish Windows support or compatibility with all
CLI, SDK, or Agent Framework versions.

From the adapter checkout, with the SDK and Node versions above installed:

```bash
git clone https://github.com/WitSeal/witseal ../witseal-cli
git -C ../witseal-cli checkout --detach c80600f91c63d1dcee72d510f035133a2cf2f85e
(cd ../witseal-cli && npm ci --ignore-scripts --no-audit --no-fund && npm run build)

dotnet restore samples/WitSeal.AgentFramework.Harness/WitSeal.AgentFramework.Harness.csproj --locked-mode
dotnet build samples/WitSeal.AgentFramework.Harness/WitSeal.AgentFramework.Harness.csproj -c Release --no-restore

witseal_cli_entry="$(cd ../witseal-cli && pwd)/dist/src/cli/index.js"
witseal_test_dir="$(mktemp -d)"
WITSEAL_CLI_ENTRY="$witseal_cli_entry" WITSEAL_DATA_DIR="$witseal_test_dir" \
WITSEAL_MODE=witness \
dotnet samples/WitSeal.AgentFramework.Harness/bin/Release/net10.0/WitSeal.AgentFramework.Harness.dll

python3 scripts/test_harness_failures.py \
  --harness samples/WitSeal.AgentFramework.Harness/bin/Release/net10.0/WitSeal.AgentFramework.Harness.dll \
  --cli "$witseal_cli_entry"
```

Expected: separate PASS lines for A, B, C, Gate, and both negative controls.
The controls withhold A or B's receipt token from the bridge and require an
overall failure; they are tests of the harness's failure detection.

The harness requires an explicitly supplied empty data directory and refuses
to overwrite existing evidence. It creates `witness/`, `gate/`, and two evidence
JSON files beneath that directory. Do not enable
`WITSEAL_UNSAFE_ALLOW_NO_POLICY` for this check. The exported evidence includes
local execution context; inspect it before sharing it.

For a package-consumer repeat, create a separate `net10.0` console project,
copy this harness's `Program.cs`, and add
`WitSeal.AgentFramework` **0.1.0** as a `PackageReference` instead of the harness's
`ProjectReference`. Keep the SDK, Node and CLI inputs above. Retain that
consumer's own `packages.lock.json` and use `dotnet restore --locked-mode` for
subsequent repeats. The repository CI exercises the source harness; it does not
claim to rerun the NuGet consumer on every push.

No model or agent loop participates. See [COVERAGE.md](COVERAGE.md) for the
boundary of the result and [CONTRIBUTING.md](CONTRIBUTING.md) for development
and lock-update instructions.

## License

Apache-2.0.

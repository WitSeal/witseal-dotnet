# Coverage — what WitSeal witnesses in a Microsoft Agent Framework agent

This document states precisely what the WitSeal Microsoft Agent Framework adapter
puts under the witnessed-execution boundary, and what it does not. The goal is an
honest scope statement, not a maximal claim.

## In scope (witnessed → execution receipt)

- **Command execution routed through the WitSeal-authored tool.** When the agent
  calls the WitSeal `run_shell_command` tool (mechanism A), the command is
  executed by the WitSeal runtime (classify → policy → mediate → witness →
  receipt) via the bridge, and a full execution receipt is written. WitSeal owns
  that execution (L3).

- **Command execution captured by the function-invocation middleware**
  (mechanism B). For tool calls the configured predicate selects (by default,
  the `run_shell_command` tool), the middleware routes execution through WitSeal
  and returns the witnessed result instead of letting the call run on a raw
  shell. It can short-circuit the call with `context.Terminate`.

- **Deny-by-default in Gate mode.** In `gate` mode a command with no allowing
  policy is blocked (exit code 100), does not run, and is recorded as evidence.

Each in-scope execution produces a receipt that anyone can independently
re-verify with `witseal verify` against the WitSeal data directory — no trust in
this adapter, the host, or the model is required to check what ran.

## Out of scope (NOT witnessed by this adapter)

- **Microsoft Agent Framework internals.** The agent loop, planner, memory,
  chat-history management, and orchestration are not under the boundary.

- **The LLM / model traffic.** Prompts, completions, token usage, and the model's
  reasoning are not witnessed. WitSeal witnesses *execution*, not *generation*.

- **All host traffic / every side effect.** Only what is executed through the
  WitSeal tool or the witnessed code path is covered. Tools other than the ones
  the predicate selects, native network calls the host makes directly, file I/O
  performed outside a witnessed command, and any subprocess the host spawns on
  its own are **not** witnessed. If an agent is given both the WitSeal tool and an
  unwitnessed shell tool, only the WitSeal one is covered — to get full execution
  coverage, route every execution-capable tool through WitSeal (or remove the
  alternatives).

- **Interactive / streaming stdin and control sequences.** The bridge runs each
  command as a discrete `/bin/sh -c` execution; it does not model a persistent
  interactive terminal session.

## Boundary summary

The unit of witnessing is **one command executed through WitSeal**, producing one
verifiable execution receipt. The adapter's job is to make the agent's execution
flow through that boundary; it makes no claim about the parts of the system that
remain outside it.

## No change to WitSeal canon

This adapter invokes the unchanged WitSeal CLI as a child process. It does not
modify the WitSeal core, its wire format, or its canonical artifacts; the receipts
it produces are ordinary WitSeal receipts.

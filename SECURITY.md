# Security Policy

## Reporting a Vulnerability

WitSeal is a trust-runtime product. Vulnerabilities in WitSeal undermine the trust property the product exists to provide. We take reports seriously. This repository is the WitSeal adapter for the Microsoft Agent Framework (the `WitSeal.AgentFramework` NuGet package); the same disclosure policy applies here as to the core runtime.

### Report channel

Use the private GitHub Security Advisory form:

<https://github.com/WitSeal/witseal-dotnet/security/advisories/new>

Include:

- A description of the vulnerability
- Steps to reproduce
- Affected version(s)
- Your name and contact info (for credit, optional)

A dedicated security email address and PGP key are not currently published for
Phase 1. Use the private advisory form for sensitive reports.

**Do not file a public GitHub issue for security vulnerabilities.**

### Response timeline

| Step | Target |
|---|---|
| Acknowledgement | within 72 hours |
| Initial assessment | within 7 days |
| Fix or mitigation | within 90 days for high/critical; within 180 days for medium/low |
| Public disclosure | 90 days from initial report (coordinated) or upon fix release, whichever is sooner |

We follow a **90-day coordinated disclosure window**. After that window, the reporter is free to disclose publicly regardless of fix status. We will request an extension only with strong justification (e.g., complex fix requiring coordinated upstream changes).

### Scope

In scope:
- This adapter package (`WitSeal.AgentFramework`): the .NET → WitSeal bridge, the WitSeal-authored tool, and the function-invocation middleware
- The way this adapter constructs the WitSeal CLI invocation and parses its output
- The witnessed-execution boundary as implemented by this adapter

Out of scope (in Phase 1):
- The WitSeal CLI runtime and its core libraries — report those against the core repository (`WitSeal/witseal`)
- Schema definitions, hash chain construction/verification, and policy evaluation logic — these live in the core runtime
- Vulnerabilities in dependencies (report to the dependency upstream; we'll coordinate)
- Issues in agent frameworks WitSeal integrates with (Microsoft Agent Framework, Claude Code, OpenCode, etc.)
- Social engineering against contributors
- DDoS / availability of any hosted service (no hosted service exists in Phase 1)

### Safe harbor

We will not pursue legal action against researchers who:

- Make a good-faith effort to avoid privacy violations and disruption to users
- Report vulnerabilities promptly
- Do not exploit vulnerabilities beyond what is necessary to demonstrate the issue
- Do not access, modify, or destroy data belonging to others

This is the standard `disclose.io` safe-harbor framing.

---

## Verifying releases

This package is published to [nuget.org](https://www.nuget.org/packages/WitSeal.AgentFramework) via **Trusted Publishing (OIDC)** from GitHub Actions — no long-lived API key is stored, and the publishing identity is the release workflow itself. Each release is tagged `vX.Y.Z` in this repository, and the published package version matches that tag.

To check what you are consuming:

- Confirm the package version on nuget.org matches a `vX.Y.Z` tag in this repository.
- The package is `WitSeal.AgentFramework`, authored by **WitSeal Maintainers**, licensed Apache-2.0.
- The adapter invokes the unchanged WitSeal CLI as a child process; it does not bundle the runtime. Verify the WitSeal CLI itself per the core repository's `SECURITY.md` (Sigstore Cosign + Rekor).

The receipts this adapter produces are ordinary WitSeal receipts; verify them with `witseal verify` against the WitSeal data directory.

---

## Cryptographic primitives

This adapter performs **no cryptography of its own**. It shells out to the WitSeal CLI, which owns event/receipt hashing (SHA-256), canonicalization (RFC 8785 / JCS), and the hash chain. The cryptographic constructions and their sources are documented in the core runtime's `SECURITY.md`. There are no private keys in this adapter and none in the Phase 1 local runtime — there are no keys to leak.

---

## Threat model

This adapter's job is to route an agent's command execution through the WitSeal witness boundary so each command yields an independently verifiable execution receipt. It is a thin process-launch-and-parse layer; the trust properties are provided by the WitSeal core, whose threat model lives in the core repository at `docs/threat-model.md`.

**What this adapter contributes to the boundary:**
- Command execution routed through the WitSeal-authored tool or captured by the function-invocation middleware is executed by the WitSeal runtime and recorded as a receipt.
- In `gate` mode, a command with no allowing policy is blocked (deny-by-default, exit code 100), does not run, and is recorded as evidence.

**What this adapter does NOT protect (see `COVERAGE.md` for the full scope statement):**
- Microsoft Agent Framework internals (agent loop, planner, memory, orchestration) — not under the boundary.
- The LLM / model traffic — prompts, completions, and the model's reasoning are not witnessed. WitSeal witnesses *execution*, not *generation*.
- Tool calls the configured predicate does not select, native network calls the host makes directly, file I/O outside a witnessed command, and any subprocess the host spawns on its own. If an agent is given both the WitSeal tool and an unwitnessed shell tool, only the WitSeal one is covered.
- Everything the WitSeal core threat model already excludes (e.g., a malicious producer rewriting the entire chain, subprocess escapes via `LD_PRELOAD`, prompt injection / model-level jailbreak).

Honest documentation of these limitations is itself a security posture.

---

## Disclosed vulnerabilities

No disclosures yet (this package is pre-1.0). Disclosed vulnerabilities, fixes, and reporters will be listed here as they are resolved.

---

## Acknowledgements

We will publicly credit reporters who request credit, with a link to their profile or homepage of choice. Reporters who prefer anonymity will be credited as "anonymous researcher" or omitted entirely per their preference.

---

## Bug bounty

No formal bug bounty in Phase 1. Reporters are credited publicly (with consent) and may be invited as design partners. A formal bounty program may be introduced when WitSeal has commercial revenue (post-Phase 5).

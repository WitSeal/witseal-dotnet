# Contributing

This repository contains the .NET adapter and a no-LLM integration harness.
Use a focused change and explain which public behavior, integration path, or
documentation it affects. Wire-format and runtime changes belong in the
corresponding WitSeal project and require coordination.

## Development checks

Use the SDK selected by `global.json`. The unit tests execute on .NET 8; the
integration harness executes on .NET 10. See the exact checked versions and CLI
setup in [README.md](README.md#reproducible-no-llm-check).

```bash
dotnet restore tests/WitSeal.AgentFramework.Tests/WitSeal.AgentFramework.Tests.csproj --locked-mode
dotnet test tests/WitSeal.AgentFramework.Tests/WitSeal.AgentFramework.Tests.csproj -c Release --no-restore
```

For changes affecting tool invocation, the bridge, or the harness, also run the
README's positive harness and negative controls. A receipt on the direct bridge
path does not substitute for a receipt on the tool or middleware paths.

The public-sanitary check in CI applies to new tracked files as well as edits.
Keep generated `bin/` and `obj/` output, local execution evidence, credentials,
and personal paths out of contributions. Report vulnerabilities through
[SECURITY.md](SECURITY.md), not a public issue.

## SDK and dependency updates

The executable harness and unit-test project have separate committed
`packages.lock.json` files. Ordinary CI uses locked restore; a dependency update
should deliberately regenerate the affected lock, show the diff, and rerun the
relevant checks. Do not assume a library lock constrains dependency resolution
in applications that consume the NuGet package; those consumers own their graph.

When updating the SDK, review `global.json`, the versions in `ci.yml` and
`release.yml`, the required runtimes, the locks, and the README's checked
combination together. The release workflow must install the exact SDK selected
by `global.json` even though it is not part of routine development validation.
Keep existing required check names stable. The new harness check is a separate
integration result; changes to repository protection rules are not implicit.

The release workflow publishes to NuGet and GitHub. It is not a development-test
command; normal validation does not require a tag or release workflow dispatch.

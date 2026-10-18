#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
"""Negative integration controls: withhold A or B's receipt from the bridge.

The real CLI still runs in a fresh temporary directory. A narrow test shim
removes one receipt token from its stderr, so a healthy C cannot hide a missing
A or B receipt. These controls test the harness, not a new runtime capability.
"""

import argparse
import os
from pathlib import Path
import subprocess
import tempfile


SHIM = r"""
import {spawnSync} from 'node:child_process';
const args = process.argv.slice(2);
const r = spawnSync(process.execPath, [process.env.WITSEAL_REAL_CLI_ENTRY, ...args],
  {encoding: 'utf8', env: process.env});
if (r.error) throw r.error;
let stderr = r.stderr ?? '';
if (args.includes('exec') && args.at(-1).includes(process.env.HARNESS_HIDE_MARKER)) {
  stderr = stderr.replace(/receipt=rcpt_[A-Za-z0-9]+/g, 'receipt=(withheld)');
}
process.stdout.write(r.stdout ?? '');
process.stderr.write(stderr);
process.exit(r.status ?? 1);
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--harness', type=Path, required=True, help='built harness DLL')
    parser.add_argument('--cli', type=Path, required=True, help='real built CLI entry')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    harness = args.harness.resolve(strict=True)
    cli = args.cli.resolve(strict=True)
    for label, marker in [('A', 'harness-authored-tool'), ('B', 'harness-middleware')]:
        with tempfile.TemporaryDirectory(prefix='witseal-harness-control-') as work:
            root = Path(work)
            shim = root / 'withhold-receipt.mjs'
            shim.write_text(SHIM, encoding='utf-8')
            env = {
                **os.environ,
                'WITSEAL_REAL_CLI_ENTRY': str(cli),
                'WITSEAL_CLI_ENTRY': str(shim),
                'WITSEAL_DATA_DIR': str(root / 'evidence'),
                'WITSEAL_MODE': 'witness',
                'HARNESS_HIDE_MARKER': marker,
            }
            env.pop('WITSEAL_UNSAFE_ALLOW_NO_POLICY', None)
            result = subprocess.run([args.dotnet, str(harness)], env=env,
                                    capture_output=True, text=True, timeout=90)
            if (result.returncode == 0 or f'FAIL {label}: missing receipt/event footer' not in result.stderr
                    or 'PASS harness:' in result.stdout):
                raise SystemExit(f'FAIL negative control {label}: unexpected harness result\n'
                                 + result.stdout + result.stderr)
            print(f'PASS negative control {label}: missing receipt prevents overall success')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

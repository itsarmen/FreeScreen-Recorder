#!/usr/bin/env python3
import argparse
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT / "FreeScreenRecorder.csproj"
DOTNET = Path(r"C:\Program Files\dotnet\dotnet.exe")


def find_dotnet() -> str:
    if DOTNET.is_file():
        return str(DOTNET)
    return "dotnet"


def main() -> int:
    parser = argparse.ArgumentParser(description="Build and launch FreeScreen Recorder.")
    parser.add_argument("-c", "--configuration", default="Debug", choices=["Debug", "Release"])
    parser.add_argument("-p", "--platform", default="x64", choices=["x64", "x86", "ARM64"])
    parser.add_argument("--no-build", action="store_true", help="Launch the existing build.")
    parser.add_argument("--wait", action="store_true", help="Wait for the app to exit.")
    args = parser.parse_args()

    target_framework = "net8.0-windows10.0.26100.0"
    runtime = f"win-{args.platform.lower()}"
    output_dir = ROOT / "bin" / args.platform / args.configuration / target_framework / runtime
    executable = output_dir / "FreeScreenRecorder.exe"

    if not args.no_build:
        build = subprocess.run(
            [
                find_dotnet(),
                "build",
                str(PROJECT),
                "-c",
                args.configuration,
                f"-p:Platform={args.platform}",
            ],
            cwd=ROOT,
        )
        if build.returncode != 0:
            return build.returncode

    if not executable.is_file():
        print(f"Executable not found: {executable}", file=sys.stderr)
        return 1

    process = subprocess.Popen([str(executable)], cwd=output_dir)
    if args.wait:
        return process.wait()

    print(f"Launched FreeScreen Recorder (pid {process.pid})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Exercise a built CLI against real pipes whose reader remains open but never drains."""

import argparse
import array
import fcntl
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import termios
import time


DEADLINE_SECONDS = 8
GIT_TIMEOUT_SECONDS = 3
DIAGNOSTIC_LINES = 512
DIAGNOSTIC_WIDTH = 4096


def wait_for(predicate, process):
    deadline = time.monotonic() + DEADLINE_SECONDS
    while time.monotonic() < deadline:
        if predicate():
            return
        if process.poll() is not None:
            raise AssertionError(f"CLI exited prematurely with {process.returncode}")
        time.sleep(0.01)
    raise AssertionError("Fixture did not reach the required state")


def assert_dead(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return
    raise AssertionError(f"Fixture process {pid} survived CLI termination")


def exercise(binary, cancel=False, blocked_stdout=False):
    with tempfile.TemporaryDirectory(prefix="clone-stalled-output-", dir="/private/tmp") as temporary:
        directory = Path(temporary)
        root = directory / "projects"
        root.mkdir(mode=0o700)
        script = directory / "git"
        if blocked_stdout:
            body = "mkdir -p repository/.git\ntouch repository/.git/HEAD repository/.git/config\n"
        else:
            body = (
                f"echo $$ > '{directory}/git.pid'\n"
                "mkdir repository\n"
                f"/usr/bin/awk 'BEGIN {{for(i=0;i<{DIAGNOSTIC_LINES};i++) {{"
                f"for(j=0;j<{DIAGNOSTIC_WIDTH};j++) printf \"x\"; print \"\"}}}}' >&2\n"
                "/bin/sleep 30 &\n"
                f"echo $! > '{directory}/child.pid'\n"
                f"touch '{directory}/emitted'\nwait\n"
            )
        script.write_text("#!/bin/sh\nset -eu\n" + body)
        script.chmod(0o700)
        reader, writer = os.pipe()
        process = None
        try:
            # Measure capacity before launch; optionally keep stdout prefilled.
            os.set_blocking(writer, False)
            capacity = 0
            while True:
                try:
                    capacity += os.write(writer, b"x" * DIAGNOSTIC_WIDTH)
                except BlockingIOError:
                    break
            os.set_blocking(writer, True)
            if not blocked_stdout:
                remaining = capacity
                while remaining:
                    remaining -= len(os.read(reader, remaining))
            process = subprocess.Popen(
                [str(binary), "--root", str(root), "--git", str(script),
                 "--config-dir", str(directory / "config"), "--timeout", str(GIT_TIMEOUT_SECONDS),
                 "https://example.com/team/repo"],
                stdout=writer if blocked_stdout else subprocess.PIPE,
                stderr=subprocess.DEVNULL if blocked_stdout else writer,
                start_new_session=True,
            )
            os.close(writer)
            writer = None
            if not blocked_stdout:
                wait_for(lambda: (directory / "emitted").exists(), process)
                def pipe_is_stalled():
                    pending = array.array("i", [0])
                    fcntl.ioctl(reader, termios.FIONREAD, pending, True)
                    return pending[0] > capacity - (DIAGNOSTIC_WIDTH + 1)

                wait_for(pipe_is_stalled, process)
                if cancel:
                    process.send_signal(signal.SIGINT)
            code = process.wait(timeout=DEADLINE_SECONDS)
            expected = 0 if blocked_stdout else 130 if cancel else 124
            assert code == expected, f"Expected exit {expected}, received {code}"
            assert not list(root.glob(".clone-staging-*")), "Staging survived CLI termination"
            destination = root / "example.com/team/repo"
            assert destination.exists() == blocked_stdout, "Unexpected publication state"
            if not blocked_stdout:
                for name in ("git.pid", "child.pid"):
                    assert_dead(int((directory / name).read_text()))
                assert process.stdout.read() == b"", "Failure published a destination on stdout"
            # Reader is intentionally still open here; assertions precede sink release.
        finally:
            if writer is not None:
                os.close(writer)
            os.close(reader)
            if process is not None:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=DEADLINE_SECONDS)
                if process.stdout is not None:
                    process.stdout.close()
                for name in ("git.pid", "child.pid"):
                    if (directory / name).exists():
                        try:
                            os.kill(int((directory / name).read_text()), signal.SIGKILL)
                        except ProcessLookupError:
                            pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    binary = parser.parse_args().binary.resolve(strict=True)
    for name, options in (
        ("timeout with stalled stderr", {}),
        ("SIGINT with stalled stderr", {"cancel": True}),
        ("success with prefilled stdout", {"blocked_stdout": True}),
    ):
        exercise(binary, **options)
        print(f"PASS: {name}")


if __name__ == "__main__":
    main()

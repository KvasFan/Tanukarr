#!/usr/bin/env python3
"""Run persisted collection migration/range regressions on owned PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET


POSTGRES_IMAGE = "postgres@sha256:b0f9560a2de083e2cc7382e75f808c7381a32852a7ec49117deedb300e552b24"
ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default=POSTGRES_IMAGE)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    if args.report.exists():
        raise ValueError("Use a fresh report path")
    secret = secrets.token_hex(32)
    container = None
    error = None
    output = ""
    counts = {}
    metadata = {}
    cleanup_complete = False

    def redact(value):
        return str(value).replace(secret, "[REDACTED]")

    def docker(*arguments):
        result = subprocess.run([args.docker, *arguments], capture_output=True, text=True, timeout=180)
        if result.returncode:
            raise RuntimeError(redact(result.stderr.strip()))
        return result.stdout.strip()

    try:
        if "@sha256:" not in args.image:
            raise ValueError("The disposable PostgreSQL image must be digest-pinned")
        docker("pull", args.image)
        with tempfile.TemporaryDirectory(prefix="tanukarr-postgres-collections-") as temporary:
            temporary = Path(temporary)
            environment_file = temporary / "postgres.env"
            environment_file.write_text("POSTGRES_USER=tanukarr_fixture\nPOSTGRES_PASSWORD=" + secret
                                        + "\nPOSTGRES_INITDB_ARGS=--auth-host=scram-sha-256\n", encoding="utf-8")
            if os.name == "posix":
                environment_file.chmod(0o600)
            container = docker("run", "--detach", "--name", "tanukarr-postgres-collections-" + secrets.token_hex(6),
                               "--label", "tanukarr.postgres-collections-test=true", "--security-opt", "no-new-privileges:true",
                               "--publish", "127.0.0.1::5432", "--env-file", str(environment_file),
                               "--tmpfs", "/var/lib/postgresql/data:rw,noexec,nosuid,size=512m", args.image)
            detail = json.loads(docker("inspect", container))[0]
            binding = detail["NetworkSettings"]["Ports"]["5432/tcp"]
            if len(binding) != 1 or binding[0]["HostIp"] != "127.0.0.1":
                raise ValueError("PostgreSQL is not restricted to loopback")
            port = binding[0]["HostPort"]
            # The official image first runs a temporary server for initialization and
            # restarts it; pg_isready can answer during that window, so require an
            # actual query to succeed before trusting the server.
            deadline = time.monotonic() + 90
            while True:
                ready = subprocess.run([args.docker, "exec", container, "pg_isready", "-U", "tanukarr_fixture"],
                                       capture_output=True, text=True, timeout=10)
                if ready.returncode == 0:
                    probe = subprocess.run([args.docker, "exec", container, "psql", "-U", "tanukarr_fixture", "-d", "postgres",
                                            "-Atc", "SELECT version();"], capture_output=True, text=True, timeout=10)
                    if probe.returncode == 0 and probe.stdout.strip():
                        version = probe.stdout.strip()
                        break
                if time.monotonic() >= deadline:
                    raise TimeoutError("Owned PostgreSQL did not initialize")
                time.sleep(0.5)
            metadata.update(imageId=detail["Image"], serverVersion=version, loopbackOnly=True, tmpfsOnly=True)
            environment = {key: value for key, value in os.environ.items() if not key.lower().startswith("sonarr__postgres__")}
            environment.update(Sonarr__Postgres__Host="127.0.0.1", Sonarr__Postgres__Port=port,
                               Sonarr__Postgres__User="tanukarr_fixture", Sonarr__Postgres__Password=secret)
            result = subprocess.run(
                [args.dotnet, "test", "src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj", "-c", "Debug", "--no-build", "--no-restore",
                 "--filter", "FullyQualifiedName~AddGroupedSeriesPersistenceFixture|FullyQualifiedName~MovieMetadataMigrationFixture", "--results-directory", str(temporary / "results"),
                 "--logger", "trx;LogFileName=postgres-collections.trx", "--logger", "console;verbosity=minimal"],
                cwd=ROOT, env=environment, capture_output=True, text=True, timeout=900)
            output = redact(result.stdout + "\n" + result.stderr)
            reports = list((temporary / "results").glob("*.trx"))
            if len(reports) == 1:
                tree = ET.parse(reports[0])
                counters = tree.find("{*}ResultSummary/{*}Counters")
                counts = {key: int(value) for key, value in counters.attrib.items()}
            if result.returncode or counts.get("passed", 0) == 0 or counts.get("failed", 0):
                raise RuntimeError("Persisted PostgreSQL collection tests failed; see the sanitized log")
    except (Exception, KeyboardInterrupt) as caught:
        error = redact(str(caught) or type(caught).__name__)
    finally:
        if container:
            try:
                docker("rm", "--force", "--volumes", container)
                cleanup_complete = True
            except Exception as caught:
                error = error or redact(str(caught))
        else:
            cleanup_complete = True
        args.report.parent.mkdir(parents=True, exist_ok=True)
        report = {"createdUtc": datetime.now(timezone.utc).isoformat(), "sourceRevision": os.environ.get("GITHUB_SHA"),
                  "postgresImage": args.image, **metadata, "passed": error is None, "error": error,
                  "testCounts": counts, "cleanupComplete": cleanup_complete,
                  "scope": {"actualPostgreSQL": True, "ownedGeneratedDatabase": True, "fullCoreSuite": False,
                            "ownerConfigurationAccessed": False}}
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        args.report.with_suffix(".log").write_text(output, encoding="utf-8")
    print(json.dumps({"passed": error is None, "testCounts": counts, "cleanupComplete": cleanup_complete, "error": error}))
    return 1 if error else 0


if __name__ == "__main__":
    sys.exit(main())

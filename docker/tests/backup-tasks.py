#!/usr/bin/env python3
"""Check real backup commands and task timestamps using a disposable Forms instance."""

import argparse
from datetime import datetime, timedelta, timezone
import importlib.util
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import time
import zipfile

from smoke import require


TESTS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("owned_backup_security", TESTS / "functional-security.py")
functional = importlib.util.module_from_spec(spec)
spec.loader.exec_module(functional)


def utc_now():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


class BackupTasks(functional.FunctionalSecurity):
    def __init__(self, args):
        super().__init__(args)
        self.image_labels = {}
        self.evidence = []
        self.browser_report = None

    def task(self, version="v5"):
        tasks = self.json_api(f"/api/{version}/system/task")
        backup = [task for task in tasks if task.get("taskName") == "Backup"]
        require(len(backup) == 1, "Expected exactly one Backup scheduled task")
        return backup[0]

    def wait_task(self, before, changed):
        deadline = time.monotonic() + 10
        while True:
            task = self.task()
            if (task["lastExecution"] != before["lastExecution"]) == changed:
                return task
            require(time.monotonic() < deadline, "Successful Backup did not update Last Execution")
            time.sleep(0.2)

    def wait_command(self, command):
        deadline = time.monotonic() + 90
        while True:
            command = self.json_api(f"/api/v5/command/{command['id']}")
            if command["status"] in ("completed", "failed", "aborted", "cancelled"):
                return command
            require(time.monotonic() < deadline, "Owned backup command did not finish")
            time.sleep(0.2)

    def compare_apis(self, expected):
        for version in ("v3", "v5"):
            actual = self.task(version)
            for field in ("id", "lastExecution", "lastStartTime", "nextExecution", "interval"):
                require(actual[field] == expected[field], f"{version} Backup task differs in {field}")
            last_execution = datetime.fromisoformat(actual["lastExecution"].replace("Z", "+00:00"))
            next_execution = datetime.fromisoformat(actual["nextExecution"].replace("Z", "+00:00"))
            require(next_execution == last_execution + timedelta(minutes=actual["interval"]),
                    "Next backup execution is not based on the latest successful backup")

    def seed_backup_time(self, overdue=False):
        self.docker("stop", "--time", "20", self.container)
        self.prepare_config_for_host_sqlite()
        stamp = datetime.now(timezone.utc) - timedelta(days=8) if overdue else datetime.now(timezone.utc) - timedelta(hours=2)
        value = stamp.strftime("%Y-%m-%d %H:%M:%S.%f")
        database = self.root / "config" / "sonarr.db"
        with sqlite3.connect(database) as connection:
            require(connection.execute("PRAGMA integrity_check").fetchone() == ("ok",), "Owned seed database is corrupt")
            cursor = connection.execute(
                'UPDATE "ScheduledTasks" SET "LastExecution" = ?, "LastStartTime" = ? WHERE "TypeName" = ?',
                (value, value, "NzbDrone.Core.Backup.BackupCommand"))
            require(cursor.rowcount == 1, "Owned seed did not find exactly one backup task")
        self.docker("start", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.verify_app_ownership_after_host_seed()
        return self.task()

    def verify_archive(self, archive_type, previous_names):
        backups = self.json_api("/api/v5/system/backup")
        new = [item for item in backups if item["name"] not in previous_names and item["type"] == archive_type]
        require(len(new) == 1, "Expected one newly created " + archive_type + " backup archive")
        item = new[0]
        archive = self.root / "config" / "Backups" / archive_type / item["name"]
        require(archive.is_file() and archive.stat().st_size == item["size"], "Backup API does not match its actual ZIP")
        with zipfile.ZipFile(archive) as zipped:
            require(zipped.testzip() is None, "Actual backup ZIP failed CRC verification")
            names = {Path(name).name.lower() for name in zipped.namelist()}
            require({"sonarr.db", "config.xml"}.issubset(names), "Backup ZIP is missing the database or configuration")
            payload = zipped.read(next(name for name in zipped.namelist() if Path(name).name.lower() == "sonarr.db"))
        connection = sqlite3.connect(":memory:")
        try:
            connection.deserialize(payload)
            require(connection.execute("PRAGMA integrity_check").fetchone() == ("ok",), "Backed-up SQLite database is corrupt")
        finally:
            connection.close()
        return {"type": archive_type, "size": item["size"], "zipCrcValid": True,
                "databaseIntegrity": "ok", "configIncluded": True}

    def manual_backup(self, version):
        before = self.task()
        prior_names = {item["name"] for item in self.json_api("/api/v5/system/backup")}
        command = self.wait_command(self.json_api(f"/api/{version}/command", "POST", {"name": "Backup"}))
        after = self.task()
        evidence = {"case": version + " manual success", "commandStatus": command["status"],
                    "commandTrigger": command["trigger"], "before": before, "after": after}
        self.evidence.append(evidence)
        require(command["status"] == "completed" and command["trigger"] == "manual", "Manual Backup command did not complete")
        evidence["archive"] = self.verify_archive("manual", prior_names)
        after = self.wait_task(before, changed=True)
        evidence["after"] = after
        require(after["lastStartTime"] == command["started"], "Backup task start time does not match the real command")
        self.compare_apis(after)
        self.check(version + " successful manual ZIP backup updates V3/V5 Last Execution and next cadence")

    def failure_and_recovery(self):
        before = self.task()
        backups = self.json_api("/api/v5/system/backup")
        settings = self.json_api("/api/v5/settings/general")
        original_folder = settings["backupFolder"]
        self.docker("exec", "--user", "0:0", self.container, "mkdir", "/config/BackupDenied")
        self.docker("exec", "--user", "0:0", self.container, "chown", "0:0", "/config/BackupDenied")
        self.docker("exec", "--user", "0:0", self.container, "chmod", "0555", "/config/BackupDenied")
        settings["backupFolder"] = "/config/BackupDenied"
        self.json_api("/api/v5/settings/general/1", "PUT", settings)
        try:
            command = self.wait_command(self.json_api("/api/v5/command", "POST", {"name": "Backup"}))
            after = self.task()
            self.evidence.append({"case": "deliberate owned non-writable folder", "commandStatus": command["status"],
                                  "before": before, "after": after, "exceptionRetained": bool(command.get("exception"))})
            require(command["status"] == "failed" and command.get("exception"), "Owned unwritable folder did not preserve a failed command")
            for field in ("lastExecution", "lastStartTime", "nextExecution"):
                require(after[field] == before[field], "Failed Backup replaced successful task " + field)
            self.compare_apis(after)
            require(not list((self.root / "config" / "BackupDenied").glob("**/*.zip")), "Failed backup unexpectedly created an archive")
            self.check("failed backup keeps its command error and preserves last success and schedule in both APIs")
        finally:
            settings = self.json_api("/api/v5/settings/general")
            settings["backupFolder"] = original_folder
            self.json_api("/api/v5/settings/general/1", "PUT", settings)
        require(self.json_api("/api/v5/system/backup") == backups, "Failed backup modified existing archives")
        time.sleep(1.1)  # Archive names have one-second precision.
        self.manual_backup("v5")
        self.check("restoring only the owned backup path allows a fresh successful backup")

    def scheduled_backup(self):
        prior_names = {item["name"] for item in self.json_api("/api/v5/system/backup")}
        self.seed_backup_time(overdue=True)
        deadline = time.monotonic() + 70
        while True:
            scheduled = [command for command in self.json_api("/api/v5/command")
                         if command.get("name") == "Backup" and command.get("trigger") == "scheduled"]
            if scheduled:
                command = self.wait_command(max(scheduled, key=lambda item: item["id"]))
                break
            require(time.monotonic() < deadline, "Scheduler did not run the overdue owned backup")
            time.sleep(1)
        require(command["status"] == "completed", "Scheduled backup did not complete")
        # Command completion is visible before CommandExecutedEvent updates the
        # scheduled task. Wait for the same observable state as the manual case.
        deadline = time.monotonic() + 10
        while True:
            after = self.task()
            if after["lastStartTime"] == command["started"]:
                break
            require(time.monotonic() < deadline, "Scheduled Backup start time differs from its command")
            time.sleep(0.2)
        self.compare_apis(after)
        archive = self.verify_archive("scheduled", prior_names)
        self.evidence.append({"case": "actual overdue scheduler", "commandStatus": command["status"],
                              "after": after, "archive": archive})
        require(after["lastStartTime"] == command["started"], "Scheduled Backup start time differs from its command")
        self.check("real scheduler creates a scheduled ZIP and records task timing without changing archive category")

    def persist(self):
        before = self.task()
        self.docker("restart", "--time", "20", self.container)
        self.refresh_binding()
        self.wait_ready()
        self.compare_apis(before)
        self.check("successful backup timestamp and next cadence persist through a real container restart")

    def browser(self):
        self.seed_backup_time()
        artifact = self.args.report.parent.resolve()
        def native_path(value):
            if self.args.browser_node.lower().endswith(".exe") and sys.platform != "win32":
                return subprocess.check_output(["wslpath", "-w", str(value)], text=True).strip()
            return str(value)
        payload = {"baseUrl": self.base_url, "username": self.username, "password": self.password,
                   "apiKey": self.api_key, "ownedFixture": True, "imageId": self.image_id,
                   "playwrightModule": self.args.playwright_module, "artifactDirectory": native_path(artifact)}
        result = subprocess.run([self.args.browser_node, native_path(TESTS / "backup-ui.cjs")],
                                input=json.dumps(payload), capture_output=True, text=True, encoding="utf-8", timeout=180)
        output = result.stdout + result.stderr
        for secret in (self.api_key, self.metadata_key, self.password, self.username):
            output = output.replace(secret, "[REDACTED]")
        artifact.joinpath("backup-browser.log").write_text(output, encoding="utf-8")
        print(output, flush=True)
        browser_report = artifact / "backup-ui.json"
        if browser_report.exists():
            self.browser_report = json.loads(browser_report.read_text(encoding="utf-8"))
        require(result.returncode == 0 and self.browser_report and self.browser_report.get("passed"),
                "Packaged backup task browser check failed; see separate backup-ui receipt")
        require(self.browser_report.get("imageId") == self.image_id, "Backup browser used another image")
        self.browser_tested = True
        self.check("actual Tasks Backup button updates Last Execution through SignalR without page reload")

    def no_acquisition(self):
        for name in ("series", "importlist", "indexer", "downloadclient"):
            require(self.json_api("/api/v5/" + name) == [], "Owned backup test changed acquisition data")
        require(self.json_api("/api/v5/queue").get("records") == [], "Backup fixture created queued downloads")
        self.check("owned backup verification leaves library, lists, indexers, clients and queue empty")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:verified")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--browser-node")
    parser.add_argument("--playwright-module")
    args = parser.parse_args()
    require(bool(args.browser_node) == bool(args.playwright_module), "Provide both external browser options")
    require(not args.report.exists(), "Use a fresh report path to preserve earlier verification evidence")
    args.report.parent.mkdir(parents=True, exist_ok=True)
    audit = BackupTasks(args)
    error = None
    logs = ""
    cleaned = False
    try:
        audit.start()
        details = json.loads(audit.docker("image", "inspect", args.image))[0]
        audit.image_labels = details["Config"].get("Labels", {}) or {}
        audit.authentication()
        status = audit.json_api("/api/v5/system/status")
        audit.runtime_metadata = {key: status.get(key) for key in ("productName", "productVersion", "version", "runtimeVersion", "migrationVersion")}
        audit.check("owned Forms-authenticated packaged backup instance started")
        audit.seed_backup_time()
        audit.manual_backup("v5")
        time.sleep(1.1)
        audit.manual_backup("v3")
        audit.failure_and_recovery()
        audit.scheduled_backup()
        audit.persist()
        if args.browser_node:
            audit.browser()
        audit.no_acquisition()
    except (Exception, KeyboardInterrupt) as failure:
        error = str(failure) or type(failure).__name__
        for secret in (audit.api_key, audit.metadata_key, audit.password, audit.username):
            error = error.replace(secret, "[REDACTED]")
        print("FAIL " + error, flush=True)
    finally:
        if audit.container:
            try:
                logs = audit.docker("logs", audit.container)
                for secret in (audit.api_key, audit.metadata_key, audit.password, audit.username):
                    logs = logs.replace(secret, "[REDACTED]")
            except Exception as failure:
                error = error or "Could not collect owned backup logs: " + type(failure).__name__
        try:
            audit.cleanup()
            cleaned = not args.keep
        except Exception as failure:
            error = error or "Owned backup cleanup failed: " + type(failure).__name__
        args.report.with_suffix(".log").write_text(logs, encoding="utf-8")
        args.report.write_text(json.dumps({
            "createdUtc": utc_now(), "passed": error is None, "error": error, "checks": audit.results,
            "image": args.image, "imageId": audit.image_id,
            "sourceRevision": audit.image_labels.get("org.opencontainers.image.revision"),
            "runtime": audit.runtime_metadata, "observations": audit.evidence,
            "browserUITested": audit.browser_tested, "browserChecks": audit.browser_report,
            "fixtureConfigOwnership": audit.fixture_config_ownership,
            "scope": {"mainConfigurationAccessed": False, "realLibraryTested": False,
                      "downloadsTested": False, "realBackupZipTested": True},
            "cleanupComplete": cleaned,
            "keptContainer": audit.container if args.keep else None,
            "keptData": str(audit.root) if args.keep else None
        }, indent=2) + "\n", encoding="utf-8")
    return 1 if error else 0


if __name__ == "__main__":
    raise SystemExit(main())

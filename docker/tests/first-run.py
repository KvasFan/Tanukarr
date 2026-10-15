#!/usr/bin/env python3
"""Verify first-run account creation with disposable configuration and credentials."""

import argparse
import html
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import subprocess
import sys
import urllib.parse

from smoke import require


TESTS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("first_run_security", TESTS / "functional-security.py")
functional = importlib.util.module_from_spec(spec)
spec.loader.exec_module(functional)


class FirstRun(functional.FunctionalSecurity):
    def submit(self, token, cookie, username=None, password=None, confirmation=None):
        form = urllib.parse.urlencode({
            "__RequestVerificationToken": token,
            "username": self.username if username is None else username,
            "password": self.password if password is None else password,
            "passwordConfirmation": self.password if confirmation is None else confirmation,
        })
        return self.raw("/login/setup", "POST", form, api_key=False, headers={
            "Content-Type": "application/x-www-form-urlencoded", "Cookie": cookie,
        })

    def browser(self):
        artifacts = self.args.report.parent / (self.args.report.stem + "-browser")
        artifacts.mkdir(parents=True, exist_ok=True)
        def browser_path(value):
            return subprocess.check_output(["wslpath", "-w", str(value)], text=True).strip() if self.args.windows_browser else str(value)

        payload = {"ownedFixture": True, "baseUrl": self.base_url, "imageId": self.image_id,
                   "username": self.username, "password": self.password,
                   "playwrightModule": self.args.playwright_module, "playwrightVersion": self.args.playwright_version,
                   "artifactDirectory": browser_path(artifacts)}
        result = subprocess.run([self.args.browser_node, browser_path(TESTS / "first-run-browser.cjs")],
                                input=json.dumps(payload), capture_output=True, text=True, timeout=180)
        # Browser errors must not export runtime secrets.
        detail = result.stdout + result.stderr
        for secret in (self.username, self.password, self.api_key):
            detail = detail.replace(secret, "[REDACTED]")
        print(detail.strip(), flush=True)
        require(result.returncode == 0, "First-run browser check failed")
        self.browser_tested = True

    def run(self):
        self.start()
        require(self.json_api("/api/v5/settings/general")["username"] in (None, ""),
                "Fresh startup unexpectedly created a default account")
        require(self.raw("/", api_key=False)[0] == 302, "Fresh UI was not protected")
        require(self.raw("/api/v5/series", api_key=False)[0] == 401, "Fresh API was not protected")
        status, headers, body = self.raw("/login", api_key=False)
        require(status == 200 and b"setup-required" in body and b"CREATE YOUR ACCOUNT" in body,
                "First-run account form is unavailable")
        require("no-store" in headers.get("Cache-Control", ""), "Setup page is cacheable")
        token = html.unescape(re.search(rb'name="__RequestVerificationToken"\s+value="([^"]+)"', body).group(1).decode())
        cookie = headers.get("Set-Cookie", "").split(";", 1)[0]
        require(token and cookie, "Setup did not issue an antiforgery token and cookie")
        require(self.submit("", cookie)[0] == 400, "Setup accepted a missing antiforgery token")
        require(self.submit(token, "")[0] == 400, "Setup accepted a missing antiforgery cookie")
        for username, password, confirmation in (
                ("", self.password, self.password),
                (self.username, "short", "short"),
                (self.username, self.password, "different-password"),
                ("u" * 65, self.password, self.password),
                (self.username, "p" * 129, "p" * 129)):
            status, response_headers, _ = self.submit(token, cookie, username, password, confirmation)
            require(status == 302 and "setupInvalid=true" in response_headers.get("Location", ""),
                    "Setup failed to reject invalid credentials")
        require(self.json_api("/api/v5/settings/general")["username"] in (None, ""),
                "Invalid setup inserted an account")
        self.check("protected first-run UI/API, no default account, no-store form, CSRF and server validation")

        if self.args.browser_node:
            self.browser()
        else:
            status, response_headers, _ = self.submit(token, cookie)
            require(status == 302 and response_headers.get("Location") == "/", "Setup did not sign in")
            require("httponly" in response_headers.get("Set-Cookie", "").lower(), "Setup session lacks HttpOnly")
        require(self.json_api("/api/v5/settings/general")["username"] == self.username,
                "Setup did not persist the requested account")
        require(self.json_api("/api/v5/settings/general")["password"] == "********", "Saved password is unmasked")
        status, response_headers, _ = self.submit(token, cookie, "attacker")
        require(status == 302 and "setupInvalid=true" in response_headers.get("Location", ""),
                "Old setup form replaced the account")
        require(self.json_api("/api/v5/settings/general")["username"] == self.username,
                "Replayed setup changed the saved account")
        self.check("account creation signs in and masks credentials; stale setup cannot replace an account")

        self.docker("restart", "--time", "20", self.container)
        self.refresh_binding()
        self.wait_ready()
        status, _, body = self.raw("/login", api_key=False)
        require(status == 200 and b'class="auth-forms "' in body,
                "Setup reopened after restart with an existing account")
        form = urllib.parse.urlencode({"username": self.username, "password": self.password})
        status, response_headers, _ = self.raw("/login", "POST", form, api_key=False,
                                             headers={"Content-Type": "application/x-www-form-urlencoded"})
        require(status == 302 and response_headers.get("Set-Cookie"), "Created account cannot sign in after restart")
        database = self.root / "config" / "sonarr.db"
        with sqlite3.connect(database) as connection:
            users = connection.execute('SELECT "Username", "Password", "Salt", "Iterations" FROM "Users"').fetchall()
        require(len(users) == 1 and users[0][0] == self.username and users[0][1] != self.password and
                users[0][2] and users[0][3] >= 220000, "First-run password was not salted and hashed")
        logs = self.docker("logs", self.container)
        logs += self.docker("exec", self.container, "sh", "-c", "cat /config/logs/*.txt 2>/dev/null || true")
        require(self.password not in logs and "with the password:" not in logs,
                "Startup or account creation exposed a password in logs")
        self.check("restart retains exactly one salted account, graphical setup stays closed, passwords absent from logs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="tanukarr:local")
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--browser-node")
    parser.add_argument("--playwright-module")
    parser.add_argument("--playwright-version")
    parser.add_argument("--windows-browser", action="store_true")
    args = parser.parse_args()
    require(bool(args.browser_node) == bool(args.playwright_module), "Provide both browser arguments")
    audit = FirstRun(args)
    try:
        audit.run()
    except Exception as error:
        detail = str(error)
        for secret in (audit.password, audit.api_key):
            detail = detail.replace(secret, "[REDACTED]")
        audit.failures.append({"check": "first-run", "error": detail})
        print("FAIL " + detail, flush=True)
    finally:
        try:
            audit.cleanup()
        except Exception as error:
            audit.failures.append({"check": "cleanup", "error": type(error).__name__})
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({"image": args.image, "imageId": audit.image_id,
                                          "passed": not audit.failures, "checks": audit.results,
                                          "failures": audit.failures, "browserUITested": audit.browser_tested,
                                          "keptContainer": audit.container if args.keep else None,
                                          "keptData": str(audit.root) if args.keep and audit.root else None,
                                          "realLibraryTested": False}, indent=2) + "\n", encoding="utf-8")
    return int(bool(audit.failures))


if __name__ == "__main__":
    sys.exit(main())

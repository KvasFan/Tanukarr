#!/usr/bin/env python3
"""Package verified SonarrWeeb image, source, deployment YAML and evidence."""

import argparse
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def run(executable, *arguments, cwd=None, timeout=600):
    result = subprocess.run(
        [executable, *arguments], cwd=cwd, capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=timeout,
    )
    require(result.returncode == 0, f"{Path(executable).name} {arguments[0]} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(chunk)
    return checksum.hexdigest()


def compose(image):
    return """# SonarrWeeb release deployment. Docker Compose v2 is required.
# Keep config and media directories on storage owned by PUID/PGID.
services:
  sonarrweeb:
    image: IMAGE_REFERENCE
    container_name: sonarrweeb
    restart: unless-stopped
    ports:
      - "${SONARRWEEB_BIND_ADDRESS:-127.0.0.1}:${SONARRWEEB_PORT:-6970}:8989"
    environment:
      PUID: "${SONARRWEEB_PUID:-1000}"
      PGID: "${SONARRWEEB_PGID:-1000}"
      UMASK: "${SONARRWEEB_UMASK:-002}"
      TZ: "${SONARRWEEB_TZ:-Etc/UTC}"
    volumes:
      - "${SONARRWEEB_CONFIG_PATH:-./config}:/config"
      - "${SONARRWEEB_MEDIA_PATH:-./media}:/media"
""".replace("IMAGE_REFERENCE", image)


def verify_archive(path, expected_image, image):
    """Accept both classic config IDs and containerd manifest/index image IDs."""
    with tarfile.open(path, "r") as archive:
        def read_member(name):
            try:
                member = archive.extractfile(name)
            except KeyError as error:
                raise ValueError(f"Docker archive is missing {name}") from error
            require(member is not None, f"Docker archive is missing {name}")
            return member.read()

        manifest = json.loads(read_member("manifest.json"))
        require(len(manifest) == 1 and expected_image in manifest[0].get("RepoTags", []),
                "Docker archive does not contain the expected release tag")
        config_bytes = read_member(manifest[0]["Config"])
        config_digest = "sha256:" + hashlib.sha256(config_bytes).hexdigest()
        config = json.loads(config_bytes)
        require(config.get("os") == image["Os"] and config.get("architecture") == image["Architecture"],
                "Docker archive platform differs from the verified image")
        require(config.get("rootfs", {}).get("diff_ids") == image.get("RootFS", {}).get("Layers"),
                "Docker archive filesystem differs from the verified image")
        require(config.get("config", {}).get("Labels") == image.get("Config", {}).get("Labels"),
                "Docker archive provenance labels differ from the verified image")
        if config_digest == image["Id"]:
            return

        # Newer Docker/containerd stores expose the OCI manifest/index digest as
        # .Id. Verify its content-addressed path to the exact saved configuration.
        descriptor_digest = image.get("Descriptor", {}).get("digest", image["Id"])
        require(descriptor_digest == image["Id"], "Image descriptor and inspected identity disagree")
        index = json.loads(read_member("index.json"))
        require(any(item.get("digest") == descriptor_digest for item in index.get("manifests", [])),
                "Docker archive does not reference the verified image descriptor")

        def references_config(blob_digest, depth=0):
            require(depth < 8 and bool(re.fullmatch(r"sha256:[0-9a-f]{64}", blob_digest)),
                    "Docker archive has an invalid OCI descriptor")
            blob = read_member("blobs/sha256/" + blob_digest.partition(":")[2])
            require("sha256:" + hashlib.sha256(blob).hexdigest() == blob_digest,
                    "Docker archive OCI descriptor checksum mismatch")
            document = json.loads(blob)
            if "config" in document:
                return document["config"].get("digest") == config_digest
            return any(references_config(item["digest"], depth + 1) for item in document.get("manifests", []))

        require(references_config(descriptor_digest),
                "Docker archive descriptor does not identify the verified image configuration")


def package(args):
    require(bool(re.fullmatch(r"\d+\.\d+\.\d+", args.version)), "Version must be MAJOR.MINOR.PATCH")
    require(bool(re.fullmatch(r"[A-Za-z0-9-]+/SonarrWeeb", args.repository)),
            "Repository must identify OWNER/SonarrWeeb")
    expected_image = f"ghcr.io/{args.repository.lower()}:{args.version}"
    require(args.image == expected_image, f"Expected release image {expected_image}")
    root = Path(__file__).resolve().parents[2]
    require((root / "VERSION").read_text(encoding="utf-8").strip() == args.version,
            "VERSION does not match the requested release")
    revision = run(args.git, "rev-parse", "HEAD", cwd=root)
    require(not run(args.git, "status", "--porcelain", "--untracked-files=no", cwd=root),
            "Commit tracked source changes before packaging a corresponding-source release")
    run(args.git, "cat-file", "-e", "HEAD:docker/release/package.py", cwd=root)

    image = json.loads(run(args.docker, "image", "inspect", args.image))[0]
    require(image["Os"] == "linux" and image["Architecture"] == "amd64",
            "This release has only been validated on linux/amd64")
    labels = image.get("Config", {}).get("Labels", {}) or {}
    require(labels.get("org.opencontainers.image.title") == "SonarrWeeb", "Image title is not SonarrWeeb")
    require(labels.get("org.opencontainers.image.version") == args.version, "Image version label mismatch")
    require(labels.get("org.opencontainers.image.revision") == revision,
            "Image revision does not match the committed source being packaged")
    require(labels.get("org.opencontainers.image.source") == f"https://github.com/{args.repository}",
            "Image source label does not identify the release repository")

    smoke = load_json(args.smoke_report)
    functional = load_json(args.functional_report)
    require(smoke.get("passed") is True and functional.get("passed") is True,
            "Both container verification reports must pass before packaging")
    require(functional.get("imageId") == image["Id"], "Functional report belongs to a different image")
    require(smoke.get("imageId") == image["Id"], "Smoke report belongs to a different image")
    require(smoke.get("runtime", {}).get("productVersion") == args.version,
            "Smoke report product version does not match the release")
    require(smoke.get("image") == functional.get("image"), "Verification reports identify different images")
    require(bool(smoke.get("checks")) and bool(functional.get("checks")), "Verification reports contain no checks")
    require(not smoke.get("keptContainer") and not functional.get("keptContainer"),
            "Use completed, cleaned-up test reports for release packaging")
    sbom = load_json(args.sbom)
    require(sbom.get("bomFormat") == "CycloneDX" and bool(sbom.get("components")),
            "Provide a CycloneDX image SBOM with discovered components")

    output = args.output.resolve()
    require(not output.exists() or (output.is_dir() and not any(output.iterdir())),
            "Output must be a new or empty directory; existing release files will not be overwritten")
    output.parent.mkdir(parents=True, exist_ok=True)
    stem = f"SonarrWeeb-{args.version}"
    with tempfile.TemporaryDirectory(prefix="sonarrweeb-release-", dir=output.parent) as temporary:
        staging = Path(temporary)
        image_tar = staging / "image.tar"
        run(args.docker, "save", "--output", str(image_tar), args.image)
        # Validate the saved archive before handing it to users of docker load.
        verify_archive(image_tar, args.image, image)
        compressed = staging / f"{stem}-linux-amd64-docker.tar.gz"
        with image_tar.open("rb") as source, compressed.open("wb") as destination:
            with gzip.GzipFile(fileobj=destination, mode="wb", filename="", mtime=0) as zipped:
                shutil.copyfileobj(source, zipped, length=1024 * 1024)
        image_tar.unlink()
        run(args.git, "archive", "--format=tar.gz", f"--prefix={stem}/",
            "--output", str(staging / f"{stem}-source.tar.gz"), revision, cwd=root)
        shutil.copyfile(args.sbom, staging / f"{stem}-sbom.cdx.json")
        shutil.copyfile(args.smoke_report, staging / "smoke.json")
        shutil.copyfile(args.functional_report, staging / "functional-security.json")
        (staging / "compose.yaml").write_text(compose(args.image), encoding="utf-8", newline="\n")
        metadata = {
            "project": "SonarrWeeb", "version": args.version,
            "repository": f"https://github.com/{args.repository}", "sourceCommit": revision,
            "image": args.image, "imageId": image["Id"],
            "runtime": smoke["runtime"],
            "registryDigests": image.get("RepoDigests", []), "platform": "linux/amd64",
            "created": datetime.now(timezone.utc).isoformat(),
            "smokeChecks": len(smoke["checks"]), "functionalChecks": len(functional["checks"]),
            "liveAniListRequested": bool(functional.get("liveAniListRequested")),
            "browserUITested": bool(functional.get("browserUITested")),
            "realLibraryTested": bool(smoke.get("realLibraryTested") or functional.get("realLibraryTested")),
            "downloadsTested": bool(smoke.get("downloadsTested") or functional.get("downloadsTested")),
            "notes": "Verification scope is recorded in the included reports; the SBOM is an inventory, not a clean security scan.",
        }
        (staging / "release-manifest.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        files = sorted(path for path in staging.iterdir() if path.is_file())
        checksums = "".join(f"{digest(path)}  {path.name}\n" for path in files)
        (staging / "SHA256SUMS").write_text(checksums, encoding="utf-8", newline="\n")
        output.mkdir(exist_ok=True)
        for path in staging.iterdir():
            shutil.move(str(path), output / path.name)
    print(f"Packaged SonarrWeeb {args.version}: {output}")
    print(f"Verified image: {image['Id']}; corresponding source: {revision}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--repository", required=True, help="GitHub OWNER/SonarrWeeb")
    parser.add_argument("--image", required=True, help="locally available ghcr.io/owner/sonarrweeb:version")
    parser.add_argument("--sbom", required=True, type=Path)
    parser.add_argument("--smoke-report", required=True, type=Path)
    parser.add_argument("--functional-report", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--docker", default="docker")
    parser.add_argument("--git", default="git")
    args = parser.parse_args()
    try:
        package(args)
    except (ValueError, OSError, subprocess.TimeoutExpired, tarfile.TarError) as error:
        print(f"Release packaging failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

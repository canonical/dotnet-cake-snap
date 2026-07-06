#!/usr/bin/env python3
"""
Check if the Snap Store version of dotnet-cake matches the latest upstream
Cake version for a given channel.

  --channel stable  : compares against the latest GitHub release tag of
                      cake-build/cake (e.g. "6.2.0").
  --channel edge    : compares against the latest commit SHA (7 chars) on the
"""

import sys
import json
import os
import urllib.request
import urllib.error
import socket
import http.client
import argparse


UPSTREAM_REPO = "cake-build/cake"
UPSTREAM_EDGE_BRANCH = "develop"
SNAP_NAME = "dotnet-cake"


def get_github_latest_release(token=None):
    """Fetch the latest release version from GitHub (tag_name, 'v' stripped)."""
    url = f"https://api.github.com/repos/{UPSTREAM_REPO}/releases/latest"
    try:
        request = urllib.request.Request(url)
        if token:
            request.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request) as response:
            data = json.loads(response.read().decode())
            tag = data.get("tag_name", "").strip()
            return tag[1:] if tag.startswith("v") else tag
    except urllib.error.URLError as e:
        print(f"Error fetching GitHub release: {e}", file=sys.stderr)
        return None


def get_github_latest_commit(token=None):
    """Fetch the latest commit SHA (7 chars) from the upstream edge branch."""
    url = f"https://api.github.com/repos/{UPSTREAM_REPO}/commits/{UPSTREAM_EDGE_BRANCH}"
    try:
        request = urllib.request.Request(url)
        if token:
            request.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request) as response:
            data = json.loads(response.read().decode())
            # Short SHA (7 chars) to match the snap version format used for edge.
            return data.get("sha", "")[:7]
    except urllib.error.URLError as e:
        print(f"Error fetching upstream commit: {e}", file=sys.stderr)
        return None


class UnixSocketHTTPConnection(http.client.HTTPConnection):
    """HTTP connection over a Unix socket (for the local snapd API)."""

    def __init__(self, socket_path):
        super().__init__("localhost")
        self.socket_path = socket_path

    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.connect(self.socket_path)


def get_snap_store_version(channel):
    """Fetch the current version from the Snap Store via the snapd API."""
    socket_path = "/run/snapd.socket"
    try:
        conn = UnixSocketHTTPConnection(socket_path)
        conn.request("GET", f"/v2/find?name={SNAP_NAME}")
        response = conn.getresponse()
        if response.status != 200:
            print(f"Error: snapd API returned status {response.status}", file=sys.stderr)
            return None

        data = json.loads(response.read().decode())
        if data.get("type") != "sync" or "result" not in data:
            print("Unexpected snapd API response", file=sys.stderr)
            return None

        for snap in data["result"]:
            if snap.get("name") == SNAP_NAME:
                channels = snap.get("channels", {})
                channel_key = f"latest/{channel}"
                if channel_key in channels:
                    return channels[channel_key].get("version", "").strip()
                print(f"Channel {channel_key} not found in store", file=sys.stderr)
                return None

        print(f"{SNAP_NAME} snap not found in store", file=sys.stderr)
        return None
    except (socket.error, OSError) as e:
        print(f"Error connecting to snapd socket: {e}", file=sys.stderr)
        return None
    except (json.JSONDecodeError, http.client.HTTPException) as e:
        print(f"Error fetching Snap info: {e}", file=sys.stderr)
        return None


def main():
    parser = argparse.ArgumentParser(
        description=f"Check if Snap Store has the latest {SNAP_NAME} version for a given channel"
    )
    parser.add_argument(
        "--channel",
        choices=["stable", "edge"],
        default="stable",
        help="Snap channel to check (default: stable)",
    )
    args = parser.parse_args()

    token = os.getenv("GITHUB_TOKEN")
    auth_mode = "authenticated" if token else "unauthenticated"
    print(f"Using {auth_mode} GitHub API access")

    if args.channel == "stable":
        print("Fetching latest GitHub release version...")
        expected_version = get_github_latest_release(token=token)
        version_type = "GitHub release"
    else:  # edge
        print(f"Fetching latest upstream {UPSTREAM_EDGE_BRANCH} commit SHA...")
        expected_version = get_github_latest_commit(token=token)
        version_type = f"Upstream {UPSTREAM_EDGE_BRANCH} SHA"

    if not expected_version:
        print(f"Failed to fetch {version_type}", file=sys.stderr)
        return 2

    print(f"{version_type}: {expected_version}")

    print(f"Fetching Snap Store {args.channel} channel version...")
    snap_version = get_snap_store_version(args.channel)
    if not snap_version:
        print(f"Failed to fetch snap version from {args.channel} channel", file=sys.stderr)
        return 2

    print(f"Snap Store {args.channel} version: {snap_version}")

    if expected_version == snap_version:
        print(f"✓ Versions match - Snap Store {args.channel} has the latest {SNAP_NAME} version")
        return 0

    print(f"✗ Versions differ - Snap Store {args.channel} does NOT have the latest {SNAP_NAME} version")
    return 1


if __name__ == "__main__":
    sys.exit(main())

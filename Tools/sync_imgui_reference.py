#!/usr/bin/env python3
# The MIT License (MIT)
# Copyright (c) 2014-2026 Omar Cornut
# Copyright (c) 2026 Least Action Technologies Inc.

import argparse
import os
import subprocess
import sys


def run_command(cmd, cwd=None, capture=True):
    result = subprocess.run(cmd, cwd=cwd, text=True, capture_output=capture)
    if result.returncode != 0:
        err = result.stderr.strip() if result.stderr else result.stdout.strip()
        raise RuntimeError(f"Command failed ({' '.join(cmd)}): {err}")
    return result.stdout.strip() if result.stdout else ""


def sync_reference(repo_url: str, commit_or_tag: str, destination: str):
    dest_path = os.path.abspath(destination)
    os.makedirs(os.path.dirname(dest_path), exist_ok=True)

    print(f"[*] Synchronizing canonical Dear ImGui reference...")
    print(f"    Repository:  {repo_url}")
    print(f"    Target:      {commit_or_tag}")
    print(f"    Destination: {dest_path}")

    is_git_repo = os.path.isdir(os.path.join(dest_path, ".git"))

    if not is_git_repo:
        if os.path.exists(dest_path) and os.listdir(dest_path):
            raise RuntimeError(f"Destination directory '{dest_path}' exists and is not empty or a git repository.")
        
        print(f"[*] Cloning repository into {dest_path}...")
        # Shallow clone if it's a tag or branch; if it fails, fallback to full clone
        try:
            run_command(["git", "clone", "--depth", "1", "--branch", commit_or_tag, repo_url, dest_path])
        except RuntimeError:
            print("[*] Shallow branch/tag clone failed; cloning with blob filter...")
            run_command(["git", "clone", "--filter=blob:none", repo_url, dest_path])
            run_command(["git", "checkout", commit_or_tag], cwd=dest_path)
    else:
        print(f"[*] Fetching updates in existing reference directory...")
        run_command(["git", "fetch", "--tags", "origin"], cwd=dest_path)
        run_command(["git", "checkout", commit_or_tag], cwd=dest_path)

    # Validate essential Dear ImGui files
    imgui_h = os.path.join(dest_path, "imgui.h")
    imgui_internal_h = os.path.join(dest_path, "imgui_internal.h")
    if not os.path.isfile(imgui_h) or not os.path.isfile(imgui_internal_h):
        raise FileNotFoundError(f"Missing required header files in checkout: imgui.h or imgui_internal.h")

    commit_sha = run_command(["git", "rev-parse", "HEAD"], cwd=dest_path)
    commit_desc = run_command(["git", "log", "-1", "--oneline"], cwd=dest_path)

    print(f"[+] Canonical Dear ImGui reference synchronized successfully.")
    print(f"    Resolved Commit: {commit_sha}")
    print(f"    Commit Info:     {commit_desc}")
    print(f"    Header Path:     {imgui_h}")
    return commit_sha


def main():
    parser = argparse.ArgumentParser(description="Synchronize external canonical Dear ImGui reference checkout.")
    parser.add_argument("--repo", default="https://github.com/ocornut/imgui", help="Git repository URL")
    parser.add_argument("--commit", default="v1.93.0-docking", help="Git commit, branch, or tag to checkout")
    parser.add_argument("--destination", default="ThirdParty/imgui", help="Local destination path (git-ignored)")

    args = parser.parse_args()

    try:
        commit_sha = sync_reference(args.repo, args.commit, args.destination)
        print(f"[OK] Reference ready at commit {commit_sha[:10]}")
    except Exception as ex:
        print(f"[ERROR] Failed to synchronize reference: {ex}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()

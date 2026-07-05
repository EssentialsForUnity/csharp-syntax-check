#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_root="$(mktemp -d)"
last_output=""

trap 'rm -rf "$tmp_root"' EXIT

expect_failure() {
  local name="$1"
  shift

  local output="$tmp_root/${name}.txt"
  set +e
  "$@" >"$output" 2>&1
  local status=$?
  set -e

  sed 's#^::#: :#' "$output"

  if [[ "$status" -eq 0 ]]; then
    echo "Expected $name to fail."
    exit 1
  fi

  last_output="$output"
}

expect_output() {
  local pattern="$1"

  if ! grep -Fq "$pattern" "$last_output"; then
    echo "Expected output to contain: $pattern"
    exit 1
  fi
}

checker=(dotnet run --project "$repo_root/src/CSharpSyntaxCheck" --)

expect_failure invalid-csharp \
  "${checker[@]}" \
  --path "$repo_root/tests/fixtures/invalid-csharp" \
  --language-version preview \
  --fail-on-empty
expect_output "Broken.cs"
expect_output "C# syntax check failed"

empty_project="$tmp_root/empty"
mkdir -p "$empty_project"

expect_failure empty-required-scan \
  "${checker[@]}" \
  --path "$empty_project" \
  --language-version preview \
  --fail-on-empty
expect_output "No C# files were found to check."

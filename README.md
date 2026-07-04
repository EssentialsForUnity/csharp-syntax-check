# C# Syntax Check

Reusable GitHub Actions workflow for a basic Roslyn parse check across C# files.

This is intentionally lightweight. It catches syntax errors in `.cs` files without requiring a solution file, Unity project files, restored NuGet packages, or a full compile.

## Usage

Add a workflow to the consuming repository:

```yaml
name: C# Syntax Check

on:
  push:
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  syntax:
    uses: EssentialsForUnity/csharp-syntax-check/.github/workflows/csharp-syntax.yml@main
```

You can also call the action directly when you want normal action-style version pinning:

```yaml
name: C# Syntax Check

on:
  push:
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  syntax:
    name: Parse C# files
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: C# syntax check
        uses: EssentialsForUnity/csharp-syntax-check@main
```

Optional inputs:

```yaml
jobs:
  syntax:
    uses: EssentialsForUnity/csharp-syntax-check/.github/workflows/csharp-syntax.yml@main
    with:
      path: .
      dotnet-version: 8.0.x
      language-version: preview
      exclude-directories: .git,.vs,bin,Build,Builds,Library,Logs,obj,Temp,UserSettings
      fail-on-empty: true
      checker-repository: EssentialsForUnity/csharp-syntax-check
      checker-ref: main
```

## What This Does Not Do

This does not replace a real build, Unity compile, analyzers, package restore, asmdef validation, or tests. It only parses C# source files and reports syntax diagnostics.

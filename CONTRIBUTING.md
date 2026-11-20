# Contributing to The Readme

Bug reports, accessibility feedback, translations of future localized strings, documentation fixes, and small Windows integrations are welcome. Open an issue first for larger changes. Include a minimal, public Markdown sample for rendering bugs.

## Build and test

Use Windows and a .NET 10 SDK that satisfies [global.json](global.json). From the repository root, run:

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore -warnaserror
dotnet run --project tests/TheReadme.Smoke -c Release --no-build
dotnet run --project src/TheReadme
```

To build the x64 and Arm64 EXE installers, run `./scripts/Build-Installer.ps1`. It installs the pinned Inno Setup compiler under `.tools/inno` if needed. Installer builds and hashes go into `artifacts/installers`. Pass `-Rid win-x64` to build just the x64 installer.

## Pull requests

1. Create a branch from current `main`.
2. Keep the change focused. Update tests or docs affected by the change and include screenshots for UI changes.
3. Open a pull request to `main` and let Windows CI run on the final commit.
4. Resolve review conversations and merge through GitHub when required checks pass.

Every source, documentation, or workflow change follows this process. A maintainer can tag a tested commit on `main` when the project version changes. The release workflow rebuilds and tests the installers before publishing the assets and their SHA-256 checksums.

By submitting a contribution, you license it under [Apache License 2.0](LICENSE). Follow the [code of conduct](CODE_OF_CONDUCT.md) and use the [private reporting procedure](SECURITY.md) for vulnerabilities.

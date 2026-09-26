# One Dalamud URL for multiple plugins

Keep the existing URL:

```text
https://raw.githubusercontent.com/kuchris/xivaichat/main/repo.json
```

This JSON file is a catalogue of plugins. Each entry points to its own ZIP. Source projects and release assets can live in different GitHub repositories while players subscribe to this one URL.

## Setup

1. Keep the existing `XivAiChat` entry in `xivaichat/main/repo.json`.
2. Upload the MoreMacros package as a public GitHub Release asset. The current local package is `artifacts/MoreMacros-0.3.1.0.zip`. It contains the entry DLL, Core dependency, manifest, and dependency metadata at the ZIP root.
3. Add a second object to the JSON array, using the built `artifacts/plugin/MoreMacros.json` as its manifest source. Set the fields below using the real uploaded asset URL.
4. Publish the updated catalogue on `main`. Existing users keep their repository setting and refresh the plugin installer to discover MoreMacros.

| Field | MoreMacros value |
| --- | --- |
| `InternalName` | `MoreMacros` (must match the assembly/manifest name) |
| `Name` | `MoreMacros` |
| `AssemblyVersion` | `0.3.1.0` (use the package's actual version) |
| `DalamudApiLevel` | `15` (use the built manifest value) |
| `DownloadLinkInstall` | Public URL of the uploaded MoreMacros ZIP |
| `DownloadLinkUpdate` | Same URL as the installation ZIP |
| `RepoUrl` | Source/project page for MoreMacros |
| `Author`, `Description`, `Punchline`, `Tags` | Copy from the built manifest |
| `LastUpdate` | Current Unix timestamp |

Use a version-specific asset URL so a catalogue version always identifies the same package. Upload the ZIP before publishing its catalogue entry. Each plugin keeps a distinct `InternalName` and its own version number. No DLLs or config files need to be merged.

New users add the existing URL in `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, save, then open `/xlplugins`. They can install either plugin separately.

## Adjust the current XivAiChat packer

The current `tools/pack.ps1` reads the full catalogue but updates `$repoEntries[0]`. Select the entry by identity before assigning its metadata:

```powershell
$repoEntries = @(Get-Content -LiteralPath $repoJsonPath -Raw | ConvertFrom-Json)
$matches = @($repoEntries | Where-Object { $_.InternalName -eq 'XivAiChat' })
if ($matches.Count -ne 1) {
    throw 'Expected exactly one XivAiChat entry in repo.json.'
}
$entry = $matches[0]
```

Replace each subsequent `$repoEntries[0]` assignment in that script with `$entry`. Continue serializing **the complete `$repoEntries` array** with `ConvertTo-Json -InputObject $repoEntries -Depth 10`; MoreMacros and future entries remain intact.

For a MoreMacros release, perform the same update by `InternalName = 'MoreMacros'`, inserting a new object only when it is absent. Do not regenerate the shared catalogue from a single plugin's manifest.

## Release automation

The existing XivAiChat workflow triggers on tags and publishes `XivAiChat.zip`. Keep that workflow specific to XivAiChat. MoreMacros needs its own package/release step, even though both packages appear at the same subscription URL.

When updating the shared catalogue, start from the latest `main`, merge only the matching entry, and push that commit. Serialize catalogue updates or retry a rejected push after re-reading `main`; this prevents simultaneous plugin releases from losing each other's metadata. If source projects live in separate repositories, writing to `xivaichat` requires a GitHub App or a token with permission to that repository; a source repository's default `GITHUB_TOKEN` does not grant write access to another repository.

## Sources inspected

- [Current catalogue](https://raw.githubusercontent.com/kuchris/xivaichat/main/repo.json)
- [Current packer](https://raw.githubusercontent.com/kuchris/xivaichat/main/tools/pack.ps1)
- [Current release workflow](https://raw.githubusercontent.com/kuchris/xivaichat/main/.github/workflows/release.yml)
- [GitHub token scope](https://docs.github.com/en/actions/concepts/security/github_token)

This guide describes the setup. No GitHub repository, release, or public catalogue was changed by creating it.

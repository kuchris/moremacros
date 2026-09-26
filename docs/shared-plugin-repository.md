# One Dalamud URL for multiple plugins

The shared catalogue is published at:

```text
https://raw.githubusercontent.com/kuchris/DalamudPlugins/main/repo.json
```

The dedicated [DalamudPlugins repository](https://github.com/kuchris/DalamudPlugins) includes **XIV AI Chat** and **MoreMacros**. Players subscribe once and install either plugin separately. Each plugin retains its own settings, source repository, version, and release ZIP.

| Plugin | Source | Initial shared-catalogue release |
| --- | --- | --- |
| XIV AI Chat | [kuchris/xivaichat](https://github.com/kuchris/xivaichat) | [0.1.2.0](https://github.com/kuchris/xivaichat/releases/tag/0.1.2.0) |
| MoreMacros | [kuchris/moremacros](https://github.com/kuchris/moremacros) | [0.3.1.0](https://github.com/kuchris/moremacros/releases/tag/0.3.1.0) |

## Player setup

Add the URL in `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, save, and open `/xlplugins`. If you used `xivaichat/main/repo.json`, replace that repository entry with the new URL. Keep the installed plugins and their settings, then refresh the installer.

## Publish a MoreMacros update

1. Change the version in `MoreMacros/MoreMacros.csproj`, then run `./test.ps1` and `./build.ps1`.
2. Commit and push the source. Create a GitHub Release for that commit and upload `artifacts/MoreMacros-<version>.zip`.
3. Check that the public ZIP downloads and contains `MoreMacros.dll`, `MoreMacros.Core.dll`, `MoreMacros.json`, and `MoreMacros.deps.json` at its root.
4. Update a fresh checkout of `kuchris/DalamudPlugins/main`. In `repo.json`, select the entry with `InternalName = MoreMacros` and update its fields from the packaged manifest:

   - `AssemblyVersion` and `TestingAssemblyVersion`
   - `DalamudApiLevel` and `TestingDalamudApiLevel`
   - `Author`, `Name`, `Description`, `Punchline`, `Tags`, and `RepoUrl` when changed
   - `LastUpdate`, using the current Unix timestamp
   - `DownloadLinkInstall`, `DownloadLinkUpdate`, and `DownloadLinkTesting`, using the real version-specific release asset URL

5. Keep every other plugin entry intact. Commit and push the catalogue after the release ZIP is available.

For example, the first MoreMacros asset is:

```text
https://github.com/kuchris/moremacros/releases/download/0.3.1.0/MoreMacros-0.3.1.0.zip
```

Adding another plugin follows the same process: append one object with a unique `InternalName` and its own release links. No second subscription URL is needed.

## Release automation

Updates to the new `DalamudPlugins` catalogue are currently manual for both plugins. Publish the release ZIP first, then update its matching entry in this repository.

XIV AI Chat's existing tag workflow still publishes its ZIP and updates its older local catalogue for compatibility. It does not update `DalamudPlugins`. MoreMacros releases are currently published manually.

A future workflow in either source repository would need a GitHub App or token with write permission to `DalamudPlugins` to update the shared catalogue. Its default `GITHUB_TOKEN` only has access to its own repository.

## References

- [Shared catalogue](https://raw.githubusercontent.com/kuchris/DalamudPlugins/main/repo.json)
- [XIV AI Chat packer](https://github.com/kuchris/xivaichat/blob/main/tools/pack.ps1)
- [XIV AI Chat release workflow](https://github.com/kuchris/xivaichat/blob/main/.github/workflows/release.yml)
- [GitHub token scope](https://docs.github.com/en/actions/concepts/security/github_token)

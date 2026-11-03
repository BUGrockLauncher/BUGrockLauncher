# Versioning & Release Policy Rule

After EVERY update, bug fix, feature addition, or UI edit (regardless of how small):
1. **Assess the impact**:
   - Patch (`v1.1.x`): Small bug fixes, CSS/layout tweaks, comment hygiene, text changes.
   - Minor (`v1.x.0`): New features, new settings/toggles, architecture refactors, animations, security overhauls.
   - Major (`vX.0.0`): Breaking changes, complete redesigns, new core engine.
2. **Synchronize version across**:
   - `MainWindow.xaml`: The status bar version label `<TextBlock Text="vX.Y.Z" ... />`
   - `BUGrockLauncher.csproj`: `<Version>`, `<AssemblyVersion>`, `<FileVersion>`
   - `README.md` badge and release header.
   - `SECURITY.md` supported version table.
3. **Local Release Hub**:
   - Build/download the `.zip` packages for both Full Standalone and Lite into `E:\antigravity\Proj\BUG`.
   - Name each zip explicitly with the version: `BUGrock-Launcher-vX.Y.Z-Full-Standalone-win-x64.zip` and `BUGrock-Launcher-vX.Y.Z-Lite-win-x64.zip`.
   - Extract both folders next to the zips for instant local testing.
4. **GitHub Releases Hub**:
   - Always update the official GitHub Releases page on `BUGrockLauncher/BUGrockLauncher`.
   - Create or update the Release matching the version tag `vX.Y.Z`.
   - Upload both asset packages (`Full-Standalone` and `Lite`) to the Release so users downloading from the Releases page always get the true latest version.
   - Mark the new release as `Latest Release`.

# Versioning Policy Rule

After EVERY update, bug fix, feature addition, or UI edit (regardless of how small):
1. **Assess the impact**:
   - Patch (`v1.1.x`): Small bug fixes, CSS/layout tweaks, comment hygiene, text changes.
   - Minor (`v1.x.0`): New features, new settings/toggles, architecture refactors, animations, security overhauls.
   - Major (`vX.0.0`): Breaking changes, complete redesigns, new core engine.
2. **Synchronize version across**:
   - `MainWindow.xaml`: The status bar version label `<TextBlock Text="vX.Y.Z" ... />`
   - `BUGrockLauncher.csproj`: `<Version>`, `<AssemblyVersion>`, `<FileVersion>`
   - `README.md` badge and release header.
3. Always make sure compiled binaries will reflect the exact File and Product version in Windows Explorer properties.

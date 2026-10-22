# Repository sync checklist

When syncing this fork with the original repository (`upstream/main`):

1. Preserve fork-specific changes when resolving conflicts. In particular, keep the **App Spectrum** setting in the Settings page and retain its row and behavior alongside upstream settings.
2. Update the project version in `DynamicIsland.csproj` to match the latest version of the original repository. Check the latest upstream release/tag rather than assuming a version.
3. Review any fork-specific UI sizing affected by the sync. The Settings view needs enough height for the extra App Spectrum row (currently 414 px in both `MainWindow.xaml` and `MainWindow.xaml.cs`).
4. Build and verify the changes, then create a local commit for the sync and related fork adjustments.
5. **Do not push** the commit. Leave publishing/pushing to the user.

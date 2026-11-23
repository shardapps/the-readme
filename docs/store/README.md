# Microsoft Store packaging

The Store edition packages the same native WPF app as the GitHub edition with the matching .NET Desktop Runtime included. The English Store build preserves English runtime resources. The Store edition uses Windows file associations and the `readme.exe` App Execution Alias. The GitHub installer manages its optional `readme` PATH entry separately.

The Microsoft Store product is **The Readme - Markdown Editor**. The manifest contains the public package identity assigned to this product. For another publisher’s package, use that product’s identity and publisher display name from Partner Center.

On Windows with the .NET 10 SDK and Windows SDK installed, run:

```powershell
./scripts/Build-StorePackage.ps1
```

The script builds and bundles native x64 and Arm64 packages. GitHub Actions publishes the tested bundle and its checksum as a workflow artifact. Submit the bundle through Partner Center. The package output is for Store submission and Microsoft signs it as part of distribution.

Increase the four-part MSIX version for Store updates and leave the fourth component at zero. Change the GitHub release version separately when publishing refreshed EXE installers. See [listing copy and certification notes](LISTING.md).

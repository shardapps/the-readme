# Microsoft Store packaging

The Store edition runs the same WPF app as the GitHub edition on the .NET Framework built into Windows 11 22H2 and later. The Store edition uses Windows file associations and the `readme.exe` App Execution Alias. The GitHub installer manages its optional `readme` PATH entry separately.

The Microsoft Store product is **The Readme - Markdown Editor**. The manifest contains the public package identity assigned to this product. For another publisher’s package, use that product’s identity and publisher display name from Partner Center.

On Windows with the .NET 10 SDK and Windows SDK installed, run:

```powershell
./scripts/Build-StorePackage.ps1
```

The script builds x64 and Arm64 Windows packages. The x64 package uses Windows' 32-bit CLR to reduce RAM, and the Arm64 package runs natively. Packaging checks the unpacked app files for each architecture and the complete bundle against the 50 MB budget. GitHub Actions publishes the tested bundle and its checksum as a workflow artifact. Submit the bundle through Partner Center so Microsoft can sign it for distribution.

Increase the four-part MSIX version for Store updates and leave the fourth component at zero. Change the GitHub release version separately when publishing refreshed EXE installers. See [listing copy and certification notes](LISTING.md).

# Security policy

Shard Apps supports the latest stable release of The Readme. Install current .NET 10 Windows Desktop Runtime security updates through Microsoft.

## Report a vulnerability

Use a [private GitHub security advisory](https://github.com/shardapps/the-readme/security/advisories/new). Include the app version, Windows version, architecture, impact, and the smallest sample that demonstrates the issue. Keep private files or credentials out of reports and give maintainers time to investigate and coordinate a fix before public disclosure.

## Document handling

The reader limits text files to 2 MB and bounds preview structure and local image decoding. External images use their alternative text. The reader renders Markdown into native controls with no browser engine or active HTML. Activating an HTTP, HTTPS, or email link opens it through Windows. Relative document links apply to supported files inside the document’s folder.

The installer registers its own file types, offers an opt-in per-user PATH addition, and leaves default selection to Windows Settings.

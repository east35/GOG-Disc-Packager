# Windows code signing and reputation

The GitHub release workflow can Authenticode-sign both shipped executables before it creates the ZIP. Obtain a publicly trusted Windows code-signing certificate and add these GitHub Actions repository secrets:

- `WINDOWS_SIGNING_CERTIFICATE_BASE64`: the Base64-encoded contents of an exported `.pfx` certificate
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: the `.pfx` password

The private key must never be committed to this repository. Prefer a hardware- or cloud-backed signing service for long-term use; exportable PFX secrets are supported as a simple starting point.

There is no single Microsoft “antivirus approval.” These are separate systems:

- **Microsoft Defender malware detections:** submit the exact signed release file as a software developer at <https://www.microsoft.com/en-us/wdsi/filesubmission> when a false positive occurs.
- **SmartScreen / “unknown publisher” warnings:** Authenticode-sign every release with a consistent, publicly trusted identity. Reputation builds from signed downloads over time. An EV certificate may establish reputation faster, but it is not a permanent allow-list or a substitute for clean behavior.
- **Other antivirus products:** use each vendor's false-positive submission process, supplying the release URL, SHA-256 checksum, and signature details.

Keep release binaries reproducible, publish SHA-256 checksums, avoid packers/obfuscators, and retain the same signing identity across versions. Unsigned builds will naturally show `Unknown publisher`; a GitHub release by itself does not remove that warning.

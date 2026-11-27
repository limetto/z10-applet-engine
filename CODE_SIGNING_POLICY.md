# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

Only `Z10AppletEngine.exe` from the official releases on the [Releases page](https://github.com/limetto/z10-applet-engine/releases).
It is built by GitHub Actions on GitHub-hosted runners from the source in this repository
([`.github/workflows/release.yml`](.github/workflows/release.yml)). Builds from the regular CI workflow and from forks are not signed.

Third-party libraries bundled into the executable (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)) are not signed with this certificate.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [limetto](https://github.com/limetto) |
| Approvers | [limetto](https://github.com/limetto) |

Every release is approved manually in SignPath before it is signed. All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

It reads hardware sensors, the Windows media playback information and the keyboard locally and talks only to the EVGA Z10 keyboard over USB.

# Code signing policy

> Status: an application to SignPath Foundation is pending. Until it is accepted, releases are not
> signed and the line below describes the planned setup.

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

## What is signed

Only the Windows installer `vox-setup.exe` of tagged releases (`v*`). It is built by GitHub Actions
(`.github/workflows/build.yml`) from the public source in this repository; no binaries are added by
hand. Third-party components (sherpa-onnx, .NET runtime, NAudio, …) are shipped as published by
their authors.

Release tags are protected: they cannot be deleted or moved to another commit, and the `main` branch
cannot be force-pushed or deleted.

## Team roles

- Committers and reviewers: [privatekey7](https://github.com/privatekey7)
- Approvers (each signing request is approved by hand): [privatekey7](https://github.com/privatekey7)

Every team member must use multi-factor authentication for GitHub and SignPath.

## Privacy policy

Vox recognises speech on the computer: audio and text never leave it. The program connects to the
network only to download a speech model (from GitHub Releases of
[k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases)). No telemetry, no accounts.
Full policy: [docs/privacy.md](privacy.md).

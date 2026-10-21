# Code signing policy

> Draft for the SignPath Foundation application (docs/adr/032). The first line becomes true only
> once the project is accepted; link this page from the README and the Releases page then.

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

## What is signed

Only the Windows installer `vox-setup.exe` of tagged releases (`v*`). It is built by GitHub Actions
(`.github/workflows/build.yml`) from the public source in this repository; no binaries are added by
hand. Third-party components (sherpa-onnx, .NET runtime, NAudio, …) are shipped as published by
their authors.

## Team roles

- Committers and reviewers: [privatekey7](https://github.com/privatekey7)
- Approvers (each signing request is approved by hand): [privatekey7](https://github.com/privatekey7)

## Privacy policy

Vox works offline: audio and text never leave the computer. The program connects to the network
only when the user asks it to download a speech model (from GitHub Releases of
[k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases)). No telemetry, no accounts.

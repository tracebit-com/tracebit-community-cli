# Tracebit CLI
![GitHub Release](https://img.shields.io/github/v/release/tracebit-com/tracebit-community-cli?sort=semver)

The Tracebit CLI is the command-line tool for Tracebit Community Edition, which deploys and maintains security canaries, proactively detecting intrusions across your devices and accounts.

The CLI can be used to deploy AWS credentials, SSH keys, browser cookies, website passwords and emails with a single command.

Start by creating a free Tracebit Community Edition account at [community.tracebit.com](https://community.tracebit.com), where you can view and monitor your deployed canaries.

## Getting Started

Download the latest Tracebit CLI installer for your platform from the [GitHub releases](https://github.com/tracebit-com/tracebit-community-cli/releases/latest).

On Windows and macOS, simply run the installer from your downloads.

On Linux, run `bash install-tracebit-linux-<platform>` in the directory where you downloaded the installer.

Log in to the Tracebit CLI by running `tracebit auth`, then deploy all canary types with `tracebit deploy all`. Tracebit will automatically run in the background, making sure your credentials remain up-to-date.

## License

The code in this repository is licensed under the [MIT license](LICENSE).

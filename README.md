# Tracebit Community CLI

![GitHub Release](https://img.shields.io/github/v/release/tracebit-com/tracebit-community-cli?sort=semver)

The command-line tool for [Tracebit Community Edition](https://community.tracebit.com).
It deploys and maintains security canaries across your devices and accounts, and keeps
them fresh so they stay convincing.

Free. MIT licensed.

## Getting started

**1. Create a free account** at [community.tracebit.com](https://community.tracebit.com).
This is where deployed canaries and any alerts appear.

**2. Download the installer** for your platform from
[Releases](https://github.com/tracebit-com/tracebit-community-cli/releases/latest).

**3. Install it.** Run the installer directly on Windows and macOS. On Linux, run
`bash install-tracebit-linux-<platform>` in the directory where you downloaded the installer.

**4. Deploy.**

```bash
tracebit auth
tracebit deploy all
```

Follow the prompts to complete browser and password-manager placement. The installer
sets up background refresh for supported canary types.

## What is a canary?

A canary is a decoy credential or resource with no legitimate production use.
An attempt to use a canary credential gives you a high-confidence signal to investigate.
Credential canaries alert on use; simply reading or copying a local credential file does
not itself trigger an alert.

The CLI deploys five kinds:

| Canary type | Where it sits | What it catches |
|---|---|---|
| AWS credentials | `~/.aws/credentials` | Use of harvested credentials by attackers, agents or processes |
| SSH keys | `~/.ssh` | Attempts to authenticate with the canary key |
| Browser cookies | Browser cookie store | Replay of a stolen canary session |
| Website passwords | Password manager (saved following the CLI prompts) | Attempts to log in with the canary credentials |
| Emails | Your inbox | Interaction with the canary content |

## Why use this

A person's workstation is often the first thing an attacker actually touches. Infostealer malware is built to
scrape exactly the categories above: browser-saved sessions, password manager entries,
SSH keys, cached credentials.

Endpoint tooling is built to recognise the malware's behaviour or signature, which means
a new or well-obfuscated variant can get past it. A canary credential doesn't need to
recognise the malware at all. It only needs to be the kind of thing a credential-harvester
scrapes indiscriminately, and its use afterwards is the alert.

Detection does not rely on monitoring processes or file reads on the workstation. An alert
fires when a canary credential is used, whether from that workstation or another machine.

## Catching AI agents that go out of scope

Canaries can also reveal AI agents reaching beyond what they were asked to do.

An agent does not have to be malicious or compromised to be a problem. It only has to be
resourceful. A coding agent troubleshooting a failing service can look for credentials
to reproduce the issue without a clear boundary on which credentials it is entitled to
use. Reading a local credential file can resemble ordinary developer activity.
Classifying intent is hard. An attempt to use a credential with no legitimate purpose gives
you a concrete event to investigate.

Two real Tracebit detections, anonymised. The second involved a canary deployed in cloud
infrastructure, rather than by this workstation CLI:

**An AI coding agent on a non-engineer's laptop.**
A canary AWS credential fired on the laptop of someone on a commercial team. The employee
was using Claude Code to build an application, and the agent had attempted to use the
canary credentials it found on the machine. Nothing malicious was happening, and that is
the point. What the security team gained was visibility: agent usage outside engineering
that they had no idea was occurring, a threat model that needed updating, and a concrete
example to take to leadership.

**An agent copying credentials out of a container.**
A user troubleshooting a service with an AI agent approved the agent's request to use that
service's AWS credentials locally. The agent then ran a command on the Kubernetes
container, printed its environment variables, copied the canary credentials down to the
workstation, and tried to use them. The approval covered one thing. What the agent did
went further, and the alert fired on use.

**Canaries as a guardrail for AI adoption.**
[Synthesia](https://tracebit.com/customer/synthesia) uses canaries to monitor AI coding
agents for unexpected credential use. Their security team reports that the absence of
alerts gives them confidence in the agents operating in those environments. A quiet
canary means no monitored trigger was observed; it does not prove an agent stayed within
scope or that no compromise occurred.

For the adversarial version of this problem, where the agent is the attacker rather than
an over-eager assistant, see Tracebit's
[Context Bombs research](https://agentic.tracebit.com/context-bombs/).

## What else these canaries catch

- **Infostealer malware.** Credentials and session cookies harvested from a compromised
  workstation and later used.
- **Session-token theft.** A stolen browser session replayed from another machine.
- **Lateral movement preparation.** An attacker with a foothold attempting to use
  a canary SSH key to reach further.
- **Insider collection.** Someone with legitimate access attempting to use credentials
  they have no working reason to use.

The strongest example of what a canary finds that other tooling doesn't came from cloud
infrastructure canaries in Tracebit's commercial platform rather than this CLI, but it is
worth knowing. A customer had run Tracebit for about three months while holding off on one
particularly sensitive cloud account. Three days after finally deploying canaries there,
an application read one. Triage established that the application had been compromised for
more than three years and was being used regularly to enumerate the environment. CSPM and
EDR had both missed it.

## How it works

1. You authenticate the CLI against your free Community Edition account.
2. The CLI requests canaries from the Tracebit API and places AWS credentials and SSH
   keys in local configuration files.
3. It opens your browser to deploy browser cookies, prompts you to save website passwords
   in your password manager, and requests a canary email for your inbox.
4. The installer sets up background refresh for supported canary types. Website passwords
   do not expire and are not automatically refreshed.
5. If a canary credential is used, you get an alert.

## Deploying programmatically

Create an API token and use the [Tracebit API](https://community.tracebit.com/api-docs)
to issue canaries programmatically. See the
[OpenAPI specification](https://community.tracebit.com/openapi.json) for the endpoints.

## Covering more than workstations

This CLI covers devices and accounts. For canaries across cloud accounts, identity
providers, CI/CD pipelines and Kubernetes, see [tracebit.com](https://tracebit.com).

For CI/CD specifically, the
[Tracebit GitHub Action](https://github.com/tracebit-com/tracebit-community-action) is also
free and injects canary credentials into build pipelines, so a compromised dependency or
third-party action can be detected when the planted credentials are used.

Common questions about canaries and deception technology are answered at
[ai.tracebit.com](https://ai.tracebit.com).

## Contributing

Issues and pull requests welcome.

## License

MIT. See [LICENSE](LICENSE).

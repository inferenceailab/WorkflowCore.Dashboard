# Security policy

## Supported versions

The project is in its 0.x series. Security fixes are made on `main` and released as the next 0.x version; older 0.x releases are not patched.

| Version | Supported |
|---|---|
| Latest 0.x release | Yes |
| Older releases | No |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report them privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability** ([direct link](https://github.com/inferenceailab/WorkflowCore.Dashboard/security/advisories/new)). Include:

- what an attacker can do, and what access they need first;
- steps to reproduce, or a proof of concept;
- the affected version or commit, and your setup (authorization, journal, designer).

What to expect:

- an acknowledgement within 5 working days;
- an assessment, and a fix plan if confirmed, within 30 days;
- credit in the release notes, unless you prefer otherwise.

Please give us a reasonable time to release a fix before disclosing the problem publicly.

## Scope

In scope: the code in this repository (the three packages and the embedded UI).

Out of scope:

- Workflow Core itself: report to [danielgerlag/workflow-core](https://github.com/danielgerlag/workflow-core).
- Problems that need the host app to be misconfigured against the guidance in [docs/Security.md](docs/Security.md), for example exposing the dashboard to the internet with `Authorization = _ => true`.
- Anything a user with dashboard access can do by design. Dashboard access is administrative access to your workflows; see the threat model in [docs/Security.md](docs/Security.md).

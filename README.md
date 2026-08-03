# a GitHub Actions Runner fork

This is a fork of the GitHub Actions Runner, the application that runs a job
from a GitHub Actions workflow on a self-hosted machine. See the
[upstream repository](https://github.com/actions/runner) for full documentation.

## about this fork

This fork regularly merges upstream `actions/runner` and changes it into a
dedicated multi-repository self-hosted runner:

- one installation can register with, and listen for jobs from, multiple
  repositories, while running at most one job at a time;
- runner self-update is driven by polling this fork's GitHub releases instead
  of service-directed update messages;
- runner startup and shutdown hooks run alongside upstream's job hooks; and
- releases are versioned as upstream's major version plus 10 (upstream
  `2.336.0` corresponds to `12.336.0` here).

Everything else behaves like upstream unless noted below. The one intentional
regression: the original single-association configuration mode and its config
file shape are not supported, so an existing upstream runner directory must be
reconfigured from scratch with this fork.

### multi-repository runner

`config.sh` / `config.cmd` gains subcommands for managing repository
associations:

```text
./config.sh add-repo --url <url> --token <token> [--name ...] [--labels ...]
./config.sh list-repos
./config.sh remove-repo (--id <id> | --url <url> | --name <name>)
./config.sh remove
```

- `add-repo` registers one repository association. It accepts the same options
  as upstream's `configure` (`--unattended`, `--name`, `--labels`,
  `--runnergroup`, `--work`, `--replace`, `--ephemeral`, ...). Running
  `./config.sh` with no subcommand behaves like `add-repo`. Adding the same
  URL + runner-name combination twice is rejected.
- `list-repos` prints one line per association: local id, runner name, and
  repository URL.
- `remove-repo` removes exactly one local association (and its stored
  credential) matched by `--id`, `--url`, or `--name`; if more than one
  association matches, it refuses and asks for `--id`.
- `remove` is always local-only in this fork: it deletes the local
  configuration files and never unregisters runners on GitHub, regardless of
  `--local`. Remove registrations on GitHub via the repository settings UI/API,
  or one at a time with `remove-repo` plus manual cleanup.

At runtime the listener creates a session per association and long-polls all of
them concurrently. When a job is accepted from one repository, every
registration reports `Busy` until the job finishes, so the same machine is not
offered as available to the other repositories. `--ephemeral` on any
association gives the whole runner run-once behavior.

Configuration is stored in the usual `.runner` and `.credentials` files, but
with a multi-association schema (`associations` array plus per-association
credential entries).

### self-update via release polling

The listener ignores service-directed update messages. Instead, if a file named
`.runner-release-source` in the runner root contains an `owner/repo` slug, the
runner polls `https://api.github.com/repos/<owner/repo>/releases/latest` and
self-updates when the release tag is newer than the running version. Release
packages built by this fork's release workflow include the file automatically,
pointed at the repository that built them; delete the file to disable polling.

- Default poll interval is 1 hour; override in seconds (minimum 60) with
  `GITHUB_ACTIONS_RUNNER_RELEASE_UPDATE_POLL_INTERVAL`.
- The download asset must be named `actions-runner-<platform>-<version>.tar.gz`
  (`.zip` on Windows), and its SHA-256 is verified from the release asset
  digest or from the release notes line naming the asset.

### runner startup and shutdown hooks

In addition to upstream's `ACTIONS_RUNNER_HOOK_JOB_STARTED` /
`ACTIONS_RUNNER_HOOK_JOB_COMPLETED` job hooks, the listener runs:

- `ACTIONS_RUNNER_HOOK_RUNNER_STARTED` — before the runner starts listening for
  jobs. If the script fails, the runner exits with an error instead of
  starting.
- `ACTIONS_RUNNER_HOOK_RUNNER_SHUTDOWN` — when the listener exits, including
  after an error.

Set each variable to the path of a script; the shell is chosen the same way as
for job hooks (`.sh` via bash/sh, `.ps1` via pwsh/powershell). These hooks run
in the listener process outside of any job, so their output goes to the runner
terminal and diagnostic log rather than to a workflow step.

## building

Same as upstream:

```bash
cd src
./dev.sh layout   # dev.cmd on Windows; full build into {root}/_layout
```

`./dev.sh build` refreshes an existing layout and `./dev.sh test` runs the
tests. The dev script bootstraps the required .NET SDK itself.

## license

MIT, same as upstream ([LICENSE](LICENSE)).

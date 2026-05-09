# Multi-Repo Runner Plan

## Goal

Turn this fork into a dedicated multi-repository self-hosted runner while keeping the existing outer contract stable:

- Keep script names stable: `config.sh`, `run.sh`, `svc.sh`, `runsvc.sh`, and Windows equivalents.
- Keep binary names stable where practical, especially `Runner.Listener` and `Runner.Worker`.
- Keep config file names stable where useful, but do not preserve old config shape compatibility.
- Do not support the original single-association runner mode in this fork.

The new runner should let one installation register/poll multiple repo/token associations, while still running at most one job at a time by default.

## Terms

- VRN: virtual runner. One per repo/token association. It represents presence, credentials, session, and polling for that repo.
- RRN: real runner. The single local executor capable of running an accepted job payload.
- Association: one configured GitHub repository registration, credentials, runner id/name, server URLs, labels, and protocol details.
- Execution slot: local mutex/semaphore that controls how many jobs may run at once. Initial value: `1`.

## CLI Shape

`config.sh` remains the user-facing configuration entrypoint, but gains subcommands.

```text
./config.sh add-repo --url ... --token ... [--name ...] [--labels ...]
./config.sh remove-repo (--url ... | --name ... | --id ...)
./config.sh list-repos
./config.sh remove
```

Wrapper mapping:

```text
./config.sh add-repo ...     -> Runner.Listener configure add-repo ...
./config.sh remove-repo ...  -> Runner.Listener configure remove-repo ...
./config.sh list-repos       -> Runner.Listener configure list-repos
./config.sh remove           -> Runner.Listener remove
```

`remove` should always be local-only in this fork, regardless of `--local`. Bulk unregistering remote runner registrations across every associated repo is out of scope and too surprising.

Possible later command:

```text
./config.sh unregister-repo --url ... --token ...
```

Do not include remote unregister behavior in the first pass.

## Script Strategy

Keep scripts mostly stable. The main script change should be `config.sh` / `config.cmd` dispatch:

- pass through top-level `remove`
- prefix `configure` for `add-repo`, `remove-repo`, and `list-repos`
- optionally print usage for bare `./config.sh`

Service scripts should continue to start the same listener binary. The fork changes listener behavior internally.

## Storage Shape

Use the existing config file names if that avoids service/script churn:

- `.runner`: multi-runner settings
- `.credentials`: multi-association credential storage, or an index pointing to per-association credential files
- `.service`: unchanged service marker

No old `.runner` shape compatibility is required.

Suggested model:

```json
{
  "schemaVersion": 1,
  "workFolder": "_work",
  "executionSlots": 1,
  "associations": [
    {
      "id": "stable-local-id",
      "url": "https://github.com/owner/repo",
      "agentId": 123,
      "agentName": "runner-name",
      "poolId": 456,
      "poolName": "default",
      "serverUrl": "...",
      "serverUrlV2": "...",
      "useV2Flow": true,
      "labels": ["..."],
      "disableUpdate": false,
      "ephemeral": false,
      "credentialRef": "stable-local-id"
    }
  ]
}
```

Keep credentials isolated per association internally even if the top-level filename stays `.credentials`.

## Runtime Architecture

`Runner.Listener` becomes a multi-listener coordinator.

High-level loop:

```text
while !shutdown:
    start or maintain one polling task per VRN while the execution slot is open

    when one or more VRNs receive work:
        pick/accept the first VRN that acquires the execution slot
        begin canceling or pausing all losing VRN polls

        if winning job is V2:
            acquire full payload with acquirejob
        else:
            delete V1 queue message
            deserialize full payload from message body

        run payload through the normal RRN path
        wait for loser cleanup as needed
        release execution slot
```

The existing `Runner.Worker` process boundary should remain mostly unchanged. It already accepts an `AgentJobRequestMessage` over the pipe protocol and does not need to know which association produced it.

Preserve or adapt the existing `JobDispatcher` and worker spawn path where possible.

## Status Model

The current single listener reports status through poll requests:

- `Online` while available
- `Busy` after job dispatch begins
- active long poll is canceled when status changes

The multi-runner should preserve this globally:

- when the RRN is idle, VRNs may poll as `Online`
- when the RRN is occupied, all VRNs should stop polling or poll/report `Busy`
- when the RRN finishes, VRNs return to `Online`

This is important to avoid presenting the same physical machine as available to every repo while a job is already running.

## V2 Protocol

V2/broker flow is the clean path.

Commit point:

```text
acquirejob(jobRef)
```

Multi-runner behavior:

- VRNs may poll concurrently.
- A VRN must acquire the local execution slot before calling `acquirejob`.
- Losing VRNs that received `RunnerJobRequest` refs must not call `acquirejob`.
- Best-effort acknowledge should be reviewed; avoid acknowledging before local slot ownership if ack affects scheduling semantics.

## V1 Protocol

V1 is the risky path.

Observed current behavior:

- `GetMessageAsync` returns a full `PipelineAgentJobRequest` payload.
- Payload already includes `RequestId` and `LockedUntil`.
- Normal runner deletes the queue message shortly after dispatch starts.
- Normal runner renews the job request lock before starting `Runner.Worker`.

Hypothesis to test:

- The server may not fully commit assignment until `DeleteMessageAsync`.
- Or, the server may already consider the job assigned when `GetMessageAsync` returns and use delete only as message delivery ack.

Speculative collision behavior:

- A V1 VRN that wins the local execution slot should call `DeleteMessageAsync`, then dispatch.
- A V1 VRN that loses after receiving a job should not delete, should not renew, and should not dispatch.
- This may silently redeliver, or may strand the job until timeout/session cleanup.

This behavior must be tested before relying on it.

## V1 Experiment

Patch a test build to:

1. Configure two normal runners for the same repo.
2. Modify one runner so that when it receives a V1 `PipelineAgentJobRequest`, it does not call `DeleteMessageAsync`, does not renew, and does not dispatch.
3. Queue a job.
4. Observe whether the other runner receives the job:
   - immediately
   - after the long-poll/message visibility timeout
   - after job-request lock expiry
   - only after session expiry
   - never without manual intervention
5. Observe UI/log behavior while this happens.

The result decides whether speculative concurrent V1 polling is acceptable.

## Initial Implementation Slices

1. Adjust CLI parsing:
   - support configure subcommands: `add-repo`, `remove-repo`, `list-repos`
   - make top-level `remove` local-only
   - update `config.sh` / `config.cmd` dispatch

2. Replace configuration model:
   - add multi-association settings type
   - add association credential storage
   - implement add/list/remove association commands

3. Build VRN abstraction:
   - create session per association
   - poll per association
   - expose message/job candidate events to coordinator

4. Build coordinator:
   - global execution slot
   - cancel/pause losers
   - hand winning payload to existing dispatcher/worker path

5. Wire status:
   - global busy/online transitions
   - cancel active polls on transition

6. Run protocol experiments:
   - V2 acquire race behavior
   - V1 non-delete collision behavior

7. Harden:
   - diagnostics for association id/url/message id/request id
   - graceful shutdown
   - service-mode behavior
   - recovery after crash/restart

## Non-Goals For First Pass

- Supporting original single-association config shape.
- Bulk remote unregister across all associations.
- Multiple simultaneous jobs.
- Changing `Runner.Worker` protocol unless forced.
- Reworking service scripts beyond minimal dispatch/config changes.


---
name: warn-long-jobs-detach
enabled: true
event: bash
pattern: ^(?!.*(?:Start-Process|nohup|disown)).*(?:stryker|dotnet test Kartova\.slnx|ci-local\.sh)
action: warn
---

⏳ **Long-running job — launch it detached, don't bound it with a Bash timeout.**

The Bash tool `timeout` maxes at **600000 ms (10 min)**, including `run_in_background`. A kill at that mark is the **TOOL timeout, not an environment limit** — never conclude "the environment can't run this" from a self-set timeout, and never write that into memory or a DoD ledger.

Instead:
- Launch **detached**: PowerShell `Start-Process -FilePath <script> -RedirectStandardOutput <log> -WindowStyle Hidden -PassThru`, or Bash `nohup <cmd> > log 2>&1 & disown`.
- **Poll** the log / report artifact (e.g. `StrykerOutput/**/mutation-report.json`) on later turns, or use `ScheduleWakeup`.

(Stryker mutation / full-solution `dotnet test Kartova.slnx` / `ci-local.sh` genuinely run many minutes–hours here; they finish — just don't sit inside a 10-min tool call.) See memory `project_long_running_jobs_detached`.

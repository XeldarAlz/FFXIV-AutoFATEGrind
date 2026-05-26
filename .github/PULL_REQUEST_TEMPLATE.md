## What

<!-- One or two sentences on what this PR changes. -->

## Why

<!-- The motivating problem, linked issue, or user-visible behavior this fixes. -->

Closes #

## How to test

<!--
Minimum steps a reviewer can run to verify the change. Testing usually means selecting one Shared FATE zone with active FATEs, pressing Run, and watching it complete at least one FATE end-to-end. Make sure the dependencies listed in /afg deps are installed first. For UI-only changes, describe what to click.
-->

## Checklist

- [ ] `dotnet build -c Release` passes
- [ ] Verified in-game on at least one Shared FATE zone in the affected expansion
- [ ] If this changes user-visible behavior, README is updated
- [ ] If this touches the automation loop, relevant `[AutoFateGrind]` log lines make the sequence auditable

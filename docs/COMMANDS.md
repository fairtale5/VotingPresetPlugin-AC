# VotingPresetPlugin – Commands

**Command availability:** In this repo no plugin uses `/yes` or `/no`. If your server runs other plugins, check they don’t register those; if conflicted, use `/vote yes` and `/vote no` and document in config.

---

## Current commands (before changes)

| Command | Aliases | Who | What it does |
|---------|---------|-----|--------------|
| votetrack | vt, votepreset, vp, presetvote, pv | Player | During timer vote: cast vote for option by number (e.g. /vt 1). |
| presetshow | currentpreset, currentrack | Player | Reply with current preset name and folder. |
| presetlist | presetget, presets | Admin | Reply: “List of all presets:” then one line per preset: ` /presetuse {i} - {Name}`. Uses all presets (including AdminOnly). |
| presetstartvote | presetvotestart | Admin | Start the timer vote manually. |
| presetfinishvote | presetvotefinish | Admin | End timer vote now and apply result. |
| presetcancelvote | presetvotecancel | Admin | Cancel the current timer vote. |
| presetextendvote | presetvoteextend | Admin | Add N seconds to timer vote. |
| presetset | presetchange, presetuse, presetupdate | Admin | Switch to preset by index (from list). No vote. |
| presetrandom | (none) | Admin | Switch to a random other preset. No vote. |

Example reply for **/presets** on RR-Street-Pushin (admin):

```
List of all presets:
 /presetuse 0 - Hiroshima Expressway
 /presetuse 1 - Mirandopolis Expressway
 /presetuse 2 - Osaka Expressway
 /presetuse 3 - Shutoku Revival Project
```

---

## New / simplified commands (target)

| Command | Who | What it does |
|---------|-----|--------------|
| **/vt** &lt;number&gt; (votetrack, votepreset, vp, presetvote, pv) | User | During timer vote only: cast vote for option by that number. If no timer vote is open, reply “There is no ongoing track vote.” (no on-demand vote is started). |
| **/preset** &lt;number&gt; | User | When no vote is running: start on-demand vote for preset at that index (votable list). Server broadcasts the vote; players reply /yes or /no. If a vote is already running, reject. |
| **/yes** | User | During on-demand vote: vote yes. Optional aliases: /y. |
| **/no** | User | During on-demand vote: vote no. Optional aliases: /n. |
| **/presets** | Admin | List all presets (votable + admin-only). Same format as current (e.g. number + name). Keep only this name (remove presetlist, presetget). |
| **/presetset** &lt;number&gt; | Admin | Switch to preset by index. No vote. One command only (remove presetchange, presetuse, presetupdate). |
| **/presetcancelvote** / **/presetvotecancel** / **/cancelvote** | Admin | Cancel whatever vote is currently running (timer or on-demand). |

Timer vote controls (admin, unchanged): presetstartvote, presetfinishvote, presetextendvote, presetrandom.

Optional later: **/preset** &lt;name&gt; to start on-demand vote by preset name (case-insensitive, single match). Allow cancel command for non-admin when they initiated the on-demand vote.

---

## Removed / simplified

- **/vt** (and aliases): unchanged for timer vote; only used to cast a vote when a timer vote is open. Do not use /vt to start an on-demand vote.
- **Add:** **/preset** &lt;number&gt; to start an on-demand vote when idle; /yes, /no (and optionally /y, /n) for on-demand vote; presetcancelvote / presetvotecancel / cancelvote (admin) cancel whatever vote is running.
- **Remove aliases for list:** presetlist, presetget → keep only **/presets** for admin.
- **Remove aliases for set:** presetchange, presetuse, presetupdate → keep only **/presetset**.

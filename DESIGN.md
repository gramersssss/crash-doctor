# Crash Doctor — design notes

## What it is
A desktop tool for people who mod Cyberpunk 2077. One job: after a crash, tell you *why* and *what to do*,
in plain language, using the evidence already on the machine (RED4ext, CET, ArchiveXL, redscript, crash reporter,
Windows driver events, settings, mod list). Secondary job: before a crash, list the standing problems.

Audience: PC players with 20–300 mods, Vortex or manual, who currently post "game crashes, help" threads.
The page's single job: the diagnosis. Everything else supports it.

## Direction: a ripperdoc's chart, not a neon dashboard
Modded Cyberpunk tooling defaults to black + neon. This tool reads like a clinical chart instead: light, precise,
paper-and-ink, with red/amber/green used *only* as diagnosis marks, never as decoration. The dark element is the
instrument bezel across the top (game / patch / GPU / driver), which frames the chart like a monitor housing.

### Tokens
Color
- Paper   #EDF0F3  page background (cool clinical, not cream)
- Card    #FFFFFF  panels
- Ink     #10141A  text, bezel background
- Steel   #5B6472  secondary text, rules
- Line    #D6DBE2  hairlines
- Signal  #D93A3A  crash / high risk
- Amber   #E09A1B  suspect / medium risk / attention
- Mint    #1F9D6A  clean / low risk / fixed
- Trace   #2F6FED  links, selection, focus ring

Type (system fonts only; the app is offline)
- Display / labels: Bahnschrift (Windows 10+). Condensed DIN-style; used in SemiCondensed for headings and
  uppercase eyebrows. It reads as signage/instrument labelling, which is the point.
- Body: Segoe UI.
- Data / logs / timestamps: Cascadia Mono, falling back to Consolas.

Scale: 12 / 13 / 15 / 18 / 24 / 34 px. Eyebrows 12px uppercase, letter-spacing .08em.

Layout
```
+-------------------------------------------------------------------------------+
| BEZEL  Cyberpunk 2077 2.31 · Steam · RTX 2070 Laptop 8 GB · driver 616.56 · 160 mods · [Scan] |
+----------+--------------------------------------------------------------------+
| Diagnosis|  DIAGNOSIS                                   Sep 6, 01:29 · 19 min |
| Sessions |  "Script error in Sexual encounters Clouds nude patch, 5 s        |
| Health   |   before the crash. Not a GPU fault."                              |
| Mods     |  confidence ● ● ● ○     what to do → [3 actions]                   |
| Settings |  EVIDENCE  T-5s  …   T-0  …                                        |
| Report   |--------------------------------------------------------------------|
|          |  SESSION TRACE  (signature)                                        |
|          |  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬|   ▬▬▬▬▬▬|  ▬▬|  ▬▬▬▬▬▬▬▬| |
|          |  Sep 2 21:26 3h52 clean   Sep 3 01:18 7m GPU  …                    |
+----------+--------------------------------------------------------------------+
```

Signature element: the **session trace**. Every play session is a bar on a telemetry strip; length = duration,
end cap = how it ended (mint = clean exit, red = GPU fault, amber = script/mod crash, grey = unknown).
Hovering a bar shows the verdict. It is drawn in on load (0.6 s, respects reduced motion). It encodes something
true: the pattern of *when* it crashes is the diagnosis half the time (e.g. "every 3 minutes" vs "after 4 hours").

Structure that carries information: evidence is listed as T-minus seconds before the crash, because proximity
in time *is* the evidence. Nothing is numbered unless it is a sequence.

Motion: the trace draws in; verdict card fades up 120 ms after. Nothing else moves. Hover states only.

Copy: verdicts are one sentence, active voice, name the mod, say what to do. Errors say what went wrong and what
to try. Empty states invite the scan.

## How it reasons — the diagnostic philosophy

This is the part that decides whether the tool is worth installing. It was written after a full day of using the
engine against a real, badly-behaved 168-mod install, during which the author's own analysis reached the wrong
conclusion three times in a row. Those three failures are the source of every rule below.

### The honest claim
Crash Doctor does **not** promise to fix every crash. It promises to do, reliably and in ten seconds, the evidence
work that people currently do badly over days: read what is actually on the machine, sort many crashes into the
few distinct problems they really are, and rule things out. Naming the culprit is a bonus, not the contract.

Any tool promising to solve every crash is lying, and this audience can tell.

### The three failures, and what they taught
1. **"It's video memory."** Twelve crashes had the card at 91–102 %. Real problem, genuinely fixed — and the
   crashes continued. *A strong correlation that explains many cases can still not be the cause of the one in front
   of you.*
2. **"It's mod world-streaming load."** The crash actually happened while streaming had gone almost silent.
   *The number being measured was not the number that mattered.*
3. **"It's the version-mismatched trainer."** Loaded in 24 of 30 crash dumps. The user then played with it and did
   not crash. *Correlation across a set is not causation for a member of it.*

Every wrong answer came from reasoning about **what happened just before one crash**. Every right answer came from
**comparing many sessions against each other**. That is the whole lesson, and the architecture encodes it.

### Five layers, and only the bottom two may be confident
**1 — Facts.** Read, never inferred: faulting instruction address and exception code (from the minidump), loaded
modules, video memory used vs total, player position, tracked quest, engine OOM flag, framework versions, mod
inventory, settings, the crash screenshot. No other tool in this ecosystem opens the minidump at all.

**2 — Grouping.** Cluster crashes by faulting address. The headline is *"you do not have 30 crashes, you have 3
problems"*, which is a completely different conversation from "the game crashes sometimes". Deterministic, and on
the real install it separated 30 crashes into 4 families instantly.

**3 — Elimination.** For every suspect, ask whether the same evidence also appears in sessions that ended
**cleanly**. If it does, it is background noise and must be demoted and labelled as such — never ranked high.
This is the layer whose absence caused all three failures above. It requires at least two clean sessions before it
is allowed to conclude anything; with no clean baseline it must say so rather than assume.

**4 — Known fixes, keyed on crash signature.** This is the answer to "but every crash is different". The *fixes*
differ; the *evidence gathering* does not. So evidence collection is universal code, and fixes are a lookup table
keyed on a stable signature (faulting address + game version). The table starts with what has been verified
first-hand and grows.

**5 — Verdict, with an honest confidence and a real "not enough evidence" state.** Always accompanied by what was
*ruled out* and why. Four eliminated suspects is genuinely valuable to someone who is stuck.

### Guided bisect
The reliable way to find a cause nobody has seen before, including one with no log signature at all. The app knows
the mod list, the session history and the crash signatures, so it can drive the experiment instead of leaving the
user to guess: disable this group, play, report whether the signature changed, restore, halve, repeat. Mechanical,
honest, and the thing that would have solved the real case fastest.

### Rules the copy must obey
- Never name a mod the evidence does not support. "Its script errored 48 s before the crash" is not a cause.
- State correlation as correlation, with the counts visible ("in 24 of 30 crashes"), never as a conclusion.
- Anything appearing in clean sessions is noise. Say the word "noise" and show the clean-session count.
- "I don't know yet, here is what is ruled out and here is the next test" is a valid, respectable verdict.
- Confidence must be able to reach zero.

## Quality floor
Responsive to 900 px (the window can be small). Visible focus ring (Trace). Reduced-motion respected.
Keyboard: rail is a list of buttons; sessions are focusable rows.

## Things tried / rejected
- Dark + neon: rejected, it is the default for anything Cyberpunk and reads as fan art, not an instrument.
- Numbered steps in the diagnosis: rejected unless the actions truly must happen in order (they usually don't).

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

## Quality floor
Responsive to 900 px (the window can be small). Visible focus ring (Trace). Reduced-motion respected.
Keyboard: rail is a list of buttons; sessions are focusable rows.

## Things tried / rejected
- Dark + neon: rejected, it is the default for anything Cyberpunk and reads as fan art, not an instrument.
- Numbered steps in the diagnosis: rejected unless the actions truly must happen in order (they usually don't).

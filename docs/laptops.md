# Laptops

Ohman talks to the HP BIOS mailbox (`hpqBIntM`), which every OMEN and Victus laptop exposes. What differs
between models is which commands the firmware answers and which bytes each performance mode wants. That is
the only model-specific thing in the app, and it lives in one table: [`src/Platform.cs`](../src/Platform.cs).

## The two states

Every OMEN and Victus laptop is **supported**, and there is no "unsupported" list. A board nobody has ever seen
still runs: Ohman reads the firmware's own system-design data, works out which generation it is, and drives it
with the mode bytes documented for that generation. Power, GPU and lighting appear only where the firmware
answers for them. If it answers with nothing usable, Ohman stays read-only and says so in the window.

A model becomes **verified** once somebody has run the checklist below on that exact board and every control
did what it says. Its settings are then fixed rather than worked out at run time.

## Verified

| Model | Board | Verified on | Notes |
|---|---|---|---|
| OMEN Transcend 14 (2024) | `8C58` | 2026-09-12 | Checklist run on `8C58`: Core Ultra 9 185H + RTX 4070. Modes, fans, power gain, GPU power and four-zone lighting all confirmed against OMEN Gaming Hub 1101.2608. Graphics switching writes correctly but the restart it needs was never taken, so it is unconfirmed. `8E41` was listed here as the same machine. It is not: an owner reported a 2025 14-fb1xxx with a Core Ultra 9 285H and an RTX 5070, same firmware generation but different silicon. It is driven by the generic path and its own firmware answers; see [#2](https://github.com/P4R1H/ohman/issues/2). |
| OMEN 15 / 17 (2019–2021) | `8748` | 2026-09-13 | Owner ran it on their own machine and confirmed the controls work. Driven with the mode bytes for its firmware generation, which is what `hp-wmi` does for this board too. |
| OMEN 15z-en100 (2021) | `88D2` | 2026-09-13 | Owner confirmed it works. Listed in `hp-wmi`, driven with the mode bytes for its firmware generation. |
| OMEN 16-wf0xxx (2023) | `8BAB` | 2026-09-14 | Full checklist run by an owner: modes, max fan, manual, curve, auto, power gain, GPU power, graphics switching and the keyboard all confirmed, and the fans returned to normal after exiting. A second owner on a 16-wf0120TX (i5-13500HX + RTX 4050, BIOS F.29) confirmed fans and lighting independently, with OMEN Gaming Hub and Light Studio both uninstalled. |
| OMEN 17-db1xxx | `8E10` | 2026-09-16 | Ryzen AI 7 350, BIOS F.20. Owner ran the checklist: modes, max fan, manual, curve, auto, power gain, GPU power, graphics switching and the fans returning after exit. The keyboard is white only, so the colour rows have nothing to change. |
| Victus 16-r0xxx | `8BBE` | 2026-09-16 | Core i5-13500H, BIOS F.31. Owner filed a report with nothing wrong on it. |
| OMEN 16-k0006nl | `8A4C` | 2026-09-16 | Owner confirmed the controls work. |
| OMEN 16-xd0xxx | `8BCD` | 2026-09-15 | Ryzen 7 7840HS + Radeon 780M, BIOS F.31. Owner ran the checklist: modes, max fan, manual, curve, power gain, GPU power, graphics switching, the four-zone keyboard and all four effects, and the fans returned to normal after exiting. |
| OMEN 17-ck2013nl | `8BAD` | 2026-09-15 | Owner confirmed modes, max fan, manual, curve, auto, GPU power and that the fans return after exiting. The keyboard lighting does not work on this machine and is still open. A timed thermal profile board in `hp-wmi`, so the fans respond to a mode change even where the power limits may not. |
| OMEN 15 / 17 | `8787` | 2026-09-15 | Owner confirmed the controls work. |
| Victus 16-d1xxx | `8A26` | 2026-09-14 | Core i7-12700H + RTX 3050, BIOS F.22. Owner ran it as a full OMEN Gaming Hub replacement and confirmed fan control, the custom curve and the modes all work with OGH uninstalled. Reports thermal policy v0, so Eco and Balanced send the same byte and only Performance differs. |
| Victus 16-e0xxx | `88EE` | 2026-09-13 | Owner confirmed it works. Not in `hp-wmi`; driven from the firmware's own answers. |
| Victus 15-fa2405TX | `8DCF` | 2026-09-13 | Owner confirmed modes, fans and the keyboard backlight all work, with no HP software installed. Driven from the firmware's own answers. |
| HyperX OMEN 15-gb0xxx (2026) | `8EEC` | 2026-09-13 | Ryzen 5 240 + RTX 5050, BIOS F.15. Max fan, manual, curve, auto, power gain, GPU power, graphics and four-zone lighting all confirmed by the owner. Reports **thermal policy v0**, so Eco and Balanced send the same byte and only Performance differs — see below. Base TDP 45 W, OMEN key 29/8613. |

See [Verifying your laptop](#verifying-your-laptop).

## Supported

Board ids come from the Linux `hp-wmi` driver's tables plus HP's own service documentation. Grouping is by
firmware generation, which is what decides the bytes; marketing names are approximate because HP
reuses a board across several SKUs.

### OMEN Transcend

| Model | Board ids |
|---|---|
| OMEN Transcend 16 (2023–2025) | `8BB3`, `8C3B`, `8C4D` |

### OMEN 16 and OMEN MAX

| Model | Board ids |
|---|---|
| OMEN 16 (2021–2022) | `8A42`, `8A43` |
| OMEN 16 (2023–2025) | `8BAA`, `8BCA`, `8C76`, `8C77`, `8C78`, `8D24`, `8D26`, `8D2F`, `8E35` |
| OMEN MAX 16 (2025) | `8D41`, `8D87` |

### OMEN 15 and OMEN 17

| Model | Board ids |
|---|---|
| OMEN 15 (2019, 15-dc / 15-dh) | `8574`, `8600` |
| OMEN 17 (2019, 17-cb0) | `8603` |
| OMEN 15 (2020) | `8A15` |
| OMEN 15 / 17 (2021) | `8BAD` |
| OMEN 15 / 17, 2018–2021 generations | `84DA`, `84DB`, `84DC`, `8572`, `8573`, `8575`, `8601`, `8602`, `8604`, `8605`, `8606`, `8607`, `860A`, `8746`, `8747`, `8748`, `8749`, `874A`, `8786`, `8787`, `8788`, `878A`, `878B`, `878C`, `87B5`, `886B`, `886C`, `88C8`, `88CB`, `88D1`, `88D2`, `88F4`, `88F5`, `88F6`, `88F7`, `88FD`, `88FE`, `88FF`, `8900`, `8901`, `8902`, `8912`, `8917`, `8918`, `8949`, `894A`, `89EB` |

### Victus

Victus firmware uses different mode bytes from OMEN: `0x00` default, `0x01` performance, `0x03` quiet. Ohman
writes those bytes only for the boards the Linux `hp-wmi` driver names, because that is the only place they
are written down:

| Model | Board ids | Mode bytes |
|---|---|---|
| Victus 16 (2021–2023) | `88F8`, `8A25` | from `hp-wmi` |
| Victus 15-fb0xxx | `8A3D` | from `hp-wmi`, no quiet mode |
| Victus 16 S / R (2023–2024) | `8B2F`, `8BD4`, `8BD5`, `8C99`, `8C9C` | from `hp-wmi`, no quiet mode |
| Victus 15, other models | `88D9`, `88DA`, `8A3E`, `8C2F`, `8C30`, `8C3F`, `8D07`, `8DCD`, `8E5E` | from the firmware |

"from the firmware" is the same path every unlisted OMEN takes: Ohman asks the board which firmware generation
it is and drives it with that generation's bytes. It works, and nobody has yet confirmed on a Victus 15 that
the quiet mode is the byte we think it is. The kernel's own Victus 16-r0xxx entry takes OMEN bytes despite the
name, so the badge on the lid does not decide this — only a readback from the machine does.

`8A3D` is the exception and shows how this gets settled: it is a Victus 15-fb0xxx, and it reached `hp-wmi`
in July 2026 because one owner ran the fan table query on theirs and sent the readback to the kernel list.
Board `8C2F` is reported to be shared between the 15" and 16" chassis, so even a board id is not always one
machine.

### A note on v0 boards

The v0 byte set has one default (`0x00`), one performance (`0x01`) and one cool (`0x02`) — there is no
separate low-power byte, so **Eco and Balanced are the same byte** and only Performance behaves differently.
This is how the firmware is documented, not a bug, and `hp-wmi` maps low power to default on these boards for
the same reason. Turning on **quieter Eco** in Settings makes Eco send `0x02` instead, which does differ.

Board `8EEC` is a 2026 machine that still reports v0, so this is not only an old-laptop concern.

## Finding your board id

Open Ohman and look at the top right of **Settings**, or run:

```
(Get-CimInstance Win32_BaseBoard).Product
```

## Verifying your laptop

This takes about five minutes and it is the most useful thing you can contribute.

1. **Modes.** Switch Eco → Balanced → Performance. The fans should audibly change within a few seconds, and
   the chassis reading on the Home page should drift. If nothing changes on any mode, say so.
2. **Fans.** Try Max (both fans should go loud), then Manual (set 50%, the reading should land near half of
   your maximum), then Curve (drag a point up, the fans should follow within one ramp delay), then Auto.
3. **Power gain.** If the row is there, move it to +15 W and check that nothing throttles or resets.
4. **GPU power.** If the row is there, try each of Base / Boost / Max.
5. **Graphics.** If your machine offers Discrete or iGPU only, *do not* switch it unless you are willing to
   restart. If you do, confirm it comes back correctly.
6. **Lighting.** If your keyboard lights, check that the zones in the app match the zones on the keyboard,
   and that Static, Breathe, Cycle and Wave all do something.
7. **Exit.** Close Ohman and confirm the fans return to the firmware's own behaviour within two minutes and
   nothing is stuck.

Then run:

```
tools\support-info.cmd
```

and open a [Verify my laptop](https://github.com/P4R1H/Ohman/issues/new?template=verify-laptop.yml) issue
with `tools\support-info.txt` attached and a line per step above. The file contains the model, board id, BIOS
version, the firmware's system-design bytes, the fan table and the OMEN key event id. No personal data, no
serial numbers.

## If your keyboard lights per key

HP's firmware lighting interface answers on these boards and drives nothing at all, so Ohman uses the
keyboard's own HID lighting interface instead. That part is written against a published standard but is
**unverified**: nobody working on Ohman has a per-key machine. Run this, which writes nothing and changes
nothing:

```
Ohman.exe --lamps
```

and open an issue with what it prints. It reports how many lamps your keyboard has, where each one is, and
which key each one lights. That is everything needed to confirm the feature works.

## If a control is wrong on your model

Open a [New laptop support](https://github.com/P4R1H/Ohman/issues/new?template=new-laptop-support.yml) issue
with the same file, what OMEN Gaming Hub shows for the same control, and, if you can get it, OGH's own
background log from a session where you clicked every mode:

```
%LOCALAPPDATA%\Packages\AD2F1837.OMENCommandCenter_v10z8vjag6ke6\LocalCache\Local\HPOMEN\
```

That log is what made the Transcend 14 profile exact, and it is the fastest route to an exact profile for any
other model.

## Adding a laptop in code

A laptop is one entry in `Platforms.Known` in [`src/Platform.cs`](../src/Platform.cs):

```csharp
new PlatformProfile {
    Name = "HP OMEN 16 (2023, 16-wf0xxx)",
    Boards = new[] { "8BAA" },
    Notes  = "Verified 2026-09-10 against OGH 1101.x logs."
}
```

Everything else (fan ranges, mode bytes, which features exist) either has a sane default on
`PlatformProfile` or is read from the firmware at run time. Override only what the evidence says is
different, and put the evidence in `Notes`.

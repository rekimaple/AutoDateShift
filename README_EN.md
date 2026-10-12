# AutoDateShift

<img src="app.png" width="96" align="right">

[简体中文](README.md) | **English**

A Windows utility that automatically shifts the system date according to preset rules, with a GUI manager and a background service. It is built for software that schedules tasks **by day of week only** (bells, timetables, duty rosters), keeping that scheduling correct across statutory holiday shifts.

---

## Features

- **Two rule modes**
  - By weekday: shift a given date to the nearest matching weekday (closest within ±3 days) — for software that schedules by day of week (bells, timetables, duty rosters)
  - By date: shift a given date to any target date, with no limit on the offset
- **Automatic restore**: dates with no rule revert to the real date, with hours, minutes and seconds left untouched
- **Two-process design**: a GUI manager plus a windowless background service (60-second self-check by default); rules take effect only while the service is running
- **Start with Windows**: implemented as a scheduled task (SYSTEM account, highest privileges), so it runs even when nobody is logged in; power-loss restart policy is selectable (remember last state / always on / stay off)
- **Time calibration**: NTP (5 common servers preset) or manual; works offline on an intranet
- **Audit trail**: one line per event (`[time] [source] message`), errors include the version and reason, split into monthly files, filterable and exportable
- **Bilingual UI**: switch between Chinese and English instantly; the layout adapts to text length
- **Single instance**: launching again activates the existing window; closing it minimizes to the tray

## Requirements

- Windows 7 / Server 2008 R2 or later (including Server 2012/2016/2019/2022 and Windows 10/11)
- No runtime installation needed: uses the built-in .NET Framework 4.0
- Changing the system time requires administrator rights; the background service runs under the SYSTEM account

## Build

No Visual Studio required — just run `build.bat`, which invokes the .NET 4.0 compiler shipped with Windows, to produce a single-file `AutoDateShift.exe`.

After building, run the self-test (all 19 assertions should pass):

```
AutoDateShift.exe --selftest --datadir %TEMP%\ads_test
```

## Usage

1. Right-click `AutoDateShift.exe` → Run as administrator
2. On the **Status** tab, start the background service ("Auto-start at boot (this tool)" is recommended)
3. On the **Rules** tab, enter your rules and save

See `部署说明.md` (Chinese) for deployment details.

> Started without administrator rights, the app enters **read-only mode** (rules, status and logs are viewable but not editable). This is intended behaviour; tick "Restart as administrator" on the Status tab to elevate.

## Uninstall

1. Stop the background service and clear auto-start at boot
2. Exit the app, then delete the exe and the data folder (default `C:\ProgramData\AutoDateShift`)
3. If the date is still shifted, correct the system date manually

If the exe is already gone: run `应急卸载.bat` as administrator to remove the scheduled task and any processes.

## Security

- Never connects out or uploads data on its own (only one request to the selected time server during an NTP calibration)
- Rules, state and logs are plain local JSON / text files that can be audited directly
- UI icons are drawn in code as vectors; no external resource files

## Disclaimer

Parts of this software were generated with AI assistance. Changing the system time may affect other software that depends on the system date (licences, certificates, scheduled tasks, file timestamps, and so on). Evaluate before use; all consequences are borne by the user.

## License

Copyright (c) 2026 rekimaple. Released under the [MIT License](LICENSE).

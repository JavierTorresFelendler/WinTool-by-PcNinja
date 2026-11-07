# MSI Specification

## Package

| Item | Value |
| --- | --- |
| Product name | WinTool by PcNinja |
| Scope | Per-machine, x64 |
| Install folder | `%ProgramFiles%\PcNinja\WinTool` |
| UpgradeCode | New GUID, fixed forever after the first release |
| File name | Always `WinTool-by-PcNinja.msi`, without a version (D-018) |
| Newer version | Major upgrade; the old version is replaced in place |
| Same version | Maintenance: the setup offers **Repair** or **Remove** |
| Older version | Blocked, with a message that a newer version is installed |
| Toolset | WiX (same major version as WinUpdate V2) |

## Interactive setup pages

1. Welcome
2. License agreement
3. Install folder
4. Shortcuts: Start menu (default on), desktop (default off)
5. **Windows Update schedule**: same options as the WinUpdate V2 schedule page,
   off unless checked
6. Ready to install
7. Finish, with the option **Launch WinTool**

The WiX wizard uses `packaging/msi/PcNinjaDialog.bmp` (493x312) and
`PcNinjaBanner.bmp` (493x58), both 24-bit BMPs composed from the approved
`src/WinTool.App/Assets/pcninja-mascot.png`. The dialog keeps artwork in the
left 164 pixels; the banner keeps the left title area light and puts the
mascot on the right. These replace wizard artwork; the existing shortcut
and installed-product icon remains `PcNinja.ico`.

## Languages

The MSI is English, with Hebrew (LCID 1037) and Spanish (3082) embedded as language
transforms (`packaging/msi/Build-Msi.ps1`, texts in `WinTool.<culture>.wxl`). When WinTool
starts the installer (the universal EXE, a self-update, the schedule offer), it passes
`TRANSFORMS=:1037` or `TRANSFORMS=:3082` to match its own language. Running the MSI
directly opens it in English; IT can choose a language the same way:

```
msiexec /i WinTool-by-PcNinja.msi TRANSFORMS=:1037
```

The dialogs keep a left-to-right layout in Hebrew. The license text is English.

## Public properties (silent install)

Every option on the setup pages has a property. Defaults match the
interactive pages. The schedule names follow WinUpdate V2 with a new prefix.

| Property | Values | Default | Replaces V2 property |
| --- | --- | --- | --- |
| `INSTALLFOLDER` | path | see above | `INSTALLFOLDER` |
| `WINTOOL_START_MENU_SHORTCUT` | `0`/`1` | `1` | — |
| `WINTOOL_DESKTOP_SHORTCUT` | `0`/`1` | `0` | — |
| `WINTOOL_LAUNCH` | `0`/`1` | `0` | — |
| `WINTOOL_APP_UPDATE_CHECK` | `0`/`1` | `1` | — |
| `WINTOOL_LANGUAGE` | `auto`/`en`/`he`/`es` | `auto` | — |
| `WINTOOL_ENABLE_SCHEDULE` | `0`/`1` | `0` | `PCNINJA_ENABLE_SCHEDULE` |
| `WINTOOL_FREQUENCY` | `Daily`/`Weekly`/`Monthly`/`Startup` | `Monthly` | `PCNINJA_FREQUENCY` |
| `WINTOOL_TIME` | `HH:mm` | `03:00` | `PCNINJA_TIME` |
| `WINTOOL_DAY_OF_WEEK` | `Sunday` … `Saturday` | — | `PCNINJA_DAY_OF_WEEK` |
| `WINTOOL_MONTHLY_DAY` | `1`–`28` | `15` | `PCNINJA_MONTHLY_DAY` |
| `WINTOOL_RUN_AT_STARTUP` | `0`/`1` | `0` | `PCNINJA_RUN_AT_STARTUP` |
| `WINTOOL_STARTUP_DELAY` | minutes | `5` | `PCNINJA_STARTUP_DELAY` |
| `WINTOOL_RUN_IF_MISSED` | `0`/`1` | `1` | `PCNINJA_RUN_IF_MISSED` |
| `WINTOOL_WAKE_TO_RUN` | `0`/`1` | `0` | `PCNINJA_WAKE_TO_RUN` |
| `WINTOOL_ALLOW_FIRMWARE` | `0`/`1` | `0` | `PCNINJA_ALLOW_FIRMWARE` |
| `WINTOOL_REBOOT_PROMPT` | `0`/`1` | `1` | `PCNINJA_REBOOT_PROMPT` |
| `WINTOOL_ENABLE_AUTORETRY` | `0`/`1` | `1` | `PCNINJA_ENABLE_AUTORETRY` |
| `WINTOOL_RETRY_INITIAL_DELAY` | minutes | `5` | `PCNINJA_RETRY_INITIAL_DELAY` |
| `WINTOOL_RETRY_MAX_ATTEMPTS` | count | `3` | `PCNINJA_RETRY_MAX_ATTEMPTS` |
| `WINTOOL_RETRY_BACKOFF` | multiplier | `2` | `PCNINJA_RETRY_BACKOFF` |
| `WINTOOL_MINIMUM_COOLDOWN` | minutes | `5` | `PCNINJA_MINIMUM_COOLDOWN` |

Invalid values stop a silent install with a logged error (MSI error 1603 and a
clear line in the MSI log). They are never replaced silently.

### Examples

```bat
msiexec /i WinTool-by-PcNinja.msi /qn /norestart /l*v "%TEMP%\WinTool-install.log"
```

```bat
msiexec /i WinTool-by-PcNinja.msi /qn /norestart ^
  WINTOOL_ENABLE_SCHEDULE=1 WINTOOL_FREQUENCY=Weekly ^
  WINTOOL_DAY_OF_WEEK=Sunday WINTOOL_TIME=03:00 WINTOOL_WAKE_TO_RUN=1
```

```bat
msiexec /x WinTool-by-PcNinja.msi /qn
```

Repair (same version, silent):

```bat
msiexec /fa WinTool-by-PcNinja.msi /qn /norestart
```

Exit codes scripts should expect: `0` success, `3010` success with restart
required, `1618` another installation is running (try again later, D-013),
`1603` failed (see the log).

## How the schedule is applied

The MSI does not create the scheduled task itself. After the files are
installed, a custom action runs `WinTool-by-PcNinja.exe schedule set ...` with the
property values. The MSI, the GUI and the CLI therefore share one code path for
schedules. Uninstall runs `WinTool-by-PcNinja.exe schedule remove` before removing files.

## Command line

See the CLI section in [ARCHITECTURE.md](ARCHITECTURE.md). The full option
reference is written with the CLI implementation.

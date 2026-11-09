# GameScreen

<img src="assets/GameScreen.svg" width="64" height="64" alt="GameScreen icon">

GameScreen routes PC games to a display you choose. Keep your work monitor as the Windows main display and use a second monitor for gaming.

## Getting started

1. In Windows Display settings, choose **Extend these displays**.
2. Open GameScreen, select your **Gaming display**, and click **Apply**.
3. Launch a game in **Windowed** or **Borderless** mode. GameScreen moves the window to your selected display after it opens.

GameScreen detects Steam and Epic installations automatically. For other games, choose **Add game…** and select the game executable. Use **Refresh** after installing new games or changing your monitor setup.

Closing the window keeps GameScreen running in the system tray. Use **Pause** to suspend automatic routing or **Quit** from the tray menu to exit.

### Fullscreen games

Some fullscreen games choose the Windows main display at startup. Click **Start fullscreen mode** before launching one of these games. GameScreen temporarily makes the selected gaming display the Windows main display. After playing, click **End fullscreen mode** to restore the previous main display.

## Portable and installed editions

| Edition | How to start | Settings |
| --- | --- | --- |
| Portable ZIP | Extract the entire ZIP to a writable folder. Open `GameScreen.exe` with `portable.flag` beside it. | `Data\<computer name>\settings.json` in the extracted folder |
| Windows x64 MSI | Run the installer, then launch **GameScreen** from the Start menu. Uninstall it from **Settings → Apps → Installed apps**. | `%LOCALAPPDATA%\Game Monitor\settings.json` |

Both editions request administrator access at launch so GameScreen can move windows belonging to games that also run as administrator.

## Build and package

On 64-bit Windows, run the build script in PowerShell from the repository root. It uses the Windows .NET Framework C# compiler and writes `build\GameScreen.exe`.

```powershell
.\build.ps1
```

To build the MSI and portable ZIP, install or extract the [WiX 3 build tools](https://github.com/wixtoolset/wix3/releases/tag/wix3141rtm) and pass the directory containing `candle.exe` and `light.exe`.

```powershell
.\package.ps1 -WixBin 'C:\Tools\WiX'
```

The release packages are written to `dist\`.

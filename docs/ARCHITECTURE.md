# PULSE · Xbox 连发助手

Windows x64 / .NET 8 WPF application. Eight Xbox buttons have independent hold-to-repeat or press-to-toggle modes, timing and saved settings. Xbox axes, D-pad, menu and other buttons pass through. XInput reads physical input, ViGEmBus emits a virtual Xbox 360 controller, HidHide temporarily hides the physical device from games.

Each card's mode button switches between 长按连发 (hold to repeat, release to stop) and 单击连发 (press once to latch, press again to stop). Enable the adjacent switch to arm that button. Only physical down edges change a latch; holding a button cannot repeatedly toggle it. A cancellation press is consumed. Latched triggers emit full-strength presses so shallow activation edges still cross game thresholds; hold-mode triggers retain physical analog strength. Pause, disable, mode changes and session end clear active latches; rate edits retain a latch and restart its pulse phase. Modes and intervals persist, while active latches never persist across restarts. Closing to tray preserves the running session. Older settings default to hold mode.

## Build

Version 1.2.0 adds an immediate 中文 / English selector in the sidebar. It translates static labels, button models, validation, application errors and the tray menu. `AppSettings.Language` persists the selection; older settings default to Chinese. `Assets/Strings.json` contains the two languages and is embedded in the executable. `Loc.cs` supplies WPF dynamic resources and formatted model text. Changing language updates presentation without updating the live bridge settings.

Use .NET SDK 8.0.425 or compatible .NET 8 SDK. The relative `../deps/vigem-client/lib/netstandard2.0/Nefarius.ViGEm.Client.dll` is the official Nefarius.ViGEm.Client 1.21.256 NuGet assembly. Its embedded native DLL is included by the single-file publish.

```powershell
dotnet publish src/XboxRapidFire.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/publish
```

Requires .NET 8 Desktop Runtime x64. To enable the install button, put the original signed installers in `publish/Drivers`: `ViGEmBus_1.22.0_x64_x86_arm64.exe`, `HidHide_1.5.230_x64.exe`. The tool verifies their Authenticode signature and publisher immediately before requesting elevation. Sources and licensing are listed in the delivered guide and `Licenses` directory. ViGEm is retired; this app uses its final official bus release.

## Architecture

- `MainWindow.xaml`: vector artwork, dark theme, accessible switches and interval cards.
- `MainWindow.xaml.cs`: window lifecycle, commands, tray icon, global pause, live feedback.
- `ButtonModel.cs`: bound per-button state and validated interval editing.
- `Engine.cs`: pure time-based button/trigger state machine and persistence.
- `Native.cs`: XInput structures and interop.
- `Bridge.cs`: dedicated input thread, virtual report output, rumble and disconnection handling.
- `Hiding.cs`: reversible per-session HidHide transaction, journal and watchdog.
- `HidHideDriver.cs`: native HidHide control-device IOCTLs using UTF-16 lists.
- `ControllerDevices.cs`: Unicode SetupAPI/HID/Configuration Manager enumeration.
- `VirtualPad.cs`: waits for the newly added XInput slot without trusting the driver's reported player number.
- `DriverSetup.cs`: official installer validation and interactive installation.
- `SelfTest.cs`, `IntegrationTest.cs`: state-machine, UI and optional installed-driver checks.

The live input thread receives immutable settings snapshots. It never touches UI controls or configuration files. Shutdown stops input, restores only this session's HidHide additions, removes the virtual target, and stops physical vibration. Parent-container hiding follows the official HidHide GUI eligibility rule: HID/XUSB class and no unrelated children. The app requires one physical XInput controller to prevent slot/identity ambiguity. It preserves other applications' HidHide configuration.

The title-bar close button and minimize button hide the window to the system tray without stopping input. Double-click the tray icon or choose its open command to restore the window. Only the tray menu's exit command performs application shutdown and controller restoration. If an operation is in progress, a tray exit request waits for it to finish before restoring input and exiting.

## Verification commands

```powershell
./artifacts/publish/XboxRapidFire.exe --self-test C:/temp/logic.json
./artifacts/publish/XboxRapidFire.exe --ui-test C:/temp/ui
./artifacts/publish/XboxRapidFire.exe --diagnose C:/temp/diagnostic.json
./artifacts/publish/XboxRapidFire.exe --native-probe C:/temp/native.json
./artifacts/publish/XboxRapidFire.exe --hid-probe C:/temp/hid.json
```

Installed-driver tests (run with other remappers and this UI closed):

```powershell
./artifacts/publish/XboxRapidFire.exe --virtual-test C:/temp/virtual.json
./artifacts/publish/XboxRapidFire.exe --bridge-test C:/temp/bridge
./artifacts/publish/XboxRapidFire.exe --ui-driver-test C:/temp/ui-driver
```

UI test mode does not persist test settings or install drivers. Native probe loads the packaged client and attempts a bus connection without adding devices. Virtual test creates a temporary virtual pad and checks emitted reports through XInput. Bridge test temporarily hides the physical pad, runs passthrough/rapid-fire for four seconds, then restores the session. A separate non-whitelisted observer is required to verify game-side visibility and crash recovery.

Physical and virtual slots are distinguished by comparing XInput connections before and after creating the target. Launch games after starting rapid fire, because existing physical-device handles can remain open.

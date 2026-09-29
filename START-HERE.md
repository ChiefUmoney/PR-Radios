# PR Radios — prototype 0.2

A Windows 11 overlay for TeamSpeak 3, with realistic handheld and mobile radio housings based on your references. The radio is a separate transparent desktop window. TeamSpeak carries the voice; Roblox runs independently.

## Start

1. Close TeamSpeak 3 if it is running.
2. Double-click **PR-Radios-Bridge.ts3_plugin** and use TeamSpeak's package installer. If Windows asks which app to use, choose `C:\Program Files\TeamSpeak 3 Client\package_inst.exe`.
3. Open TeamSpeak 3. Enable **PR Radios Bridge** under **Tools → Options → Addons** if needed, and connect to your server.
4. Double-click **PR-Radios.exe**. The screen changes from OFFLINE to your current channel when the bridge connects.
5. In TeamSpeak's Capture settings, select **Push-To-Talk** and assign a key you can use while playing. The overlay's ? button opens help; it does not transmit audio itself.
6. Use windowed or borderless Roblox, position the radio, and press **Ctrl+Alt+R** to let mouse clicks pass through to the game.

Keep the folder together. No administrator privileges, game modification, or network listener are required for the overlay. The app and plugin are locally built and unsigned.

## Controls

| Control | Action |
| --- | --- |
| RADIO / Ctrl+Alt+S | Switch handheld and mobile layouts |
| LOCK / Ctrl+Alt+R | Toggle click-through mode |
| Ctrl+Alt+H | Show or hide the radio |
| Drag the mobile top casing or handheld speaker grille / battery seam | Move the radio |
| Toolbar − / + | Resize the radio |
| Left knob, mouse wheel | Adjust TeamSpeak playback volume by 2 dB |
| Right knob, mouse wheel | Previous or next channel |
| First key below the screen / click right knob | Choose a channel |
| Mobile five keys below screen | Channel menu, previous, next, lock, switch model |
| Handheld three keys below screen | Channel menu, volume down, volume up |
| Direction pad left / right | Previous / next channel |
| Direction pad up / down | Volume up / down |
| Handheld keys 1–9 | Join the corresponding channel in the channel list |
| Handheld *, 0, # | Volume down, lock, volume up |
| Mobile green power button | Hide the overlay; Ctrl+Alt+H restores it |
| Orange button | Setup help (not an emergency call) |
| ? | Open setup help |
| × | Exit |
| System tray icon | Show, hide, unlock, switch layout, or exit |

Your radio choice, size, and position are saved to preferences.json when you exit. It always starts unlocked. The toolbar hides when click-through mode is enabled; Ctrl+Alt+R restores it. Red/orange indicates your own transmission, green indicates another speaker, and standby means connected without detected speech. Updates normally arrive within about half a second.

## What this version includes

- Two realistic radio skins, a transparent always-on-top window, and click-through mode.
- A native 64-bit TeamSpeak 3 API 26 plugin, suitable for the installed TS3 3.6.2 API.
- Live channel name, active speaker, transmit/receive status, channel switching, and playback volume.
- A local named pipe restricted to the Windows account running the plugin. No Roblox hooks or process access.
- Offline status when the bridge or server is unavailable.

## Prototype limits

- The generated housings closely follow the references; they are not exact replicas. The original PR logo is retained in the app icon, tray icon, and toolbar.
- Push-to-talk uses TeamSpeak's native settings. There is no independent overlay microphone mode.
- No scanning, zones, emergency calling, radio voice filter, or automatic vehicle/team detection yet.
- Channel switching obeys server permissions. Join password-protected channels through TeamSpeak. The overlay lists at most 128 channels, in the order provided by TS3, without a nested channel tree.
- Everyone you talk to must be connected through TeamSpeak. This does not integrate with ERLC's built-in radio.
- Only the currently selected TeamSpeak server tab is controlled. Channel/volume commands are rejected if that tab changes before the bridge processes them.
- Exclusive fullscreen is not a supported target. Roblox compatibility still needs a live gameplay check on your setup.

## Verification

Both binaries compiled successfully. Both layouts were rendered and visually inspected. A native test host with simulated TeamSpeak callbacks checked bridge loading, API version, JSON escaping, repeated connections, channel changes, stale-server rejection, volume bounds, talking indicators, error reporting, disconnect state, plugin re-enabling, and shutdown. The real overlay client was also checked against that test bridge. A short desktop smoke test checked that the overlay opens, polls, switches layouts, toggles click-through, and closes.

This has **not** been tested on a live TeamSpeak voice connection or during ERLC gameplay. The test host simulates TeamSpeak; it does not prove end-to-end voice or game compatibility.

## Troubleshooting

- **OFFLINE / Enable TS3 bridge:** check that TeamSpeak is running and PR Radios Bridge is enabled. The app and TeamSpeak must run under the same Windows account.
- **Join TS3 server:** the bridge is working, but the selected TeamSpeak tab is not connected.
- **Radio hidden or locked:** use Ctrl+Alt+H / Ctrl+Alt+R, or the radio's icon in the Windows system tray. Windows may place it under the tray's up-arrow menu.
- **Hotkey unavailable:** another program may own it. Use the toolbar or tray commands.
- **Cannot change channel:** inspect TeamSpeak for password or permission messages.
- **Not visible over Roblox:** try windowed mode first.

## Source and rebuilding

Source is included in `source/`. Run `Build.ps1` with Windows PowerShell and Visual Studio 2022 C++ Build Tools installed. The desktop app uses the Windows .NET Framework WPF runtime; the bridge includes its C++ runtime statically.

The `source/sdk/` headers come from the [official TeamSpeak 3 plugin SDK](https://github.com/teamspeak/ts3client-pluginsdk). Original notices remain in those files. This is an independent prototype, not an official Motorola, TeamSpeak, Roblox, or ERLC product.

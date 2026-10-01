![HyperTrunk](docs/hypertrunk-banner.svg)

# HyperTrunk

**Connect a Windows PC to several VLANs at once through a single Ethernet cable, using Hyper-V.**

## [⬇️ Download the latest version](../../releases/latest)

*Download the `.zip` file from the page above.*

HyperTrunk is a small Windows desktop app that turns a physical network adapter into a VLAN trunk. For each VLAN you need, it creates a virtual network adapter tagged with the right VLAN ID. Windows then sees one network card per VLAN, each with its own IP address.

It is built around **Luminex GigaCore** networks (common in live events and AV installations): VLANs are picked from the Luminex groups, with the same names and colors as on the switches. The group list can be customized, so it also works with any other VLAN-capable switch.

Everything HyperTrunk does relies on standard Hyper-V PowerShell commands (`New-VMSwitch`, `Add-VMNetworkAdapter`, `Set-VMNetworkAdapterVlan`…). The app just makes them quick and safe to use, and shows every command it runs.

> [!NOTE]
> **About this project:** I am not a developer. About 90% of the work on HyperTrunk (code, architecture, bug fixes and this documentation) was done by **Claude**, the AI assistant made by Anthropic, through Claude Code. My part was the idea, the requirements, and testing on real hardware.

## Features

- Lists the physical Ethernet adapters and shows which ones already have a vSwitch
- Creates or removes a Hyper-V external vSwitch on an adapter in one click
- Adds a VLAN by picking a Luminex group: the VLAN ID and color are filled in automatically
- Optionally assigns a static IPv4 address and subnet mask to each VLAN
- Group 1 ("Manage") is created untagged, the other groups are tagged (access mode)
- Customizable group list (names, VLAN IDs, colors) through a JSON file
- Built-in console showing every PowerShell command and its output, with a Copy button
- Log files for troubleshooting

## Requirements

- **Windows 10 or 11**, 64-bit, in an edition that includes Hyper-V (Pro, Enterprise or Education — *not* Home)
- **Hyper-V enabled**: *Turn Windows features on or off* → check **Hyper-V**, then restart. HyperTrunk shows a shortcut to this window if Hyper-V is missing.
- **Administrator rights**: the app asks for them on launch
- A wired Ethernet adapter, connected to a switch port configured as a **trunk** carrying the VLANs you need

## Installation

1. Download the latest `HyperTrunk-<version>-win-x64.zip` from the [latest release](../../releases/latest) page.
2. Extract it to a folder of your choice.
3. Run `HyperTrunk.exe` and accept the administrator prompt.

No installation is needed. The .NET runtime is included in the zip.

## Usage

1. **Select an adapter** in *Network Adapters*, then click **Create vSwitch**.
   The adapter's network connection drops for a few seconds while Hyper-V takes over the card. The PC's own connection then goes through a new `vEthernet (HyperTrunk_<adapter>)` adapter.
2. Click **Add VLAN**, give it a name, pick a **Luminex group**, and optionally enter an **IP address** and **subnet mask**.
   A new `vEthernet (<VLAN name>)` adapter appears in Windows, tagged with the group's VLAN ID.
3. Repeat for every VLAN you need.
4. To clean up, use **Remove VLAN**, or **Remove vSwitch** to remove the switch and all its VLANs at once.

> [!WARNING]
> Creating a vSwitch rebinds the physical adapter. If you are connected remotely through this adapter, you may lose the connection.
> Some consumer network drivers handle Hyper-V binding badly: on one test machine, a Killer E2500 adapter stopped working until a reboot. Use a reliable adapter (Intel, Realtek…) where possible.

## Configuration

### Luminex groups

On first launch, HyperTrunk writes a `luminex-groups.json` file next to `HyperTrunk.exe` with the default groups (Group02 = VLAN 200, Group03 = VLAN 300, … Group20 = VLAN 2000). Edit it to rename groups, change VLAN IDs or colors, then restart the app:

```json
[
  { "Name": "Manage (Untagged)", "VlanId": 1,   "ColorHex": "#325197" },
  { "Name": "Group02",           "VlanId": 200, "ColorHex": "#E80000" },
  { "Name": "Audio Dante",       "VlanId": 210, "ColorHex": "#00AA55" }
]
```

If the file is invalid or empty, HyperTrunk falls back to the default groups and writes a warning to the console.

### Logs

Logs are written to `%LocalAppData%\HyperTrunk\logs\`, one file per day. Include them when reporting a bug.

## Building from source

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download) (or Visual Studio 2026 with the *.NET desktop development* workload).

```powershell
git clone https://github.com/GUPS-ECLAIROLOGUE/HyperTrunk.git
cd HyperTrunk
dotnet build
dotnet test
```

To produce a standalone build like the one in Releases:

```powershell
dotnet publish HyperTrunk/HyperTrunk.csproj -c Release -r win-x64 --self-contained true -o publish
```

The version number is the build date (`yyyy.MM.dd`) and is shown in the window title.

### Project structure

| Folder | Content |
| --- | --- |
| `HyperTrunk/Services` | Hyper-V and network logic: runs the PowerShell commands (`HyperVService`) |
| `HyperTrunk/ViewModels` | UI state and commands (MVVM) |
| `HyperTrunk/Views` | WPF windows |
| `HyperTrunk/Models` | Data types, including the default Luminex groups |
| `HyperTrunk/Logging` | File and console logging |
| `HyperTrunk.Tests` | xUnit tests (tests that need a real Hyper-V machine are skipped by default) |

## License

HyperTrunk is free software, distributed under the [GNU General Public License v3.0](LICENSE).

Luminex and GigaCore are trademarks of their respective owners. HyperTrunk is an independent project, not affiliated with or endorsed by Luminex.

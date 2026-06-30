# Livewire Browser

> [Русский](README-ru.md) | English

A Windows GUI application for browsing Axia/Telos Livewire AoIP networks. The application discovers audio devices (nodes, Element, Fusion, codecs, telephone hybrids), displays IP addresses, models, and lists of Livewire inputs (Sources), and lets you listen to any selected channel on a selected audio output with True Peak, M/S/I LUFS level meters and a phasescope.

![](screenshot1.png)

### Solution Structure

```
src/
  LivewireBrowser.Core/      device discovery, models, cache, settings, logging
  LivewireBrowser.Audio/     RTP reception, PCM decoding, WASAPI playback
  LivewireBrowser.App/       WPF UI (MVVM)
tests/
  LivewireBrowser.Core.Tests/
```

### Stack

- .NET 8, WPF (MVVM, CommunityToolkit.Mvvm)
- NAudio — RTP capture, WASAPI output
- YamlDotNet — device cache and settings

### Building and Running

Requires [.NET 8 SDK x64](https://dotnet.microsoft.com/en-us/download/visual-studio-sdks).

```powershell
git clone https://github.com/ykmn/LivewireExplorer
cd .\LivewireExplorer
dotnet build LivewireBrowser.sln
dotnet run --project src\LivewireBrowser.App
dotnet test tests\LivewireBrowser.Core.Tests
```

### Publishing to `release/`

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

The script builds a self-contained single-file executable (`win-x64`, no installed .NET required) and places `LivewireBrowser.exe` in the [release/](release/) folder at the project root, clearing it first.

## How Device Discovery Works

There is no openly documented UDP broadcast discovery protocol for real Livewire networks. Instead, the application uses:

1. **LWRP (Livewire Routing Protocol) — TCP port 93.** This is a documented line-based Telnet-like control protocol supported by every Axia/Telos Livewire device. The scanner ([LwrpScanner.cs](src/LivewireBrowser.Core/Discovery/LwrpScanner.cs)) iterates over all hosts in the selected network interface's subnet, attempts a TCP/93 connection, and if the device responds, sends `VER` (device info) and `SRC` (list of outgoing channels with names and multicast addresses) commands.
2. **SAP/SDP announcements (RFC 2974/4566), port 9875** — an additional, more standard mechanism for AES67/Livewire+ streams. Only works if SAP Announcements are explicitly enabled on the nodes (Synchronization menu) — disabled by default on many devices.
3. **Livewire Advertisement Protocol, UDP multicast 239.192.255.3:4001** — a native Axia Livewire protocol confirmed in the official Axia IP-Audio Driver manual (Rev 2.10): every LW device and the IP-Audio Driver itself periodically announce their channels to this multicast group (plus port 4000 for requesting an immediate full announcement). Unlike the TCP/93 subnet sweep, this is **passive listening without address enumeration** — noticeably faster on large networks.

LWRP, SAP, and Advertisement results are merged in [NetworkScanner.cs](src/LivewireBrowser.Core/Discovery/NetworkScanner.cs). Devices that the TCP/93 sweep missed but are visible via Advertisement are still included in the list.

### Advertisement Packet Format (reverse-engineered from real captures)

The binary TLV format was reverse-engineered from real packet captures (port 4001) and is not officially documented. Parsing is implemented in [AdvertisementParser.cs](src/LivewireBrowser.Core/Discovery/AdvertisementParser.cs):

- Record = `tag (4 ASCII bytes) + type (1 byte) + value`. Value width depends on type: `0x00`→1 byte (nested field count: `NEST`, `INDI`), `0x01`→4 bytes (`INIP`=device IP; `PSID`/`FSID`/`BSID`=source ID — `FSID` is literally the multicast address `239.192.x.y`/`239.193.x.y` as 4 raw bytes), `0x03`→string (2-byte length + ASCII, null-padded), `0x06`/`0x08`→2 bytes, `0x07`→1 byte.
- Two packet types: a short periodic beacon (~87 bytes, `ADVT=2`, no channel list) and a full announcement (`ADVT=1`, hundreds to thousands of bytes) with the device name (`ATRN`) and source blocks tagged with dynamic labels `S001`, `S002`, ... — each containing: `PSID` (LW channel number, lower 2 bytes), `FSID` (multicast address), `PSNM` (channel name — same field name as in LWRP `SRC`).
- Confirmed on real data: `PSID=0x5295` (21141) for a channel with `FSID=EF C0 52 95` (239.192.82.149) — `82*256+149=21141`, matching the LW number decoding formula already used by the application.

Confirmed `VER` response fields on real networks:
- `LWRP` (protocol version),
- `DEVN` (device name/type:
  - simple nodes return service strings like `lwwd`/`LiveAES`/`LiveIO`,
  - Engine/Fusion/ZIP ONE/Sound4Streamer/Nx12 return the literal product type),
- `NSRC`/`NDST`/`NGPI`/`NGPO` (source/destination/GPI/GPO counts),
- xNodes additionally have `PRODUCT`+`MODEL` (e.g. `"Axia xNode"` + `"Analog 4x4 I/O"`).
- The `SRC` response is formatted as a `BEGIN ... SRC <num> PSNM:"..." RTPA:"a.b.c.d" ... END` block; the device name/model is constructed from these fields by priority: `PRODUCT+MODEL` → `DEVN` → `"LWRP device"`.

The official **Livewire Routing Protocol v2.0.2** specification (Telos Systems Corp.) confirms that the `IP` command (without parameters) returns the device's current network configuration including `hostname:<name>` (DNS-compatible, max 12 characters) — the name configured on the device itself, not guessed externally. Device name priority in the application:
1. `hostname` from the `IP` response →
2. for the "IP Drivers" category — the actual computer name via **NetBIOS Name Service** (see below) →
3. reverse DNS (PTR lookup) →
4. `PRODUCT+MODEL`/`DEVN` from `VER`.

### Computer Name for IP-Audio Driver

The Axia IP-Audio Driver is software running on a Windows machine, not a standalone hardware device, and often has no PTR record in the network's DNS. For the "IP Drivers" category, the application additionally queries the host via **NetBIOS Name Service** (UDP/137, "Node Status" request — the same mechanism as `nbtstat -A <ip>`) and retrieves the actual Windows computer name directly from the machine, independently of DNS configuration ([NetBiosNameResolver.cs](src/LivewireBrowser.Core/Network/NetBiosNameResolver.cs)).

Confirmed on a real devices: the `IP` command response arrives as **two separate lines** in **different formats** — `IP ADDR:"172.22.0.36" LINK:1` (colon-separated, like `VER`/`SRC`) and separately `IP hostname air-pc2` (no colon at all, just a space-separated word-value pair). `ExtractIpHostname` handles both variants.

**IP-Audio Driver (software driver on a PC).** `DEVN:"lwwd"` is not described in the official specification, but is consistently found on real networks with source/destination/GPI/GPO counts exactly matching the documented specifications of the Axia IP-Audio Driver product (1/4/8/24-channel versions) — this is a heuristic based on indirect evidence, not a protocol guarantee. Such devices are placed in a separate "IP Drivers" category with the model "Axia IP-Audio Driver"; the host computer name is typically obtained via the same `hostname` field from the `IP` command (the driver usually reports the PC name as the device network name) or via reverse DNS.

> [!IMPORTANT]
> **Disclaimer.** The exact semantics of other LWRP attributes not yet encountered are not officially documented by Telos Alliance — the above was verified from real network logs and the official specification, but differences may exist in other firmware generations or devices. If something is classified incorrectly, check the log file (it records raw `VER`/`SRC`/`IP` response strings at Debug level) and adjust the parser settings in [LwrpScanner.cs](src/LivewireBrowser.Core/Discovery/LwrpScanner.cs).

If neither the `hostname` from `IP` nor reverse DNS resolves (e.g. on a network without configured hostnames per device), the application appends the IP address in parentheses after the device's general self-description (e.g. `LiveIO (172.22.0.27)`) — otherwise multiple identical nodes would appear indistinguishable in the list.

In the UI, each device displays separately: **"Device Class"** (category from the list below — nodes, codecs, Engine, etc.), **"Name"** (priority: configured hostname → reverse DNS → IP suffix), and **"Device Model"** (e.g. for a digital node — `AES/EBU 8x8 I/O`, for a telephone hybrid — `Nx12`).

> [!NOTE]+
> The Livewire driver for Linux is not currently considered.

> [!TIP]+
> The `/` key can hide the last digits of IP addresses in the list.
> In device class sort mode, the `+` key expands all categories and discovered devices, and the `-` key collapses them.

## Stopping Scan and Rescan

![](screenshot2.png)

The "Scan" button changes to "Stop" during a full scan, and likewise the "Rescan" button on a specific device — pressing again cancels the corresponding operation (`CancellationToken` threaded through the entire chain `NetworkScanner.FullScanAsync`/`RescanDeviceAsync` → `LwrpScanner` → `SapListener`).

The status bar throughout the scan shows exactly what is happening (current polled IP and request stage — TCP/93, VER, SRC, IP) as well as scan start/stop/completion messages.

> [!WARNING]
> - LWRP subnet scanning is limited to 4096 hosts (see `NetworkInterfaceHelper.GetHostAddresses`) — for very large subnets (e.g. /16) only part of the address space will be scanned; using an interface with a /24 or smaller subnet is recommended.
> - LWRP protocol fields were reconstructed unofficially by parsing device responses (see disclaimer above) — the correctness of name/model/channel parsing will depend on the specific device firmware.

## Listening to a Channel

Clicking a channel number connects the application to the channel's multicast group and receives RTP directly (`RtpReceiver` in [LivewireBrowser.Audio](src/LivewireBrowser.Audio)), decodes linear PCM (24-bit, big-endian, 48 kHz — confirmed from real packet captures), and outputs through WASAPI (`AudioPlaybackEngine`) to the audio device selected in the bottom panel, with a level meter (`LevelMeter`) and volume slider.

`RtpReceiver` parses the RTP header per RFC 3550 not as a fixed 12 bytes, but accounting for variable length: the CSRC identifier list and optional extension header can shift the payload start, and the padding bit can add padding bytes at the end of the packet (`RtpReceiver.TryGetPayloadRange`). Without this, header tail/padding bytes were fed into the decoder as audio samples and audible as white noise.

### LW Channel Number

Livewire encodes its 16-bit channel number in the multicast address: `239.192.<hi>.<lo>` ⇔ channel `hi*256+lo`. The application computes this automatically (`LwrpScanner.ComputeLwNumber`) and displays it alongside the source's index on the device (`ChannelNumber`) and the channel name.

### Loudness Meters

On the right side of the main window are four vertical meters: **True Peak**, **Momentary**, **Short Term**, and **Integrated Loudness**, each with a current value and a clickable maximum (clicking resets only that maximum). Implemented in [LoudnessMeter.cs](src/LivewireBrowser.Audio/LoudnessMeter.cs):
- K-weighting (ITU-R BS.1770-4, pre-filter + RLB filter) over 100 ms gating blocks,
- Momentary/Short Term — sliding average over 400 ms/3000 ms,
- Integrated — proper two-pass gating per EBU R128 (absolute gate -70 LUFS, relative gate — "mean minus 10 LU"),
- True Peak — approximated via 4× linear interpolation upsampling, not full polyphase reconstruction per ITU-R BS.1770 Annex 2 — not a certified measurement, but catches most inter-sample peaks that a standard sample-domain peak meter misses.

The phasescope has an input gain knob x1...x12 — for display purposes only.

## Cache, Settings, and Logs

All working files are stored **next to the `.exe`**, not in `%LOCALAPPDATA%` or Program Files — this lets the application run as portable and avoids requiring write access to system folders:

- `data/devices.yaml` — cache of the last discovered device and channel list (loaded at startup, updated after each scan). All fields are saved: device class, model, name, IP, and for each channel — index, LW number, name, multicast address/port, and active flag (`IsActive`).
- `data/settings.yaml` — settings: selected Livewire network interface, automatic full rescan interval, last volume, main window size (saved on close, restored on next launch).
- `logs/app-YYYYMMDD.log` — debug log (new file each day). Records: sent/received packets, parse results, network and audio errors, unhandled UI exceptions. The log folder can be opened directly from the app: **Settings → "Open Log Folder"**. Log level is configurable (`AppSettings.LogLevel`, default `Warn` for new profiles) — switch to `Debug` before diagnosing an issue, otherwise details such as packet parsing will not be written.

You can edit these files manually following YAML syntax, or delete them — they will be recreated automatically.

## Application Settings

Opened via the **"Settings"** button in the main window:

![](screenshot3.png)

- **Livewire Network Interface** — must be selected before scanning; without this, traffic goes through the default interface and typically does not reach the Livewire network on multi-homed machines. The list and "Refresh" button use `NetworkInterfaceHelper`.
- **Discovery Mode** (`DiscoveryMode`): "Sweep" — TCP/93 sweep of all subnet hosts only (`LwrpScanner.ScanSubnetAsync`); "Sweep + Announcements" — same plus passive SAP/Advertisement multicast listening (default, previous behavior); "Announcements Only" — passive listening only, no subnet sweep (faster, but will miss devices that do not announce themselves).
- **Automatic full rescan interval** (in minutes, 0 — disabled).
- **Language** (`English`/`Russian`, default `English`) — switches without restarting the application.
- **Log level** (`Debug`/`Info`/`Warn`/`Error`, default `Warn`).
- **Clear device cache.**
- **Open log folder.**

Main window size and position are saved separately from the settings dialog — automatically on application close, restored on next launch with a check that the saved position is still on a visible screen.

## Localization

The interface is fully available in English or Russian (see the Language setting above). All strings are in [Strings.en.xaml](src/LivewireBrowser.App/Localization/Strings.en.xaml) / [Strings.ru.xaml](src/LivewireBrowser.App/Localization/Strings.ru.xaml); static XAML labels read them via `{DynamicResource}` (updated live on language change), ViewModel code uses `Loc.Get(key)`. Switching is instant, no window restart needed.

## Sorting and Search

- **Sorting** (dropdown next to the "Scan" button): by device class (groups + by name within group), by IP address (numeric octet comparison via [IpAddressUtil.cs](src/LivewireBrowser.Core/Network/IpAddressUtil.cs), not lexicographic — `172.22.0.9` comes before `172.22.0.10`), by name. **Default: by IP address.** Applied immediately and recalculated after each scan/rescan.

  When sorting by device class, discovered devices are grouped by category ([DeviceClassifier.cs](src/LivewireBrowser.Core/Discovery/DeviceClassifier.cs)): analog nodes, digital nodes, Engine, Fusion, codecs, telephone hybrids, IP drivers, and other. Classification is based on substrings in the `VER` response (device model/type); the matching list is easy to extend.

- **Search** (the "Search:" field below the sort panel) — live substring search across the class, name, model, and IP address of any device (`DeviceViewModel.MatchesSearch`). The ◀/▶ buttons cycle through matches; the current match is highlighted in yellow and automatically scrolled into view.

## License

The application is distributed under the GPL v3 license. As the author, I open-source the code and permit:

- using the application for personal, educational, and commercial purposes;
- modifying the application.

License restrictions:

- redistribution of the original application requires a link to this repository;
- modifications must be published as open source — the project cannot be made closed-source;
- any derivative works also become GPL v3.

## Donate

RU: [https://yoomoney.ru/to/4100135835863](https://yoomoney.ru/to/4100135835863)
International: `0x0EDe142a3D9f1D556562e112A9bC34c220158C9A` *(ETH, BNB, Poly, Arbitrum, Base)*

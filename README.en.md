<div align="center">

<img src="assets/app-icon.png" width="96" />

[中文](README.md) · **English**

# 🚀 NetSpeedTest

**A professional Windows desktop network speed test tool · Open source & free**

[![CI](https://github.com/lingyingaojue/NetSpeedTest/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/lingyingaojue/NetSpeedTest/actions/workflows/ci.yml)
[![Release](https://img.shields.io/badge/release-v1.4.4-green?style=flat-square)](https://github.com/lingyingaojue/NetSpeedTest/releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square&logo=windows)](https://github.com/lingyingaojue/NetSpeedTest)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![Downloads](https://img.shields.io/github/downloads/lingyingaojue/NetSpeedTest/total?style=flat-square&color=blue)](https://github.com/lingyingaojue/NetSpeedTest/releases)
[![Stars](https://img.shields.io/github/stars/lingyingaojue/NetSpeedTest?style=flat-square&color=yellow)](https://github.com/lingyingaojue/NetSpeedTest/stargazers)
[![AI-DeepSeek](https://img.shields.io/badge/AI-DeepSeek-4D6BFE?style=flat-square)](https://www.deepseek.com)

[Download](#-download) · [Website](https://lingyingaojue.github.io/NetSpeedTest/) · [Toolbox](#-network-toolbox) · [Changelog](#-changelog)

</div>

![Main window](assets/screenshot.png)

> The complete main window in dark theme: embedded sidebar pages, custom title bar and live rate curves.

## 📖 Table of Contents

- [✨ Core Features](#-core-features)
- [🖼️ Screenshots](#️-screenshots)
- [🌐 Web Console & REST API](#-web-console--rest-api)
- [🧰 Network Toolbox](#-network-toolbox)
- [⌨️ Shortcuts & Parameters](#️-shortcuts--parameters)
- [🗂️ Data & Configuration Files](#️-data--configuration-files)
- [🏗️ Tech Stack](#️-tech-stack)
- [🚀 Quick Start](#-quick-start)
- [📥 Download](#-download)
- [📝 Changelog](#-changelog)
- [❓ FAQ](#-faq)
- [🤝 Contributing](#-contributing)
- [🤖 How This Project Is Built & AI Collaboration](#-how-this-project-is-built--ai-collaboration)
- [💬 Feedback & Contact](#-feedback--contact)
- [📄 License](#-license)

## ✨ Core Features

### Smart Speed Engine

- **128 threads of concurrent HTTP GET/POST**, up to 1024 in adaptive mode
- **URL rotation with health scoring**; failing nodes cool down and get down-weighted
- **Emergency drop compensation** adds threads and corrects the result
- **Ramp-up excluded from the average**, 3 s sliding-window smoothing
- **Gradual startup** so the link is never saturated instantly

### Multi-NIC Parallel Testing

- **Test several NICs at once**, each bound to its own source IP
- **Default-gateway adapters are selected automatically**
- **Download/upload charts switch between combined and per-NIC curves**
- **One failing adapter never blocks the others**
- **Virtual adapters are filtered by default** and can be allowed in Settings

### Full-Chain Latency Probing

- **Five-tier fallback**: UDP → ICMP → TCP 443 → HTTPS HEAD → HTTP HEAD
- **Internet latency, jitter and LAN latency share one probe chain**
- **Jitter is smoothed with a sliding-window standard deviation**
- **Packet loss is probed in 5-packet batches** with an automatic UDP recheck
- **Every adapter shows its own latency, jitter and packet loss**

### NIC-Level Precision Metering

- **Differentially derived from IPv4Statistics**, free of HTTP overhead
- **Kbps / Mbps / Gbps switch automatically**
- **Dual LiveCharts2 line charts**, 200 ms sampling, 500-point window
- **Nine-detail info card per adapter plus active thread counts**
- **Draggable chart splitter and animated mode transitions**

### Professional Interaction

- **Custom title bar with embedded sidebar pages**; switching is locked while testing
- **Dark/light themes and Chinese/English switch instantly and persist**
- **System-tray residency with state sync and balloon notifications**
- **GitHub OTA updates checked automatically at startup**
- **Shortcuts**: Enter / Esc / Ctrl+D / Ctrl+U / Ctrl+B

### History & Profile Management

- **SQLite history with paging**, a statistics bar and CSV export
- **8 built-in CDN nodes plus profile import/export** (HBCS-compatible JSON)
- **Atomic settings writes**, so an interrupted save never loses configuration
- **Built-in web server and REST API for remote control**
- **Multi-NIC export carries the aggregate result**, per-adapter details and BatchId

## 🖼️ Screenshots

![Settings page](assets/screenshots/settings.png)

> Settings: threads, test duration and compensation parameters are all tunable; the light theme, embedded pages and web console share the same design language.

- Dark / light themes switch instantly and persist, using the same palette as the website
- History, profiles, settings, web server, more, protocols and about are all embedded sidebar pages
- The web console fits desktop and mobile browsers for live rate curves and history

## 🌐 Web Console & REST API

A built-in HTTP server (enabled by default, implemented with `HttpListener`, no extra runtime component to install) — a phone or another PC on the same subnet can watch and drive the speed test from a browser.

| Capability | Description |
|:--|:--|
| 🖥️ **Web console** | Open `http://<local-IP>:8080` to watch live rate curves, adapter status and history |
| 🔀 **Automatic port fallback** | If the default port is taken the next free port is probed; the actual port is persisted and echoed in the UI |
| ⚙️ **Custom port** | A fixed port can be pinned on the Web Server page (1024 – 65535) |
| 🔐 **Session token** | Non-loopback writes must carry the random `X-NST-Token` (256-bit) generated at startup; loopback access needs no token |
| 🛡️ **Same-subnet access control (ACL)** | An allow-list is derived from local adapter subnets: only same-subnet devices are served, anything else gets 403 |
| 🔒 **Security response headers** | Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer` |
| 🚫 **SSRF protection** | Automatic redirects are disabled and every hop is validated (max 3 hops); loopback / private / reserved / CGNAT / multicast / IPv6-private ranges are blocked, target ports limited to 80 / 443 |
| 📄 **Editable page** | The UI lives in `wwwroot\index.html` next to the executable and can be edited without recompiling; release builds embed a fallback copy |

### REST API

| Method | Path | Description |
|:--|:--|:--|
| `GET` | `/api/status` | Test state, live rate and current port |
| `GET` | `/api/adapters` | Adapter list and selection state |
| `POST` | `/api/adapters/select` | Choose which adapters take part in the test |
| `GET` | `/api/profiles` | Read the list of speed test profiles |
| `POST` | `/api/profiles` | Create or save a speed test profile |
| `GET` | `/api/history` | Page through history records (`?page=&pageSize=`) |
| `DELETE` | `/api/history` | Clear the history |
| `GET` | `/api/settings` | Read the current test parameters |
| `POST` | `/api/settings` | Write test parameters (concurrent-write safe, atomic persist) |
| `POST` | `/api/test/start` | Start a test (returns 200 only once the running state is real; a repeat call returns 409) |
| `POST` | `/api/test/stop` | Stop a test (also cancels during the preparation phase) |
| `GET` | `/api/server` | Server port, LAN state and adapter-subnet bindings |

> Every endpoint waits on the UI thread with a unified **2-second timeout** (returning 503 instead of hanging) and adds a 30-second request-level timeout; errors are sanitised into category messages and request bodies are capped at 256 KB.

## 🧰 Network Toolbox

**18-in-1 professional network diagnostic tools** built in — daily troubleshooting in a single window, the <b>18-in-1 Toolbox</b> inside the app.

| Tool | Description |
|:--|:--|
| 🏓 **Ping** | ICMP connectivity test with a custom target and count, reporting per-packet RTT and TTL |
| 🌐 **DNS Query** | A / AAAA resolution with elapsed time and address family |
| 📡 **HTTP Request** | HEAD probe reporting status code / response time / Server / Content-Type / Content-Length |
| 🗺️ **Traceroute** | ICMP TTL hop-by-hop trace (up to 30 hops) showing hop IP / latency / TTL live |
| 🔌 **Port Test** | TCP port connectivity test with a custom host and port (1 – 65535) |
| 📏 **MTU Probe** | Binary-search path MTU discovery (68 – 1500, don't-fragment) |
| 🔄 **DNS Compare** | Side-by-side latency for 8.8.8.8 / 114.114.114.114 / 1.1.1.1 / 223.5.5.5 |
| 🌍 **IP Geo** | IP geolocation / ISP / AS / time zone lookup |
| 🌐 **Public IP** | Three-source egress IP probe via ipify / ipip / ifconfig.me, cross-checked to reduce CDN bias |
| 🔒 **SSL Certificate** | Certificate issuer / subject / validity dates / days remaining / serial number / signature algorithm |
| 📨 **HTTP Header** | Full response header dump for any URL |
| 🧮 **Subnet Calculator** | IP + mask → network address / broadcast address / usable host count and range (including /31 and /32) |
| 📊 **Bandwidth** | Mbps ↔ MB/s · KB/s · Gbps · KBps conversion, plus the estimated time for a 100 MB file |
| 🕐 **Timestamp** | Unix seconds / milliseconds ↔ date, both directions |
| #️⃣ **Hash** | MD5 / SHA1 / SHA256 hashing |
| 🔤 **Base64** | Base64 encode and decode |
| 🆔 **UUID** | Batch UUID v4 generation (1 – 50) |
| 🔍 **NAT Detection** | STUN (RFC 5389) classification of mapping behaviour (cone / symmetric), port preservation, hairpin self-loopback and STUN/TCP availability; Google + Xiaomi servers cross-checked against the HTTP egress IP |

## ⌨️ Shortcuts & Parameters

### Shortcuts

| Shortcut | Action |
|:--|:--|
| <kbd>Enter</kbd> | Start a test (download mode, same as <kbd>Ctrl</kbd>+<kbd>D</kbd>) |
| <kbd>Esc</kbd> | Stop the test |
| <kbd>Ctrl</kbd> + <kbd>D</kbd> | Download only |
| <kbd>Ctrl</kbd> + <kbd>U</kbd> | Upload only |
| <kbd>Ctrl</kbd> + <kbd>B</kbd> | Full-speed bidirectional |

> Typing inside a text box does not trigger them.

### Parameters

"Range" is the slider span on the Settings page; "Default" is the factory value in the built-in `appsettings.json`.

| Parameter | Range | Default |
|:--|:--:|:--:|
| Concurrent threads | 2 – 1024 | **128** |
| Test duration | 10 – 600 s | **60 s** |
| Thread ramp-up interval | 0 – 2000 ms | **50 ms** |
| Average measurement delay | 1 – 30 s | **10 s** |
| Rate smoothing window | 0.5 – 10 s | **3.0 s** |
| NIC poll interval | 200 – 5000 ms | **1000 ms** |
| Latency poll interval | 500 – 10000 ms | **1000 ms** |
| Jitter sample interval | 500 – 5000 ms | **1000 ms** |
| Jitter target host | — | **8.8.8.8** |
| Packet loss target host | — | **8.8.8.8** |
| Packet loss poll interval | 500 – 5000 ms | **1000 ms** |
| Compensation threshold | 0.3 – 0.8 | **0.5** |
| Compensation confirm window | 1 – 10 s | **3 s** |
| Adaptive start threads | 1 – adaptive ceiling | **2** |
| Adaptive thread ceiling *(derived)* | 128 / 256 / 512 / 1024 steps | **1024** |
| Web server port | 1024 – 65535 | **8080** |

> **Adaptive thread ceiling** is derived by the engine: 1024 for a single NIC, scaled by each NIC's share in multi-NIC mode; once adaptive mode is on "concurrent threads" no longer applies and the thread count grows from the starting value in 128 / 256 / 512 / 1024 steps.<br>
> **Web server port** is configured on the Web Server page (automatic fallback or custom); every other parameter lives on the Settings page.<br>
> URLs in imported custom profiles go through the same SSRF validation as the Web API.

## 🗂️ Data & Configuration Files

All user data lives under `%LOCALAPPDATA%\NetSpeedTest\`:

| File | Description |
|:--|:--|
| `NetSpeedTest.db` | SQLite database: speed test history and custom profiles (WAL mode + busy_timeout) |
| `appsettings.json` | The parameter override layer saved from the Settings page (atomic writes) |
| `web.json` | Web server switch, port mode and LAN access settings |
| `theme.json` / `language.json` | Theme and UI language selection |

> A default `appsettings.json` is embedded in the executable and merged with the user override, so deleting the user folder restores factory settings.<br>
> Launch with the `--debug` argument to write `debug.log` next to the executable.

## 🏗️ Tech Stack

| Layer | Technology |
|:--|:--|
| Runtime | .NET 8.0-windows |
| UI framework | WPF (custom-drawn title bar + embedded sidebar pages) |
| Architecture | MVVM (CommunityToolkit.Mvvm source generators + Microsoft.Extensions.DependencyInjection) |
| Real-time charts | LiveChartsCore.SkiaSharpView.WPF |
| Data persistence | Microsoft.Data.Sqlite |
| Configuration | Microsoft.Extensions.Configuration (embedded defaults + user override layer) |
| Web server | `System.Net.HttpListener` + embedded `wwwroot` static assets |
| Test framework | xUnit (`NetSpeedTest.Tests`) |
| AI collaboration | **DeepSeek** (AI-assisted programming) |

Project layout:

```text
NetSpeedTest/
├─ NetSpeedTest/            # WPF application
│  ├─ Converters/           # XAML value converters
│  ├─ Helpers/              # Version, formatting and other helpers
│  ├─ Languages/            # Chinese / English localization resources
│  ├─ Models/               # Data models and options
│  ├─ Services/             # Speed test, web server, persistence services
│  ├─ Themes/               # Dark / light theme resources
│  ├─ ViewModels/           # MVVM view models
│  ├─ Views/                # Windows and embedded pages
│  └─ wwwroot/              # Web console static assets
├─ NetSpeedTest.Tests/      # xUnit tests (incl. README and site contract tests)
├─ assets/                  # Icons, screenshots and website assets
├─ docs/                    # Design spec and QA documents
└─ tools/                   # Screenshot, documentation check and smoke scripts
```

## 🚀 Quick Start

### Requirements

Windows 10 / 11 · [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build & Run

```powershell
git clone https://github.com/lingyingaojue/NetSpeedTest.git
cd NetSpeedTest
dotnet restore
dotnet build
dotnet run --project NetSpeedTest\NetSpeedTest.csproj
```

### Run the Tests

```powershell
dotnet test NetSpeedTest.Tests\NetSpeedTest.Tests.csproj
```

### Publish a Single-File Build

```powershell
dotnet publish NetSpeedTest\NetSpeedTest.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The result is one self-contained executable (about 170 MB); the target machine needs no .NET runtime installed.

### End-to-End Scripts

`tools\` contains integration scripts that drive a real Release build (administrator rights are needed to open the HTTP.sys port):

| Script | Purpose |
|:--|:--|
| `tools\smoke-test.ps1` | Drives the Web API: token validation, security headers, input validation, test lifecycle, concurrent settings writes and FATAL log scan |
| `tools\verify-version.ps1` | Checks the version string across csproj → executable file properties → Web console → static assets |
| `tools\verify-token.ps1` | Checks that the session token is enforced for real LAN HTTP requests |
| `tools\verify-extra.ps1` | Additional scenario verification |
| `tools\verify-docs.ps1` | Verifies the README, website and community files stay on the shared template |
| `tools\capture-screenshots.ps1` | Launches the real app and refreshes README / website screenshots at a fixed size |

## 📥 Download

Get the compiled single-file build from [Releases](https://github.com/lingyingaojue/NetSpeedTest/releases) (.NET 8 self-contained, about 170 MB) and run it right away.

Website and live tour: <https://lingyingaojue.github.io/NetSpeedTest/>

## 📝 Changelog

See [CHANGELOG.md](CHANGELOG.md) for the latest three releases. Current version: **v1.4.4** fixes nine defects — remote test results not being persisted, bidirectional tests reporting "0 succeeded", cancelled tests recording zero traffic, MTU probing failing across a gateway, inconsistent settings bounds and firewall inbound rules only ever being deleted; the app now requests administrator rights by default (a UAC prompt on every launch) so it can open inbound firewall rules automatically.

## ❓ FAQ

### Why do the results differ from browser speed tests?

Browser tests usually sample for a short time over one or a few connections, while NetSpeedTest runs 128 concurrent threads by default and excludes the ramp-up phase from the average, which tracks real link bandwidth more closely. If the gap is large, make sure the network is idle, switch nodes and retest after 30 seconds.

### Why does the app ask for UAC every time?

Since v1.4.4 the application manifest requests administrator rights so inbound firewall rules can be created and cleaned up automatically. A standard account that declines elevation cannot start the app; allow elevation for this program in system policy if needed.

### Why can't my phone reach the web console?

Check that you are opening the local LAN IP and the port shown in the UI (8080 by default, automatically advanced when taken); the ACL only serves same-subnet devices, so cross-subnet and anonymous sources receive 403.

### Where is the measurement history stored, and how do I back it up?

History and configuration live under `%LOCALAPPDATA%\NetSpeedTest\`, where `NetSpeedTest.db` is the SQLite database; copy the whole folder to back it up, or export CSV from the history page.

### Which Windows versions are supported, and is .NET required?

Windows 10 / 11 x64. The released build is a self-contained .NET 8 single file, so the target machine needs no .NET runtime; building from source requires the .NET 8 SDK.

## 🤝 Contributing

Code, documentation and translation contributions are welcome — please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

The palette, section templates and copy rules live in [docs/DESIGN.md](docs/DESIGN.md).

- Use [Issues](https://github.com/lingyingaojue/NetSpeedTest/issues) for feature requests and bug reports
- Report security problems privately via [SECURITY.md](SECURITY.md), not in a public issue
- Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) when taking part in the community

## 🤖 How This Project Is Built & AI Collaboration

**This project is made with DeepSeek AI.** **凌影傲爵 (LingYingAoJue)** led requirements design, architecture decisions and final acceptance, while **DeepSeek AI** collaborated on the implementation and documentation.

| Role | Credit |
|:--|:--|
| Design & development | 凌影傲爵 |
| AI collaboration | DeepSeek |

> This credit matches the in-app "About → AI Collaboration" card and the [website](https://lingyingaojue.github.io/NetSpeedTest/) pages.

## 💬 Feedback & Contact

| Channel | Address |
|:--|:--|
| 🐛 Issue | <https://github.com/lingyingaojue/NetSpeedTest/issues> |
| 📧 Email | mashuo2010az@163.com |
| 💬 WeChat / QQ | Smailboy2010 |
| 👥 QQ group | See the QR code under ["Feedback & Contact"](https://lingyingaojue.github.io/NetSpeedTest/#contact) on the website |

> Contact details on the in-app "About" page can be copied to the clipboard in one click.

## 📄 License

[MIT License](LICENSE) · © 2026 lingyingaojue

<div align="center">

**NetSpeedTest** · © 2026 凌影傲爵 · Built with DeepSeek AI collaboration

</div>

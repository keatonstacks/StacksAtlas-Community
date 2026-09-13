# StacksAtlas Community Edition

> Industrial-grade network discovery, hardware inventory classification, topology mapping, and control bridge.

StacksAtlas Community is the free, open-source core of StacksAtlas under the **MIT License**.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![React 18](https://img.shields.io/badge/React-18-blue.svg)](https://react.dev/)

---

## Features

- **Fast Network Sweeps**: High-performance ARP, ICMP, SNMP, and mDNS discovery sweeps.
- **Automated Device Classification**: Hardware vendor OUI lookups, OS detection, and model heuristics.
- **Interactive Network Topology Graph**: Real-time visualization of parent-child uplinks, switches, and access points.
- **OpenAVC Control Bridge**: Device drawer control, macros, and smart-building integration.
- **Outbound Webhooks**: Real-time event notifications for Slack, Discord, Microsoft Teams, and custom HTTP endpoints.
- **Zero-Install Portable Scanner**: Instant single-file executable for ad-hoc network troubleshooting.

---

## Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/)

### Building from Source

```bash
# 1. Build the React UI
cd StacksAtlas.UI
npm install
npm run build
cd ..

# 2. Build and run the Backend API
dotnet build StacksAtlas.slnx -c Release
dotnet run --project StacksAtlas.API
```

Once started, navigate to `http://localhost:5000` in your web browser.

---

## Contributing

We welcome contributions! Please feel free to submit pull requests for:
- New device fingerprints and OUI definitions
- SNMP profiles for enterprise networking gear
- OpenAVC drivers and integrations
- UI enhancements

---

## License

StacksAtlas Community is open-sourced under the [MIT License](LICENSE).
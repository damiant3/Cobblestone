# Codex NetTool

A network administration toolkit providing Wireshark-style packet capture, port scanning, subnet host discovery, and a group membership admin panel for the codex mesh. Presented as a tabbed UI.

## Modules

- **PacketAnalyzer** — Protocol layer types (EthFrame, IpPacket, TcpSegment, UdpDatagram, ArpPacket, IcmpPacket, DnsQuery, HttpMessage); CapturedPacket with multi-layer decode; DisplayFilter with boolean algebra; CaptureState with ring-buffer
- **PortScanner** — ScanType (TCP Connect/SYN/UDP/Ping), PortStatus, service identification; well-known service table (18 ports including codex-specific 9100/2682); three preset ScanProfiles; NetworkMap with DiscoveredHost
- **GroupAdmin** — Widget rendering of GroupState: overview panel, member table with trust scores, service registry table, action toolbar
- **NetToolApp** — Top-level tabbed app state (5 tabs), toolbar with interface/target selection
- **NetToolPersist** — JSON serialization of scan results and discovered hosts (kind 40) via DiskFacts
- **TestGroupMembership** — 8 tests covering group creation, member add, heartbeat, suspect/dead transitions, election, service registry

## Completeness

70% — Data model and UI widgets are complete. Missing: actual packet capture driver wiring (NE2K NIC would be the source), no `opening` entry point, SYN scan logic stubbed, DNS/HTTP layer decoders not present, GroupAdmin write operations (force election, announce service) are buttons with no handler. Test suite is thorough for GroupMembership.

## Codex Conformance

Full — Pure Codex throughout. Packet capture would be emitted through a network plug targeting the NE2K device driver.

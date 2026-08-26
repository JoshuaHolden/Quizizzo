# ADR-0001: Server-authoritative reconstructable state

Status: Accepted

The server owns all party and game decisions. Clients consume role-specific snapshots and may request a fresh snapshot at any time, so correctness never depends on receiving every realtime message. This adds snapshot/view design work but makes refresh and reconnect reliable and prevents client-side cheating.

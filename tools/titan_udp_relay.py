#!/usr/bin/env python3
"""
Titan Game Hub - Lightweight CGNAT UDP Room Relay Server
Usage: python3 titan_udp_relay.py --host 0.0.0.0 --port 47900
Pairs peers sending 'TITAN_RELAY_JOIN#<ROOM_CODE>' and forwards all game UDP packets between peers in the same room.
"""
import argparse
import socket
import time

ROOM_TTL_SEC = 120.0
JOIN_PREFIX = b"TITAN_RELAY_JOIN#"


def main():
    parser = argparse.ArgumentParser(description="Titan Game Hub UDP Relay")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=47900)
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((args.host, args.port))
    print(f"[TitanRelay] Listening on {args.host}:{args.port}")

    rooms = {}       # room -> {addr: last_seen}
    addr_room = {}   # addr -> room

    while True:
        try:
            data, addr = sock.recvfrom(65535)
            now = time.monotonic()

            if data.startswith(JOIN_PREFIX):
                room = data[len(JOIN_PREFIX):].decode("utf-8", errors="ignore").strip().upper()
                if not room:
                    continue
                old_room = addr_room.get(addr)
                if old_room and old_room != room and old_room in rooms:
                    rooms[old_room].pop(addr, None)
                rooms.setdefault(room, {})[addr] = now
                addr_room[addr] = room
                sock.sendto(b"TITAN_RELAY_OK", addr)
                continue

            room = addr_room.get(addr)
            if not room or room not in rooms:
                continue

            peers = rooms[room]
            peers[addr] = now

            stale = [p for p, ts in peers.items() if now - ts > ROOM_TTL_SEC]
            for p in stale:
                peers.pop(p, None)
                addr_room.pop(p, None)

            for peer_addr in list(peers.keys()):
                if peer_addr != addr:
                    sock.sendto(data, peer_addr)
        except Exception as exc:
            print(f"[TitanRelay] Error: {exc}")


if __name__ == "__main__":
    main()

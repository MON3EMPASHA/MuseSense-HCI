import asyncio
import sys

from bleak import BleakScanner


async def discover_devices(duration: float = 8.0) -> None:
    """
    Discover nearby Bluetooth Low Energy (BLE) devices and print their
    names and addresses. Uses the `bleak` library, which talks to the
    Windows BLE stack via WinRT — no compilation or PyBluez required.

    Note: this is BLE only. Classic Bluetooth devices (e.g. older
    headsets that don't advertise over BLE) will not appear here.
    """
    print("Starting BLE device discovery...")
    print(f"Scanning for {duration:.0f} seconds...\n")

    try:
        devices = await BleakScanner.discover(
            timeout=duration, return_adv=True
        )
    except Exception as e:
        print(f"Error during scan: {e}")
        sys.exit(1)

    if not devices:
        print("No BLE devices found.")
        print(
            "\nMake sure Bluetooth is turned on and that the target "
            "device is in pairing/advertising mode."
        )
        return

    print(f"Found {len(devices)} device(s):\n")
    print("-" * 80)
    print(f"{'Device Name':<32} {'Address':<20} {'RSSI':>6}")
    print("-" * 80)

    rows = []
    for address, (device, adv) in devices.items():
        name = device.name or adv.local_name or "Unknown"
        rssi = adv.rssi if adv.rssi is not None else 0
        rows.append((name, address, rssi))

    rows.sort(key=lambda r: r[2], reverse=True)

    for name, address, rssi in rows:
        display_name = name if len(name) <= 30 else name[:29] + "…"
        print(f"{display_name:<32} {address:<20} {rssi:>5} dBm")

    print("-" * 80)


if __name__ == "__main__":
    asyncio.run(discover_devices())

import bluetooth
import sys


def discover_devices():
    """
    Discover nearby Bluetooth devices and print their names and MAC addresses.
    Uses PyBluez library for device discovery.
    """
    print("Starting Bluetooth device discovery...")
    print("This may take a moment...\n")

    try:
        # Discover nearby devices with names
        devices = bluetooth.discover_devices(
            duration=8, lookup_names=True, flush_cache=True
        )

        if not devices:
            print("No Bluetooth devices found.")
            return

        print(f"Found {len(devices)} device(s):\n")
        print("-" * 60)
        print(f"{'Device Name':<30} {'MAC Address':<20}")
        print("-" * 60)

        for addr, name in devices:
            # Use the name returned by PyBluez, fallback to Unknown if empty
            device_name = name if name else "Unknown"
            print(f"{device_name:<30} {addr:<20}")

        print("-" * 60)

    except bluetooth.BluetoothError as e:
        print(f"Bluetooth Error: {e}")
        print("\nMake sure you have PyBluez installed:")
        print("  pip install pybluez")
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    discover_devices()

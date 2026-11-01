# Project instructions

## Project context

- This is a Unity 6 project using C#. The exact editor version is in [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt).
- [Mirror](../Assets/Mirror/version.txt) is the installed networking framework.
- [FizzySteamworks](../Assets/Mirror/Transports/FizzySteamworks/README.md) is installed as a Mirror transport for Steam networking.
- Steamworks.NET provides Steamworks API bindings and is installed through the Unity Package Manager Git dependency in [manifest.json](../Packages/manifest.json); its resolved revision is in [packages-lock.json](../Packages/packages-lock.json).

## Working with the project

- For Steam multiplayer, use the existing Mirror, FizzySteamworks, and Steamworks.NET stack by default. Do not replace these libraries or add alternative networking or Steam packages without the user's approval.
- Before making networking changes, inspect the relevant scripts, scenes, prefabs, NetworkManager, transport assignment, and Steam initialization. Installed packages do not imply that these are configured or working.
- Check the installed package versions and the FizzySteamworks README when choosing APIs or explaining setup. Describe any required Unity Inspector, Steam client, and testing steps, and distinguish unverified behavior from verified results.
- Prefer project code over edits to third-party Mirror or FizzySteamworks sources unless those edits are necessary. Preserve Unity `.meta` files, GUIDs, and serialized references when moving or renaming assets.

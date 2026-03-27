<div align="center">

<img src="Docs/Clubhawk-round safe.png" alt="Clubhawk Logo" width="128">

# Staff Permission System

*A comprehensive staff permission management system for VRChat worlds.*

[![VPM](https://img.shields.io/badge/VPM-8B5CF6?style=flat-square&logo=unity&logoColor=white)](https://pesky12.github.io/PeskyBox/index.json)
[![License](https://img.shields.io/badge/License-MIT-EC4899?style=flat-square)](LICENSE)

</div>

---

Centralized permission management with PIN authentication, ProTV integration, audio boost controls, and dynamic object visibility.

## Features

- **StagePermissionManager** — Central manager for staff permissions with network sync
- **PIN Code Authentication** — Secure PIN-based staff authentication with hashed storage
- **LoudCube** — Audio boost system for staff members with distance-based attenuation
- **Staff Object Manager** — Dynamic object visibility based on staff permissions
- **ProTV Integration** — Automatic super user permissions for staff members
- **Staff Temporary Opt-Out** — Allow staff to temporarily disable their permissions

## Installation

### Via VPM (Recommended)

```
https://pesky12.github.io/PeskyBox/index.json
```

Copy the URL above and add it to your [VRChat Creator Companion](https://vcc.docs.vrchat.com/) package listings.

### Manual

1. Download the latest release from the [GitHub releases page]
2. Extract the `com.pesky.box.staffpermissionsystem` folder into your Unity project's `Assets/Packages` directory
3. Open Unity and let it compile
4. Have a snack!

## Quick Start

1. Add the `Staff Permission Manager` prefab to your scene
2. Configure staff list in the manager
3. Add permission-controlled objects with `StaffObjectManager`
4. (Optional) Set up PIN authentication with `PINCodePanel`
5. (Optional) Add `LoudCube` for audio boost functionality

## Components

| Component | Description |
|-----------|-------------|
| **StagePermissionManager** | Central permission manager with network sync and subscriber system |
| **StaffObjectManager** | GameObject/CanvasGroup visibility based on permissions, collider-reactive |
| **LoudCube** | Audio boost with distance attenuation, per-player boosting, synced state |
| **PINCodePanel** | Secure PIN auth with hashed storage, asterisk display |
| **StageManagerProTVPermissions** | ProTV bridge — grants super user perms to staff |
| **StaffTemporaryOptOut** | Staff opt-in/out with session persistence |

## Dependencies

- VRChat Worlds SDK 3.5.x (includes UdonSharp)
- **[AccountManager](https://pesky12.github.io/PeskyBox/index.json)** (`com.localpolicedepartment.accountmanager`) — Staff verification system

### Optional

- **ProTV** (`dev.architech.protv`) — Required for `StageManagerProTVPermissions`

## License

MIT License — Free to use in any project, commercial or otherwise.

**Restriction:** Redistribution or resale as a standalone asset package is prohibited. You may include this in your own projects, games, and worlds, but you may not sell or redistribute it as a standalone Unity package or asset store product.

## Credits

Some code contributed by [Chanoler](https://github.com/Chanoler).

---

<div align="center">

Made with 💜 by [Pesky12](https://github.com/pesky12)

</div>
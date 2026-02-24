# Pawlygon Patcher Hub

**Unity Editor tool for applying face tracking patches to VRChat avatar FBX models.**

Patcher Hub streamlines the process of applying HDiff-based binary patches to avatar models, with integrated VRChat Creator Companion (VCC) package management, dependency validation, and automated scene setup.

![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)
![License](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey)
![Version](https://img.shields.io/github/v/tag/PawlygonStudio/PatcherHub?label=version&cb=1)

## Features

- **One-Click Patching** — Apply face tracking patches to FBX avatar models with a single button
- **Batch Patching** — Select and patch multiple avatar configurations sequentially with dependency ordering
- **Package Validation** — Automatically checks required VRChat packages are installed and up to date
- **VCC Integration** — Install, update, and manage packages directly through the VRChat Creator Companion API
- **Source File Validation** — MD5 hash verification ensures original avatar files haven't been modified before patching
- **Automatic Scene Setup** — Creates a new scene with all patched prefabs arranged in a grid layout for quick testing
- **Cross-Platform** — Includes hpatchz binaries for Windows, macOS, and Linux

## Installation

### Import .unitypackage

1. Download the latest `.unitypackage` from [Releases](../../releases)
2. In Unity, go to **Assets > Import Package > Custom Package...**
3. Select the downloaded file and import all files

### Manual (Git)

Clone or download this repository into your Unity project's `Assets/!Pawlygon/PatcherHub/` folder.

## Usage

1. Open the tool via **Tools > !Pawlygon > Patcher Hub**
2. The window automatically detects all `FTPatchConfig` assets in your project
3. Select which avatars to patch using the checkboxes
4. Review any package warnings or errors shown in the UI
5. Click **Patch Selected Avatars** to apply patches
6. A new scene is created with the patched prefabs ready for testing

## Creating Patch Configurations

1. Right-click in the Project window
2. Navigate to **Create > Pawlygon > FaceTracking Patch Config**
3. Configure the avatar name, version, original FBX reference, and diff files
4. Click **Generate Hashes** to enable source file validation

## Requirements

- Unity 2022.3 or later
- [VRChat Creator Companion](https://vcc.docs.vrchat.com/) (for package management features)

## Credits

- [**Hash's EditDistributionTools**](https://github.com/HashEdits/EditDistributionTools) — Inspiration for distribution workflows using binary patching
- [**hpatchz**](https://github.com/sisong/HDiffPatch) — High-performance binary diff/patch library by housisong
- [**ikeiwa VRC Package Verificator**](https://ikeiwa.gumroad.com/l/vrcverificator) — Inspiration behind our package checking feature
- [**VRChat Creator Companion**](https://vcc.docs.vrchat.com/) — Package management integration
- [**Furality SDK**](https://furality.org/) — VCC package import implementation
- **tkya** — Countless hours of technical support to the community
- **VRChat Community** — Feedback, testing, and feature requests

*Thank you to everyone who helped make PatcherHub possible!*

## License

This project is licensed under [CC BY-NC-SA 4.0](LICENSE.md).

HDiffPatch (`hdiff/hpatchz/`) is distributed under the [MIT License](hdiff/hpatchz/License.txt).

## Links

- [Website](https://www.pawlygon.net)
- [Discord](https://discord.com/invite/pZew3JGpjb)
- [YouTube](https://www.youtube.com/@Pawlygon)
- [X (Twitter)](https://x.com/Pawlygon_studio)

---

*Made with ❤ by Pawlygon Studio*

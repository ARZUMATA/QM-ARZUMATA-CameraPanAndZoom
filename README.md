# QM_CameraPanAndZoom

This mod enchances the camera in QM.
You can now pan the camera with your mouse.
It has adjustable zoom levels that you can define.
Also it has alternative zoom method that follows different logic and should be more smooth.
Camera zoom is persistent across levels.

![thumbnail icon](media/thumbnail.png)

## Features

* Zoom camera using custom zoom levels.
* Alternative zoom method.
* PCamera panning with mouse.

- **Customizable Configuration**: Allows users to adjust settings through a [Mod Configuration Menu (MCM).](https://steamcommunity.com/sharedfiles/filedetails/?id=3469678797)

## Requirements (Optional)

- **MCM (Mod Configuration Menu)**: A configuration menu framework to manage settings via an in-game interface.

As alternative you can find config files in:
- `%AppData%\..\LocalLow\Magnum Scriptum Ltd\Quasimorph_ModConfigs\QM_CameraPanAndZoom\config_mcm.ini`

# Configuration
| Name                  | Default | Description                                                                 |
|-----------------------|---------|-----------------------------------------------------------------------------|
| ZoomTweakEnabled      | true    | Enable / Disable zoom tweak                                               |
| ZoomAlternativeMode   | false   | Ignore zoom steps and move duration. Super smooth based on how much you scroll. |
| ZoomDuration          | 5       | Zoom duration (milliseconds)                                              |
| CameraMoveDuration    | 5       | Camera move duration (milliseconds)                                        |
| PanningEnabled        | true    | Enable / Disable panning                                                  |
| PanSensitivity        | 1       | Pan sensitivity                                                           |
| PanButton             | 3       | Pan mouse button                                                          |
| ZoomInSteps           | 5       | Zoom in steps                                                             |
| ZoomOutSteps          | 5       | Zoom out steps                                                            |
| ZoomMax               | 400     | Maximum zoom in                                                           |
| ZoomMin               | 5       | Minimum zoom out                                                          |
| DebugLog              | false   | Debug log                                                                 |

# Source Code
Source code is available on [GitHub](https://github.com/ARZUMATA/QM-ARZUMATA-CameraPanAndZoom)

Thanks to NBK_RedSpy, Crynano and all the people who make their code open source.

# Change Log
## 1.0 (80b5b96)
* Initial release


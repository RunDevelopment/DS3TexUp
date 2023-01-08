# Shadow mod

This mod enables dynamic shadows in all outdoor areas of the game.
This greatly improves the quality of shadows and fixes light bugs in many areas.

## Installation

1. Unpack your game with UXM (https://www.nexusmods.com/eldenring/mods/1651)
2. Patch the game with UXM
3. Drag and drop the folders from the mod into the game directory `\DARK SOULS III\Game\`
4. Enable shadows in game

### Enabling shadows

After installing with UMX or Mod Engine, be sure to enable shadows.
When in game, go to _System_ > _Graphics_ > _Advanced Settings_ and make sure that _Shadow quality_ is set to at least _Low_.

If _Shadow quality_ is set to _Off_, this mod will be turned off.

## Online

This mod only includes MSB and GPARAM file replacements. It does not change any code or save data.

It should be safe for online play if installed with UXM.

## Performance impact

The performance impact of this mod varies between areas and can be adjusted using the _Shadow quality_ setting in game.
Generally, the performance impact is as follows:

| Shadow Quality | Compared to vanilla (same shadow quality) | Compared to Shadows Off |
| -------------- | ----------------------------------------- | ----------------------- |
| Off            | 0%                                        | 0%                      |
| Low            | 15%                                       | 20%                     |
| Medium         | 15%                                       | 20%                     |
| High           | 17%                                       | 25%                     |
| Max            | 25%                                       | 40%                     |

Generally, _Low_ and _Medium_ have the same performance cost, but _Medium_ looks significantly better.
The jump from _Medium_ to _High_ is quite large in terms of quality at only a small performance cost.

You should only set _Shadow quality_ to _Max_ if your machine can smoothly run this mod at this setting.
The relatively small improvement in quality is not worth any frame drops.

Note: If *Shadow quality* is to *Off*, this mod will be turned off and have no performance impact at all.

## Credit

A huge thanks to the folks over at ?ServerName?! This mod would not have been possible without the tools and resources you folks provide.

Used tools:

-   Yabber+
-   UXM Selective Unpacker
-   DSMapStudio 1.03
-   DS3 DebugMenuEx v1.1
-   SoulsFormats (C# library)

## License

CC BY 4.0

https://creativecommons.org/licenses/by/4.0/

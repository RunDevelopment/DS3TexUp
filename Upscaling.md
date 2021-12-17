# Upscaling

## Albedo (_a)

- `4x-UltraSharp` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50%

This consistently produced the sharpest, most interesting (detailed), and noise-free images.

## Normal (_n)

DS3 normal textures contains 3 different material maps that all have to be handled separately.

### Normal part

- `4x-UniScale_Restore` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50% on the extracted normal maps
- `1x_NormalMapGenerator-CX-Lite_200000_G` on the upscaled albedo for high-frequency normals

DS3's normal maps really don't have a lot of detail on them so upscaling them produces very flat materials. The problem is that the upscaled normal map only contains the low-frequency structure of the original, it doesn't not contain any of the "new" high-frequency details of the upscaled albedo.

To solve this, I also generate a normal map from the upscaled albedo. Since generated normal map contains (almost exclusively) the high-frequency details of the albedo. This is perfect for us.

The 2 maps (same size) are then combined using a height map addition (see the `DS3NormalMap` class). This produces a combined normal map with the low-frequency structure of the original upscaled normal map and the high-frequency details of the generated normal map.

### Gloss part

- `4x-UltraSharp` on the extracted gloss maps

Gloss maps are really blocky in DS3. A lot of the gloss artifacts seen in vanilla are not due to low-res textures but due to the extreme block compression artifacts. Normal maps use BC7, so I frankly don't understand how the gloss maps can look so bad.

Since this is a GIGO situation, I just used `4x-UltraSharp`. It does a decent job of dealing with the blockiness and retains the details and structure of the original.

### Height part

- `1x_BCGone-DetailedV2_40-60_115000_G` chained with `4x-UltraSharp` on the extracted height maps

Height maps are even blockier than gloss maps and blurry often blurry on top of that. The first model does a good job of dealing with the extreme blockiness, but GIGO. It sometimes produces noticeable artifacts that are amplified by `4x-UltraSharp`. It doesn't happen too often, so I just ignored it for now.

If the artifacts are too noticeable, I will think of a way of using the normal map to hopefully resolve the artifacts. However, I don't see the need to do so right now.

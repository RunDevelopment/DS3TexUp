# Upscaling

## Albedo (_a)

- `4x-UltraSharp`
- `4x-UltraSharp` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50%
- `ESRGAN_GroundTextures_NonTiled_RGB_UpscalingAlgorithm_128HR_32LR_305000Iterations`

The first 2 models are universal upscalers and have their strengths and weaknesses. To get the sharpest results, I run both models and then pick the sharpest upscaled image using the `GetSharpnessScore` function.

If the image is purely stone and/or dirt, nothing beats `ESRGAN_GroundTextures`. It consistently produces extremely detailed and realistic-looking images. Frankly, it's not even close when comparing it to the universal upscalers. However, this model only knows rocks and dirt, and will shape everything in that image. Hence I only use it for texture that exclusively contain the following materials: *rock, stone, gravel, dirt, sand, ash, glass, ice, and small debris*.

<details>
<summary>My evaluation of the 2 universal models</summary>

I will hence forth refer to `4x-UltraSharp` as M1 and `4x-UltraSharp` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50% as M2.

From what I tested, M2 seems to consistently produce the sharpest and most interesting results but only if the input image is very sharp, if the input is slightly blurry, the output will be too. This is a problem as some textures are just a tiny bit blurry. M2 is a model of extremes. The good results are generally very good but the bad results are also very bad.

Fortunately, M1 doesn't have this problem and will always produce okay results. However, only okay. The average quality of the upscaled images is good but no where near a good M2 upscale.

</details>

### A note on denoising and block compression

While block compression artifacts can throw off some models, many models were trained on JPEG compressed data. These models typically have no problem with BC and can be used without any prior denoising.

I prefer not to use denoising or BC-removal models (or external tools). Noise added by compression artifacts and high-frequency details are indistinguishable for each other. Any denoising models (and tools) will produce images devoid of high-frequency details which results in very flat and boring upscaled textures.

Models that can handle noise and compression artifacts tend to produce more detailed/interesting upscaled images.

### Alpha

- `1x_SSAntiAlias9x` chained with `4x-UltraSharp`

Some texture have binary or full 8 bit alpha channels. Upscaling them is a challenge because ESRGAN wasn't designed to deal with transparency. Cupscale does provide an "Enable Transparency" option but the results aren't very good.

I solved this by splitting these textures into a RGB color image and a grey-scale alpha image. The color image is the original transparent texture with its colors extended to fill transparent regions and the alpha image is just the value of the alpha channel. The color and alpha images are then upscaled separately. The alpha image uses the above model and the color image uses whatever model works best (see the list of model at the start of the albedo section). The two upscaled images are then recombined to get an upscaled transparent texture.

The `1x_SSAntiAlias9x` model is necessary to smoothen the edges of images with binary alpha. The anti aliasing also benefits the full alpha images since it smooths over some block compression artifacts in the alpha channel.

This produces nice upscaled textures with full 8 bit alpha channels. In the case of images with binary alpha, the 8 bit alpha is then quantized to binary.

## Normal (_n)

DS3 normal textures contains 3 different material maps that all have to be handled separately.

### Normal part

- `4x-UniScale_Restore` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50% on the extracted normal maps
- `1x_NormalMapGenerator-CX-Lite_200000_G` on the upscaled albedo for high-frequency normals

DS3's normal maps really don't have a lot of detail on them so upscaling them produces very flat materials. The problem is that the upscaled normal map only contains the low-frequency structure of the original, it does not contain any of the "new" high-frequency details of the upscaled albedo.

To solve this, I also generate a normal map from the upscaled albedo. The generated normal map contains (almost exclusively) the high-frequency details of the albedo. This is perfect for us.

The 2 maps (same size) are then combined using a height map addition (see the `DS3NormalMap` class). This produces a combined normal map with the low-frequency structure of the original upscaled normal map and the high-frequency details of the generated normal map.

### Gloss part

- `4x-UltraSharp` on the extracted gloss maps

Gloss maps are really blocky in DS3. A lot of the gloss artifacts seen in vanilla are not due to low-res textures but due to the extreme block compression artifacts. Normal maps use BC7, so I frankly don't understand how the gloss maps can look so bad.

Since this is a GIGO situation, I just used `4x-UltraSharp`. It does a decent job of dealing with the blockiness and retains the details and structure of the original.

### Height part

- `4x-UltraSharp` on the extracted height maps

Height maps are even blockier than gloss maps and often blurry on top of that. `4x-UltraSharp` does an unbelievable good job given the artifacts it has to deal with. However, it does add a noticeable noise.

### A note on compression artifacts

Normal textures in DS3 are typically compressed using BC7. This is definitely the right compression method to use and most normal textures (95%) use it. However, some are compressed using BC1. BC1 was designed for diffuse maps and will horribly compress normal maps resulting in heavy compression artifacts all over the normal map and especially in the gloss map.

I used `1x_BCGone-DetailedV2_40-60_115000_G` on all BC1 compressed normal textures before splitting them into normals and gloss maps. This will result in rather smooth normals and gloss maps but it's acceptable.

## Reflective (_r)

- `4x-UltraSharp` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50%

These maps aren't very noisy and this model seems to produce good results. Since this model also does most of the heavy lifting for albedo maps, the fine details and structures it generates line up.

## Shininess (_s)

- `4x-UltraSharp` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50%

Shininess is has a similar function to gloss (so similar that I haven't figured out the difference yet) however, the shininess map is its own texture and doesn't suffer from heavy compression artifacts as a result.

## Emissive (_em)

- `4x-UltraSharp` 50% + `4x_UniversalUpscalerV2-Neutral_115000_swaG` 50%

This model does a good job.

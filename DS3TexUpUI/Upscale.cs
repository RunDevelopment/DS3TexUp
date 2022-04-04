using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Pfim;

#nullable enable

namespace DS3TexUpUI
{
    public class UpscaleProject
    {
        public Workspace Workspace { get; }
        public UpscaledTextures Textures { get; set; } = new UpscaledTextures();
        public string TemporaryDir { get; set; }

        public UpscaleProject(Workspace workspace)
        {
            Workspace = workspace;
            TemporaryDir = Path.Join(workspace.TextureDir, "temp");
        }

        public void WriteDDS(TexId id, int upscale, ILogger logger)
        {
            var kind = id.GetTexKind();
            switch (kind)
            {
                case TexKind.Albedo:
                    WriteSRGB(id, upscale, Textures.Albedo, "albedo");
                    break;
                case TexKind.Normal:
                    WriteNormal(id, upscale, logger);
                    break;
                case TexKind.Reflective:
                    WriteSRGB(id, upscale, Textures.Reflective, "reflective");
                    break;
                case TexKind.Shininess:
                    WriteSRGB(id, upscale, Textures.Shininess, "shininess");
                    break;
                case TexKind.Emissive:
                    WriteEmissive(id, upscale, Textures.Emissive, "emissive");
                    break;
                case TexKind.Mask:
                    WriteMask(id, upscale);
                    break;
                default:
                    throw new Exception($"Unsupported tex kind {kind} for {id}.");
            }
        }

        private int GetTargetWidth(TexId id, int upscale)
        {
            if (DS3.OriginalSize.TryGetValue(id, out var size)) return size.Width * upscale;
            throw new Exception($"Unknown tex id {id}.");
        }
        private string GetTempPngFile(TexId id)
        {
            var target = Path.Join(TemporaryDir, id.Category, $"{id.Name.ToString()}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            return target;
        }
        private string GetOutputFile(TexId id)
        {
            var target = Path.Join(Workspace.OverwriteDir, id.Category, $"{id.Name.ToString()}.dds");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            return target;
        }
        private void ToDDS(TexId id, string png, int upscale, bool deleteAfter = false)
        {
            try
            {
                var format = DS3.OutputFormat[id];
                if (upscale >= 4 && id.GetTexKind() == TexKind.Normal && !HasAlpha(id) && format == DxgiFormat.BC7_UNORM)
                {
                    var (width, height) = DS3.OriginalSize[id];
                    var targetPixels = upscale * upscale * width * height;
                    if (targetPixels >= 2048 * 2048)
                    {
                        format = DxgiFormat.BC1_UNORM;
                    }
                }

                DDSExtensions.ToDDS(png, GetOutputFile(id), format, id);
            }
            finally
            {
                if (deleteAfter) File.Delete(png);
            }
        }
        private string GetFile(TexOverrideList source, TexId id)
        {
            if (source.GetFilesCached().TryGetValue(id, out var file)) return file;
            throw new Exception($"Cannot find {id} in directories {string.Join(" ; ", source.Directories)}");
        }
        private bool HasAlpha(TexId id)
        {
            var t = id.GetTransparency();
            return t == TransparencyKind.Binary || t == TransparencyKind.Full;
        }
        private void CheckWidth(TexId id, int upscale, int targetWidth, int actualWidth, string kind)
        {
            if (actualWidth < targetWidth)
                throw new Exception($"Unable to fulfill target for {id}. {targetWidth}px ({upscale}x) {kind} requested but only has {actualWidth}px.");
        }

        private readonly ConcurrentDictionary<string, int> _pngWidthCache = new ConcurrentDictionary<string, int>();
        private int GetPngWidth(string file)
        {
            if (_pngWidthCache.TryGetValue(file, out var width)) return width;

            width = PngExtensions.ReadPngSize(file).Width;
            _pngWidthCache[file] = width;
            return width;
        }

        private void EnsureWidth<P, A>(ref ArrayTextureMap<P> image, int targetWidth, AverageAccumulatorFactory<P, A> factory, TexId id, string kind)
            where P : struct
            where A : IAverageAccumulator<P>, new()
        {
            if (image.Width > targetWidth)
            {
                var factor = image.Width / targetWidth;
                if (targetWidth * factor != image.Width)
                    throw new Exception($"Cannot properly resize {kind} for {id}. Cannot resize {image.Width} into {targetWidth}.");

                image = image.DownSample(factory, factor);
            }
        }

        private bool TryGetAlpha(TexId id, int upscale, int targetWidth, out ArrayTextureMap<byte> alphaImage, bool upsample = false)
        {
            if (!HasAlpha(id))
            {
                alphaImage = default;
                return false;
            }

            var alpha = GetFile(Textures.Alpha, id.GetAlphaRepresentative());
            var alphaWidth = GetPngWidth(alpha);
            if (!upsample) CheckWidth(id, upscale, targetWidth, alphaWidth, "alpha");

            alphaImage = alpha.LoadTextureMap().GreyMinMaxBlend();

            if (upsample && alphaImage.Width < targetWidth)
            {
                alphaImage = alphaImage.UpSample(targetWidth / alphaImage.Width);
                CheckWidth(id, upscale, targetWidth, alphaImage.Width, "alpha");
            }

            EnsureWidth(ref alphaImage, targetWidth, Average.Byte, id, "alpha");

            if (id.GetTransparency() == TransparencyKind.Binary)
                alphaImage.QuantizeBinary(threshold: 127);

            return true;
        }
        private void WriteSRGB(TexId id, int upscale, TexOverrideList source, string kind)
        {
            var targetWidth = GetTargetWidth(id, upscale);

            var tex = GetFile(source, id.GetRepresentative());
            var texWidth = GetPngWidth(tex);
            CheckWidth(id, upscale, targetWidth, texWidth, kind);

            if (texWidth == targetWidth && !HasAlpha(id))
            {
                // fast path
                ToDDS(id, tex, upscale, deleteAfter: false);
                return;
            }

            var image = tex.LoadTextureMap();
            EnsureWidth(ref image, targetWidth, Average.Rgba32GammaAlpha, id, kind);

            // set alpha
            if (TryGetAlpha(id, upscale, targetWidth, out var alphaImage))
                image.SetAlpha(alphaImage);

            // create temporary file.
            var targetFile = GetTempPngFile(id);
            image.SaveAsPng(targetFile);

            ToDDS(id, targetFile, upscale, deleteAfter: true);
        }

        private void WriteEmissive(TexId id, int upscale, TexOverrideList source, string kind)
        {
            var targetWidth = GetTargetWidth(id, upscale);

            var tex = GetFile(source, id.GetRepresentative());
            var texWidth = GetPngWidth(tex);
            CheckWidth(id, upscale, targetWidth, texWidth, kind);

            var image = tex.LoadTextureMap();

            // The upscaling sometimes causes a very slight noise. This isn't noticeable in the PNG but it is in the
            // DDS to due color quantization artifacts. This can cause noticeable decolourings. The noise added is a
            // consistent rgb=(1,2,1).
            foreach (ref var p in image.Data.AsSpan())
            {
                p.R = (byte)Math.Max(0, (int)((p.R - 1) * 255f / 254));
                p.G = (byte)Math.Max(0, (int)((p.G - 2) * 255f / 253));
                p.B = (byte)Math.Max(0, (int)((p.B - 1) * 255f / 254));
            }

            EnsureWidth(ref image, targetWidth, Average.Rgba32GammaAlpha, id, kind);

            // set alpha
            if (TryGetAlpha(id, upscale, targetWidth, out var alphaImage))
                image.SetAlpha(alphaImage);

            // create temporary file.
            var targetFile = GetTempPngFile(id);
            image.SaveAsPng(targetFile);

            ToDDS(id, targetFile, upscale, deleteAfter: true);
        }

        private void WriteNormal(TexId id, int upscale, ILogger logger)
        {
            var targetWidth = GetTargetWidth(id, upscale);

            // normals
            var normal = GetFile(Textures.NormalNormal, id.GetNormalRepresentative());
            CheckWidth(id, upscale, targetWidth, GetPngWidth(normal), "normal");
            var normalImage = DS3NormalMap.Of(normal.LoadTextureMap()).Normals.Clone();
            EnsureWidth(ref normalImage, targetWidth, Average.Normal, id, "normal");

            // normal albedo
            if (DS3.NormalAlbedo.TryGetValue(id, out var albedoId))
            {
                var albedo = GetFile(Textures.NormalAlbedo, albedoId);
                var albedoWidth = GetPngWidth(albedo);
                if (albedoWidth >= targetWidth)
                {
                    CheckWidth(albedoId, upscale, targetWidth, albedoWidth, "normal albedo");
                    var normalAlbedoImage = DS3NormalMap.Of(albedo.LoadTextureMap()).Normals.Clone();
                    EnsureWidth(ref normalAlbedoImage, targetWidth, Average.Normal, albedoId, "normal albedo");

                    const float MaxStrength = 0.4f;
                    if (TryGetAlpha(albedoId, upscale, targetWidth, out var albedoAlphaImage, upsample: true))
                    {
                        // we want to take the alpha of the albedo into account when applying the normal map
                        albedoAlphaImage = albedoAlphaImage.Blur(1);
                        var strength = new float[albedoAlphaImage.Count].AsTextureMap(targetWidth);
                        for (int i = 0; i < strength.Data.Length; i++)
                            strength[i] = Math.Min(1f, albedoAlphaImage[i] / 63f) * MaxStrength;
                        normalImage.CombineWith(normalAlbedoImage, strength);
                    }
                    else
                    {
                        // the easy part
                        normalImage.CombineWith(normalAlbedoImage, MaxStrength);
                    }
                }
                else
                {
                    logger.SubmitLog($"Warning: normal albedo for {id} too small. {albedoWidth}px < {targetWidth}px");
                }
            }

            // DS3 normal texture
            var image = new Rgba32[normalImage.Count].AsTextureMap(normalImage.Width);
            for (int i = 0; i < image.Count; i++)
            {
                ref var p = ref image.Data[i];
                (p.R, p.G) = normalImage[i].ToRG();
            }

            // gloss
            var gloss = GetFile(Textures.NormalGloss, id.GetGlossRepresentative());
            CheckWidth(id, upscale, targetWidth, GetPngWidth(gloss), "gloss");
            var glossImage = gloss.LoadTextureMap().GreyMinMaxBlend();
            EnsureWidth(ref glossImage, targetWidth, Average.Byte, id, "gloss");
            image.SetBlue(glossImage);

            // alpha
            if (TryGetAlpha(id, upscale, targetWidth, out var alphaImage))
                image.SetAlpha(alphaImage);
            else
                image.SetAlpha(255);

            // create temporary file.
            var targetFile = GetTempPngFile(id);
            image.SaveAsPng(targetFile);

            ToDDS(id, targetFile, upscale, deleteAfter: true);
        }
        private void WriteMask(TexId id, int upscale)
        {
            const string kind = "mask";
            var targetWidth = GetTargetWidth(id, upscale);

            var tex = GetFile(Textures.Mask, id.GetRepresentative());
            var texWidth = GetPngWidth(tex);
            CheckWidth(id, upscale, targetWidth, texWidth, kind);

            if (texWidth == targetWidth && !HasAlpha(id))
            {
                // fast path
                ToDDS(id, tex, upscale, deleteAfter: false);
                return;
            }

            var image = tex.LoadTextureMap();
            EnsureWidth(ref image, targetWidth, Average.Rgba32, id, kind);

            // set alpha
            if (TryGetAlpha(id, upscale, targetWidth, out var alphaImage))
                image.SetAlpha(alphaImage);

            // create temporary file.
            var targetFile = GetTempPngFile(id);
            image.SaveAsPng(targetFile);

            ToDDS(id, targetFile, upscale, deleteAfter: true);
        }
    }

    public class UpscaledTextures
    {
        public TexOverrideList Alpha { get; set; } = new TexOverrideList();
        public TexOverrideList Albedo { get; set; } = new TexOverrideList();

        public TexOverrideList NormalNormal { get; set; } = new TexOverrideList();
        public TexOverrideList NormalGloss { get; set; } = new TexOverrideList();
        public TexOverrideList NormalHeight { get; set; } = new TexOverrideList();
        public TexOverrideList NormalAlbedo { get; set; } = new TexOverrideList();

        public TexOverrideList Reflective { get; set; } = new TexOverrideList();
        public TexOverrideList Emissive { get; set; } = new TexOverrideList();
        public TexOverrideList Shininess { get; set; } = new TexOverrideList();
        public TexOverrideList Mask { get; set; } = new TexOverrideList();
    }
}

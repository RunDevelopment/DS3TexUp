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
        public Workspace Workspace { get; set; }
        public UpscaledTextures Textures { get; set; }
        public string TemporaryDir { get; set; }

        public void WriteDDS(TexId id, int upscale)
        {
            switch (id.GetTexKind())
            {
                case TexKind.Albedo:
                    WriteSRGB(id, upscale, Textures.Albedo, "albedo");
                    break;
                case TexKind.Normal:
                    WriteNormal(id, upscale);
                    break;
                case TexKind.Reflective:
                    WriteSRGB(id, upscale, Textures.Reflective, "reflective");
                    break;
                case TexKind.Shininess:
                    WriteSRGB(id, upscale, Textures.Shininess, "shininess");
                    break;
                case TexKind.Emissive:
                    WriteSRGB(id, upscale, Textures.Emissive, "emissive");
                    break;
                default:
                    throw new Exception($"Unsupported tex kind {id.GetTexKind()} for {id}.");
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
        private void ToDDS(TexId id, string png, bool deleteAfter = false)
        {
            try
            {
                DDSExtensions.ToDDS(png, GetOutputFile(id), DS3.OutputFormat[id], id);
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

        private bool TryGetAlpha(TexId id, int upscale, int targetWidth, out ArrayTextureMap<byte> alphaImage)
        {
            if (!HasAlpha(id))
            {
                alphaImage = default;
                return false;
            }

            var alpha = GetFile(Textures.Alpha, id.GetAlphaRepresentative());
            var alphaWidth = GetPngWidth(alpha);
            CheckWidth(id, upscale, targetWidth, alphaWidth, "alpha");

            alphaImage = alpha.LoadTextureMap().GreyMinMaxBlend();
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
                ToDDS(id, tex, deleteAfter: false);
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

            ToDDS(id, targetFile, deleteAfter: true);
        }

        private void WriteNormal(TexId id, int upscale)
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
                    normalImage.CombineWith(normalAlbedoImage, 0.5f);
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

            ToDDS(id, targetFile, deleteAfter: true);
        }
    }

    public class UpscaledTextures
    {
        public TexOverrideList Alpha { get; set; }
        public TexOverrideList Albedo { get; set; }

        public TexOverrideList NormalNormal { get; set; }
        public TexOverrideList NormalGloss { get; set; }
        public TexOverrideList NormalHeight { get; set; }
        public TexOverrideList NormalAlbedo { get; set; }
        public string Normal { get; set; }

        public TexOverrideList Reflective { get; set; }
        public TexOverrideList Emissive { get; set; }
        public TexOverrideList Shininess { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace DS3TexUpUI
{
    public class Workspace
    {
        public string GameDir { get; }
        public string MapsDir => Path.Join(GameDir, "map");
        public string MapsBackupDir => Path.Join(GameDir, "map_old");

        public string TextureDir { get; }
        public string ExtractDir => Path.Join(TextureDir, "extract");
        public string OverwriteDir => Path.Join(TextureDir, "overwrite");
        public string UpscaleDir => Path.Join(TextureDir, "upscale");

        public Workspace(string gameDir, string textureDir)
        {
            GameDir = gameDir;
            TextureDir = textureDir;
        }

        public void Extract(SubProgressToken token)
        {
            EnsureBackup(token.Reserve(0));
            UnpackHighRes(token.Reserve(0.5));
            ExtractHighResTexture(token);
        }
        private void Unpack(SubProgressToken token, Func<string, bool> fileFilter)
        {
            token.SubmitStatus("Unpacking all map files");

            foreach (var (map, i) in DS3Info.Maps.Select((m, i) => (m, i)))
            {
                token.SubmitStatus($"Unpacking {map} map files");
                token.SubmitProgress(i / (double)DS3Info.Maps.Length);

                var mapDir = Path.Join(MapsDir, map);
                Yabber.Run(Directory.GetFiles(mapDir, $"{map}*.tpfbhd", SearchOption.TopDirectoryOnly));

                var files = new List<string>();
                foreach (var unpackedDir in GetUnpackedMapFileFolders(map))
                {
                    files.AddRange(Directory.GetFiles(unpackedDir, $"m*.tpf.dcx", SearchOption.TopDirectoryOnly).Where(fileFilter));
                }

                token.SubmitStatus($"Unpacking {map} textures ({files.Count})");
                Yabber.Run(token.Slice(i / (double)DS3Info.Maps.Length, 1.0 / DS3Info.Maps.Length), files.ToArray());
            }
            token.SubmitProgress(1);
        }
        private void UnpackHighRes(SubProgressToken token)
        {
            Unpack(token, f => !f.EndsWith("_l.tpf.dcx"));
        }
        private void ExtractHighResTexture(SubProgressToken token)
        {
            Directory.CreateDirectory(ExtractDir);
            Directory.CreateDirectory(OverwriteDir);

            var progressFactor = 1.0 / DS3Info.Maps.Length;
            foreach (var (map, i) in DS3Info.Maps.Select((m, i) => (m, i)))
            {
                ExtractHighResTextureMap(token.Slice(i * progressFactor, progressFactor), map);
            }
            token.SubmitProgress(1);
        }
        private void ExtractHighResTextureMap(SubProgressToken token, string map)
        {
            token.SubmitStatus($"Extracting textures from {map}");
            token.SubmitProgress(0);

            var outDir = Path.Join(ExtractDir, map);
            Directory.CreateDirectory(outDir);

            var mapDir = Path.Join(MapsDir, map);

            var files = GetUnpackedMapTextureFolders(map)
                .Where((d) =>
                {
                    var name = Path.GetFileName(d);
                    return !name.StartsWith("m_ref_") && !name.EndsWith("_l-tpf-dcx");
                })
                .Select((d) =>
                {
                    var name = Path.GetFileName(d);
                    name = name.Substring(0, name.Length - "-tpf-dcx".Length);
                    return Path.Join(d, $"{name}.dds");
                })
                .ToArray();


            token.SubmitStatus($"Extracting {files.Length} textures from {map}");

            foreach (var f in files)
            {
                File.Copy(f, Path.Join(outDir, Path.GetFileName(f)), false);
            }
            token.SubmitProgress(1);
        }

        public void Overwrtite(SubProgressToken token)
        {
            PartialOverwrtite(token);
        }
        private void PartialOverwrtite(SubProgressToken token)
        {
            var presentMaps = DS3Info.Maps.Where((map) => Directory.Exists(Path.Join(OverwriteDir, map))).ToArray();
            var progressFactor = 1.0 / presentMaps.Length;

            foreach (var (map, i) in presentMaps.Select((m, i) => (m, i)))
            {
                PartialOverwrtiteMap(token.Slice(i * progressFactor, progressFactor), map);
            }
            token.SubmitProgress(1);
        }
        private void PartialOverwrtiteMap(SubProgressToken token, string map)
        {
            token.SubmitStatus($"Overwriting textures for {map}");
            token.SubmitProgress(0);

            var nameMap = GetFileNameMap(map);

            var overwritefiles = Directory.GetFiles(Path.Join(OverwriteDir, map), "m*.dds", SearchOption.TopDirectoryOnly);
            var restoreFiles = LoadRestoreFiles(map, overwritefiles);

            token.SubmitStatus($"Overwriting textures {overwritefiles.Length} textures for {map}");

            var repack = new List<string>();

            foreach (var f in restoreFiles.Concat(overwritefiles))
            {
                var name = Path.GetFileName(f);
                name = name.Substring(0, name.Length - ".dds".Length);

                if (nameMap.TryGetValue(name, out var unpackedPath))
                {
                    File.Copy(f, unpackedPath, true);
                    repack.Add(Path.GetDirectoryName(unpackedPath));
                }
                else
                {
                    throw new Exception($"There is no unpacked file for {f}");
                }
            }

            SaveOverwriteFiles(map, overwritefiles);

            token.SubmitStatus($"Repacking textures {repack.Count} textures for {map}");

            Yabber.Run(repack.ToArray());

            token.SubmitStatus($"Repacking {map} map files");

            Yabber.Run(repack.Select(d => Path.GetDirectoryName(d)).ToHashSet().ToArray());

            token.SubmitProgress(1);
        }
        private void SaveOverwriteFiles(string map, string[] overwriteFiles)
        {
            var s = new StringBuilder();
            foreach (var file in overwriteFiles)
            {
                s.Append(Path.GetFileName(file)).Append("\n");
            }

            File.WriteAllText(Path.Join(MapsDir, map, "tex_overwrites.txt"), s.ToString(), Encoding.UTF8);
        }
        private string[] LoadRestoreFiles(string map, string[] overwriteFiles)
        {
            var path = Path.Join(MapsDir, map, "tex_overwrites.txt");
            if (!File.Exists(path))
                return new string[0];

            var restoreNames = new OverwriteDiff<string>(
                File.ReadAllText(path, Encoding.UTF8).Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0),
                overwriteFiles.Select(Path.GetFileName)
            ).Restore;

            return restoreNames.Select(n => Path.Join(ExtractDir, map, n)).ToArray();
        }
        /// <summary>
        /// Returns a map from the name of each unpacked texture to its path.
        /// </summary>
        private Dictionary<string, string> GetFileNameMap(string map)
        {
            var nameMap = new Dictionary<string, string>();
            foreach (var f in GetUnpackedMapTextureFolders(map))
            {
                var name = Path.GetFileName(f);
                name = name.Substring(0, name.Length - "-tpf-dcx".Length);
                nameMap[name] = Path.Join(f, $"{name}.dds");
            }
            return nameMap;
        }

        public void EnsureBackup(SubProgressToken token)
        {
            token.SubmitStatus("Ensuring backup");
            if (!Directory.Exists(MapsBackupDir))
            {
                token.SubmitStatus("Creating backup");
                try
                {
                    CopyFilesRecursively(new DirectoryInfo(MapsDir), Directory.CreateDirectory(MapsBackupDir), token);
                    token.SubmitProgress(1);
                }
                catch (Exception)
                {
                    token.SubmitStatus("Removing unfinished backup");
                    Directory.Delete(MapsBackupDir, true);
                }
            }
        }
        public void Restore(SubProgressToken token)
        {
            if (!Directory.Exists(MapsBackupDir))
            {
                token.SubmitStatus("Unable to restore. No backup found.");
                return;
            }

            token.SubmitStatus("Removing current map files");
            Directory.Delete(MapsDir, true);

            token.SubmitStatus("Restoring map files from backup");
            CopyFilesRecursively(new DirectoryInfo(MapsBackupDir), Directory.CreateDirectory(MapsDir), token);
        }

        public void PrepareUpscale(SubProgressToken token)
        {
            var progressFactor = 1.0 / DS3Info.Maps.Length;
            foreach (var (map, i) in DS3Info.Maps.Select((m, i) => (m, i)))
            {
                PrepareUpscaleMap(token.Slice(i * progressFactor, progressFactor), map);
            }
            token.SubmitProgress(1);
        }
        private void PrepareUpscaleMap(SubProgressToken token, string map)
        {
            token.SubmitStatus($"Preparing {map} for upscaling");

            var files = Directory.GetFiles(Path.Join(ExtractDir, map), "*.dds", SearchOption.TopDirectoryOnly);

            token.SubmitStatus($"Preparing {map} for upscaling ({files.Length} files)");

            var done = 0;
            files.AsParallel().WithDegreeOfParallelism(8).ForAll(file =>
            {
                lock (token)
                {
                    if (token.IsCanceled) return;
                    token.SubmitProgress(done++ / (double)files.Length);
                }

                string targetDir;
                var lower = file.ToLower();
                if (lower.Contains("_enkei_") || lower.Contains("_low_"))
                    targetDir = "distant";
                else if (lower.Contains("_base_") || lower.Contains("_baser_") || lower.Contains("_water_") || lower.Contains("_sky_") || lower.Contains("_mountain_"))
                    targetDir = "ignore";
                else if (file.EndsWith("_a.dds"))
                    targetDir = "a";
                else if (file.EndsWith("_n.dds"))
                    targetDir = "n";
                else if (file.EndsWith("_r.dds"))
                    targetDir = "r";
                else
                    targetDir = "other";

                try
                {
                    if (targetDir == "a")
                    {
                        using var ddsImage = DDSImage.Load(file);
                        if (ddsImage.HasTransparency()) targetDir = "a_transparent";
                    }

                    targetDir = Path.Join(UpscaleDir, map, targetDir);
                    Directory.CreateDirectory(targetDir);
                    DDSConverter.ToPNG(file, targetDir);
                }
                catch (Exception)
                {
                    // ignore this file
                    return;
                }
            });

            token.SubmitProgress(1);
        }

        private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target, IProgressToken token)
        {
            token.CheckCanceled();

            foreach (DirectoryInfo dir in source.GetDirectories())
            {
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name), token);
            }

            foreach (FileInfo file in source.GetFiles())
            {
                token.CheckCanceled();
                file.CopyTo(Path.Combine(target.FullName, file.Name));
            }
        }

        /// <summary>
        /// The `mXX_000X-tpfbhd` folders of a map.
        /// </summary>
        private string[] GetUnpackedMapFileFolders(string map)
        {
            return Directory.GetDirectories(Path.Join(MapsDir, map), $"{map}*-tpfbhd", SearchOption.TopDirectoryOnly);
        }
        /// <summary>
        /// The `m*-tpf-dcx` folders of a map.
        /// </summary>
        private string[] GetUnpackedMapTextureFolders(string map)
        {
            var list = new List<string>();

            foreach (var folder in GetUnpackedMapFileFolders(map))
                list.AddRange(Directory.GetDirectories(folder, "m*-tpf-dcx", SearchOption.TopDirectoryOnly));

            return list.ToArray();
        }
    }
}

﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;

namespace DS3TexUpUI
{
    public class Workspace
    {
        public string GameDir { get; }
        public string MapsDir => Path.Join(GameDir, "map");
        public string MapsBackupDir => Path.Join(GameDir, "map_old");
        public string ChrDir => Path.Join(GameDir, "chr");
        public string ChrBackupDir => Path.Join(GameDir, "chr_old");
        public string ObjDir => Path.Join(GameDir, "obj");
        public string ObjBackupDir => Path.Join(GameDir, "obj_old");

        public string TextureDir { get; }
        public string ExtractDir => Path.Join(TextureDir, "extract");
        public string ExtractChrDir => Path.Join(ExtractDir, "chr");
        public string ExtractObjDir => Path.Join(ExtractDir, "obj");
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

            UnpackMap(token.Reserve(0.5));
            ExtractHighResMapTexture(token);
        }
        private void UnpackMap(SubProgressToken token)
        {
            UnpackMap(token, f => !f.EndsWith("_l.tpf.dcx"));
        }
        private void UnpackMap(SubProgressToken token, Func<string, bool> fileFilter)
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
        public void UnpackChr(SubProgressToken token)
        {
            token.SubmitStatus("Unpacking all chr files");

            var files = Directory
                .GetFiles(ChrDir, "*.texbnd.dcx", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    // cXXXX
                    var name = Path.GetFileName(f).Substring(0, 5);

                    // c0000 doesn't have any interesting textures
                    // Yabber can't open the files for c6330
                    return name != "c0000" && name != "c6330";
                })
                .ToArray();

            Yabber.RunParallel(token.Reserve(0.5), files);

            token.SubmitStatus("Unpacking all chr .tpf files");

            var tpfFiles = Directory
                .GetDirectories(ChrDir, "c*-texbnd-dcx", SearchOption.TopDirectoryOnly)
                .Select(d =>
                {
                    // cXXXX
                    var n = Path.GetFileName(d).Substring(0, 5);
                    return Path.Join(d, "chr", n, n + ".tpf");
                })
                .ToArray();

            Yabber.RunParallel(token, tpfFiles);
        }
        private void ExtractHighResMapTexture(SubProgressToken token)
        {
            Directory.CreateDirectory(ExtractDir);
            Directory.CreateDirectory(OverwriteDir);

            token.ForAll(DS3Info.Maps, ExtractHighResMapTexture);
        }
        private void ExtractHighResMapTexture(SubProgressToken token, string map)
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

        public void Overwrite(SubProgressToken token)
        {
            PartialOverwrite(token);
        }
        private void PartialOverwrite(SubProgressToken token)
        {
            var presentMaps = DS3Info.Maps.Where((map) => Directory.Exists(Path.Join(OverwriteDir, map))).ToArray();
            token.ForAll(presentMaps, PartialOverwriteMap);
        }
        private void PartialOverwriteMap(SubProgressToken token, string map)
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

            token.SubmitStatus($"Repacking {repack.Count} textures for {map}");

            Yabber.RunParallel(token.Reserve(0.8), repack.ToArray());

            token.SubmitStatus($"Repacking {map} map files");

            Yabber.RunParallel(token, repack.Select(d => Path.GetDirectoryName(d)).ToHashSet().ToArray());

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

        private (string name, string original, string backup)[] GetBackups()
        {
            return new (string name, string original, string backup)[] {
                ("chr", ChrDir, ChrBackupDir),
                ("map", MapsDir, MapsBackupDir),
                ("obj", ObjDir, ObjBackupDir),
            };
        }
        public void EnsureBackup(SubProgressToken token)
        {
            token.ForAll(GetBackups(), (token, backup) =>
            {
                EnsureBackup(token, backup.name, backup.original, backup.backup);
            });
        }
        private static void EnsureBackup(SubProgressToken token, string name, string original, string backup)
        {
            token.SubmitStatus($"Ensuring {name} backup");
            if (!Directory.Exists(backup))
            {
                token.SubmitStatus($"Creating {name} backup");
                try
                {
                    CopyFilesRecursively(new DirectoryInfo(original), Directory.CreateDirectory(backup), token);
                    token.SubmitProgress(1);
                }
                catch (Exception)
                {
                    token.SubmitStatus($"Removing unfinished {name} backup");
                    Directory.Delete(backup, true);
                }
            }
        }
        public void Restore(SubProgressToken token)
        {
            token.ForAll(GetBackups(), (token, backup) =>
            {
                Restore(token, backup.name, backup.original, backup.backup);
            });
        }
        private static void Restore(SubProgressToken token, string name, string original, string backup)
        {
            if (!Directory.Exists(backup))
            {
                token.SubmitStatus($"Unable to restore. No {name} backup found.");
                return;
            }

            token.SubmitStatus($"Removing current {name} files");
            Directory.Delete(original, true);

            token.SubmitStatus($"Restoring {name} files from backup");
            CopyFilesRecursively(new DirectoryInfo(backup), Directory.CreateDirectory(original), token);
        }

        public void PrepareUpscale(SubProgressToken token)
        {
            token.ForAll(DS3Info.Maps, PrepareUpscaleMap);
        }
        private void PrepareUpscaleMap(SubProgressToken token, string map)
        {
            token.SubmitStatus($"Preparing {map} for upscaling");

            var files = Directory.GetFiles(Path.Join(ExtractDir, map), "*.dds", SearchOption.TopDirectoryOnly);

            token.SubmitStatus($"Preparing {map} for upscaling ({files.Length} files)");

            var ignorePattern = new Regex(@"\Am[\d_]+_(?i:base|sky|mountain)[_\darnme]+\z");

            token.ForAll(files.AsParallel().WithDegreeOfParallelism(8), files.Length, file =>
            {
                string targetDir;
                var name = Path.GetFileNameWithoutExtension(file).ToLower();
                if (name.Contains("_enkei_") || name.Contains("_low_"))
                    targetDir = "distant";
                else if (ignorePattern.IsMatch(name))
                    targetDir = "ignore";
                else if (name.EndsWith("_a"))
                    targetDir = "a";
                else if (name.EndsWith("_n"))
                    targetDir = "n";
                else if (name.EndsWith("_r"))
                    targetDir = "r";
                else
                    targetDir = "other";

                var png = Path.GetFileNameWithoutExtension(file) + ".png";

                try
                {
                    if (targetDir == "n")
                    {
                        var image = DS3NormalMap.Load(file);

                        var normalDir = Path.Join(UpscaleDir, map, "n_normal");
                        var glossDir = Path.Join(UpscaleDir, map, "n_gloss");
                        var heightDir = Path.Join(UpscaleDir, map, "n_height");
                        Directory.CreateDirectory(normalDir);
                        Directory.CreateDirectory(glossDir);
                        Directory.CreateDirectory(heightDir);

                        image.Normals.SaveAsPng(Path.Join(normalDir, png));
                        image.Gloss.SaveAsPng(Path.Join(glossDir, png));
                        if (image.Heights.IsNoticeable())
                            image.Heights.SaveAsPng(Path.Join(heightDir, png));
                    }
                    else
                    {
                        using var image = DDSImage.Load(file);

                        if (image.IsSolidColor(0.05))
                        {
                            // there is no point in upscaling a solid color.
                            targetDir = "ignore";
                        }
                        else
                        {
                            if (targetDir == "a")
                            {
                                var t = image.GetTransparency();
                                if (t == TransparencyKind.Binary)
                                    targetDir = "a_t_binary";
                                else if (t == TransparencyKind.Full)
                                    targetDir = "a_t_full";
                            }
                        }

                        targetDir = Path.Join(UpscaleDir, map, targetDir);
                        Directory.CreateDirectory(targetDir);
                        image.SaveAsPng(Path.Join(targetDir, png));
                    }
                }
                catch (Exception)
                {
                    // ignore this file
                    return;
                }
            });
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

        public Dictionary<string, ChrId[]> GroupCharacterFiles()
        {
            var dirs = Directory.GetDirectories(ChrDir);

            var files = new List<(string name, ChrId id)>();

            foreach (var d in dirs)
            {
                // cXXXX
                var name = Path.GetFileName(d).Substring(0, 5);
                var id = ChrId.Parse(name);

                var p = Path.Join(d, "chr", name, name + "-tpf");
                var all = Directory.GetFiles(p)
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Where(f => f != "_yabber-tpf");

                foreach (var f in all)
                {
                    files.Add((f, id));
                }
            }

            var values = files
                .GroupBy(f => f.name)
                .Select(g => new KeyValuePair<string, ChrId[]>(g.Key, g.Select(f => f.id).ToArray()));

            return new Dictionary<string, ChrId[]>(values);
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

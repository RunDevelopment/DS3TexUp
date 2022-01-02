using System;
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
        public string PartsDir => Path.Join(GameDir, "parts");
        public string PartsBackupDir => Path.Join(GameDir, "parts_old");

        public string TextureDir { get; }
        public string ExtractDir => Path.Join(TextureDir, "extract");
        public string ExtractChrDir => Path.Join(ExtractDir, "chr");
        public string ExtractObjDir => Path.Join(ExtractDir, "obj");
        public string ExtractPartsDir => Path.Join(ExtractDir, "parts");
        public string OverwriteDir => Path.Join(TextureDir, "overwrite");
        public string UpscaleDir => Path.Join(TextureDir, "upscale");
        public string CopyIndexFile => Path.Join(TextureDir, "copy.index");
        public string CopyEquivalenceFile => Path.Join(TextureDir, "copies.txt");

        public Workspace(string gameDir, string textureDir)
        {
            GameDir = gameDir;
            TextureDir = textureDir;
        }

        public void Extract(SubProgressToken token)
        {
            EnsureBackup(token.Reserve(0));

            token.SplitEqually(
                UnpackMap,
                ExtractHighResMapTexture,
                UnpackChr,
                ExtractChrTexture,
                UnpackObj,
                ExtractObjTexture,
                UnpackParts,
                ExtractPartsTexture
            );
        }
        private void UnpackMap(SubProgressToken token)
        {
            UnpackMap(token, f => !f.EndsWith("_l.tpf.dcx"));
        }
        private void UnpackMap(SubProgressToken token, Func<string, bool> fileFilter)
        {
            token.SubmitStatus("Unpacking all map files");

            foreach (var (map, i) in DS3.Maps.Select((m, i) => (m, i)))
            {
                token.SubmitStatus($"Unpacking {map} map files");
                token.SubmitProgress(i / (double)DS3.Maps.Length);

                var mapDir = Path.Join(MapsDir, map);
                Yabber.Run(Directory.GetFiles(mapDir, $"{map}*.tpfbhd", SearchOption.TopDirectoryOnly));

                var files = new List<string>();
                foreach (var unpackedDir in GetUnpackedMapFileFolders(map))
                {
                    files.AddRange(Directory.GetFiles(unpackedDir, $"m*.tpf.dcx", SearchOption.TopDirectoryOnly).Where(fileFilter));
                }

                token.SubmitStatus($"Unpacking {map} textures ({files.Count})");
                Yabber.Run(token.Slice(i / (double)DS3.Maps.Length, 1.0 / DS3.Maps.Length), files.ToArray());
            }
            token.SubmitProgress(1);
        }
        private void UnpackChr(SubProgressToken token)
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
        private void UnpackObj(SubProgressToken token)
        {
            token.SubmitStatus("Unpacking all obj files");

            var files = Directory
                .GetFiles(ObjDir, "*objbnd.dcx", SearchOption.TopDirectoryOnly)
                .ToArray();

            token.SubmitStatus($"Unpacking {files.Length} obj files");

            Yabber.RunParallel(token.Reserve(0.5), files);

            token.SubmitStatus("Unpacking all obj .tpf files");

            var tpfFiles = Directory
                .GetDirectories(ObjDir, "o*-objbnd-dcx", SearchOption.TopDirectoryOnly)
                .Select(d =>
                {
                    // oXXXXXX
                    var id = Path.GetFileName(d).Substring(0, 7);
                    return Path.Join(d, "obj", id.Substring(0, 3), id, id + ".tpf");
                })
                .Where(File.Exists)
                .ToArray();

            Yabber.RunParallel(token, tpfFiles);
        }
        private void UnpackParts(SubProgressToken token)
        {
            token.SubmitStatus("Unpacking all parts files");

            var files = Directory
                .GetFiles(PartsDir, "*.partsbnd.dcx", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f))
                .Select(f => f.Substring(0, f.Length - ".partsbnd.dcx".Length))
                .Intersect(DS3.Parts)
                .Select(f => Path.Join(PartsDir, f + ".partsbnd.dcx"))
                .ToArray();

            token.SubmitStatus($"Unpacking {files.Length} parts files");

            Yabber.RunParallel(token.Reserve(0.5), files);

            token.SubmitStatus("Unpacking all parts .tpf files");

            var tpfFiles = Directory
                .GetDirectories(PartsDir, "*-partsbnd-dcx", SearchOption.TopDirectoryOnly)
                .Select(d =>
                {
                    // XX_X_XXXX
                    var id = Path.GetFileName(d).Substring(0, 9).ToUpperInvariant();
                    var type = id.StartsWith("WP") ? "Weapon" : "FullBody";
                    return Path.Join(d, "parts", type, id, id + ".tpf");
                })
                .Where(File.Exists)
                .ToArray();

            Yabber.RunParallel(token, tpfFiles);
        }
        private void ExtractHighResMapTexture(SubProgressToken token)
        {
            Directory.CreateDirectory(ExtractDir);
            Directory.CreateDirectory(OverwriteDir);

            token.ForAll(DS3.Maps, ExtractHighResMapTexture);
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
        private void ExtractChrTexture(SubProgressToken token)
        {
            token.SubmitStatus($"Extracting chr textues");
            token.SubmitProgress(0);

            var wantedFiles = DS3.CharacterFiles;

            var outDir = Path.Join(ExtractChrDir);
            Directory.CreateDirectory(outDir);

            var files = Directory
                .GetDirectories(ChrDir, "c*-texbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // cXXXX
                    var n = Path.GetFileName(d).Substring(0, 5);
                    return Directory
                        .GetFiles(Path.Join(d, "chr", n, n + "-tpf"), "*.dds", SearchOption.TopDirectoryOnly)
                        .Where(f => wantedFiles.ContainsKey(Path.GetFileNameWithoutExtension(f)));
                })
                .ToArray();

            token.SubmitStatus($"Extracting {files.Length} chr textures");
            token.ForAll(files, f => File.Copy(f, Path.Join(outDir, Path.GetFileName(f)), true));
        }
        private void ExtractObjTexture(SubProgressToken token)
        {
            token.SubmitStatus($"Extracting obj textues");
            token.SubmitProgress(0);

            var outDir = Path.Join(ExtractObjDir);
            Directory.CreateDirectory(outDir);

            var files = Directory
                .GetDirectories(ObjDir, "o*-objbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // oXXXXXX
                    var id = Path.GetFileName(d).Substring(0, 7);

                    var tpf = Path.Join(d, "obj", id.Substring(0, 3), id, id + "-tpf");
                    if (!Directory.Exists(tpf)) return new (string, string)[0];

                    return Directory
                        .GetFiles(tpf, "*.dds", SearchOption.TopDirectoryOnly)
                        .Select(f => (id, f));
                })
                .ToArray();

            token.SubmitStatus($"Extracting {files.Length} obj textures");
            token.ForAll(files, pair =>
            {
                var (id, file) = pair;
                File.Copy(file, Path.Join(outDir, id + "_" + Path.GetFileName(file)), false);
            });
        }
        private void ExtractPartsTexture(SubProgressToken token)
        {
            token.SubmitStatus($"Extracting parts textues");
            token.SubmitProgress(0);

            var outDir = Path.Join(ExtractPartsDir);
            Directory.CreateDirectory(outDir);

            var files = Directory
                .GetDirectories(PartsDir, "*-partsbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // XX_X_XXXX
                    var id = Path.GetFileName(d).Substring(0, 9).ToUpperInvariant();
                    var type = id.StartsWith("WP") ? "Weapon" : "FullBody";

                    var tpf = Path.Join(d, "parts", type, id, id + "-tpf");

                    if (!Directory.Exists(tpf)) return new (string, string)[0];

                    return Directory
                        .GetFiles(tpf, "*.dds", SearchOption.TopDirectoryOnly)
                        .Select(f => (id, f));
                })
                .ToArray();

            token.SubmitStatus($"Extracting {files.Length} parts textures");
            token.ForAll(files, pair =>
            {
                var (id, file) = pair;
                File.Copy(file, Path.Join(outDir, id + "_" + Path.GetFileName(file)), false);
            });
        }

        public void FindCopies(SubProgressToken token)
        {
            token.SplitEqually(
                BuildCopyIndex,
                LookupCopyEquivalenceClasses
            );
        }
        private void BuildCopyIndex(SubProgressToken token)
        {
            if (File.Exists(CopyIndexFile)) return;

            token.SubmitStatus("Search for files");

            var files = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);
            var index = new CopyIndex();

            token.SubmitStatus($"Indexing {files.Length} files");
            token.ForAll(files.AsParallel(), files.Length, f =>
            {
                try
                {
                    var image = f.LoadTextureMap();
                    index.AddImage(image, f);
                }
                catch (System.Exception)
                {
                    // ignore
                }
            });

            token.SubmitStatus($"Saving index");
            index.Save(CopyIndexFile);
        }
        private void LookupCopyEquivalenceClasses(SubProgressToken token)
        {
            token.SubmitStatus("Loading index");
            var index = CopyIndex.Load(CopyIndexFile);

            var files = Directory.GetFiles(ExtractDir, "*.dds", SearchOption.AllDirectories);
            var fileIds = files.Select((f, i) => (f, i)).ToDictionary(p => p.f, p => p.i);
            var fileSizes = new (int, int)[files.Length];
            foreach (var e in index.Entries)
            {
                if (fileIds.TryGetValue(e.File, out var id))
                    fileSizes[id] = (e.Width, e.Height);
            }

            var eqRelations = new List<int[]>();

            token.SubmitStatus($"Looking up {files.Length} files");
            token.ForAll(files.AsParallel(), files.Length, f =>
            {
                try
                {
                    var similar = index.GetSimilar(f);
                    if (similar == null || similar.Count < 2)
                        return;
                    var array = similar.Select(e => fileIds[e.File]).ToArray();

                    lock (eqRelations)
                    {
                        eqRelations.Add(array);
                    }
                }
                catch (System.Exception)
                {
                    // ignore
                }
            });

            token.SubmitStatus("Finding equivalence classes");

            var eqClasses = SetEquivalence.MergeOverlapping(eqRelations, files.Length);

            var lines = eqClasses
                 .Where(e => e.Count >= 2)
                 .Select(e =>
                 {
                     e.Sort((a, b) => fileSizes[a].Item1.CompareTo(fileSizes[b].Item1));
                     return string.Join(";", e.Select(i => files[i]));
                 })
                 .ToList();

            token.SubmitStatus($"Writing results");

            File.WriteAllText(CopyEquivalenceFile, string.Join("\n", lines), Encoding.UTF8);
        }

        public void Overwrite(SubProgressToken token)
        {
            PartialOverwrite(token);
        }
        private void PartialOverwrite(SubProgressToken token)
        {
            var presentMaps = DS3.Maps.Where((map) => Directory.Exists(Path.Join(OverwriteDir, map))).ToArray();
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
                ("parts", PartsDir, PartsBackupDir),
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
            token.SplitEqually(
                token => token.ForAll(DS3.Maps, PrepareUpscaleMap),
                PrepareUpscaleChr,
                PrepareUpscaleObj,
                PrepareUpscaleParts
            );
        }
        private void PrepareUpscaleMap(SubProgressToken token, string map)
        {
            var ignorePattern = new Regex(@"\Am[\d_]+_(?i:base|sky|mountain)[_\darnme]+\z");
            PrepareUpscaleDirectory(token, map, ignorePattern.IsMatch);
        }
        private void PrepareUpscaleChr(SubProgressToken token)
        {
            PrepareUpscaleDirectory(token, "chr");
        }
        private void PrepareUpscaleObj(SubProgressToken token)
        {
            PrepareUpscaleDirectory(token, "obj");
        }
        private void PrepareUpscaleParts(SubProgressToken token)
        {
            PrepareUpscaleDirectory(token, "parts");
        }
        private void PrepareUpscaleDirectory(SubProgressToken token, string name, Func<string, bool> ignore = null)
        {
            ignore ??= _ => false;

            var sourceDir = Path.Join(ExtractDir, name);
            var targetDir = Path.Join(UpscaleDir, name);

            token.SubmitStatus($"Preparing {name} for upscaling");

            var files = Directory.GetFiles(sourceDir, "*.dds", SearchOption.TopDirectoryOnly);

            token.SubmitStatus($"Preparing {name} for upscaling ({files.Length} files)");

            token.ForAll(files.AsParallel().WithDegreeOfParallelism(8), files.Length, file =>
            {
                CategorizeTexture(file, targetDir, ignore);
            });
        }
        private static void CategorizeTexture(string tex, string outDir, Func<string, bool> ignore)
        {
            static string JoinFile(params string[] parts)
            {
                var dir = Path.Join(parts);
                Directory.CreateDirectory(Path.GetDirectoryName(dir));
                return dir;
            }

            var name = Path.GetFileNameWithoutExtension(tex);
            var png = name + ".png";

            try
            {
                using var image = DDSImage.Load(tex);

                if (ignore(name))
                {
                    image.SaveAsPng(JoinFile(outDir, "ignore", png));
                    return;
                }

                if (name.EndsWith("_n"))
                {
                    var normalImage = DS3NormalMap.Of(image);

                    normalImage.Normals.SaveAsPng(JoinFile(outDir, "n_normal", png));
                    normalImage.Gloss.SaveAsPng(JoinFile(outDir, "n_gloss", png));
                    if (normalImage.Heights.IsNoticeable())
                        normalImage.Heights.SaveAsPng(JoinFile(outDir, "n_height", png));
                    return;
                }

                if (image.IsSolidColor(0.05))
                {
                    // there is no point in upscaling a solid color.
                    image.SaveAsPng(JoinFile(outDir, "ignore", png));
                    return;
                }

                var target = "other";
                if (name.EndsWith("_a"))
                {
                    var transparency = image.GetTransparency();

                    target = transparency switch
                    {
                        TransparencyKind.Binary => "a_alpha_binary",
                        TransparencyKind.Full => "a_alpha_full",
                        _ => "a"
                    };

                    if (transparency == TransparencyKind.Binary || transparency == TransparencyKind.Full)
                    {
                        var (color, alpha) = image.ToTextureMap().SplitAlphaBlack();
                        color.SaveAsPng(JoinFile(outDir, target, "color_" + png));
                        alpha.SaveAsPng(JoinFile(outDir, target, "alpha_" + png));
                        return;
                    }
                }
                else if (name.EndsWith("_r"))
                    target = "r";
                else if (name.EndsWith("_s"))
                    target = "s";
                else if (name.EndsWith("_em") || name.EndsWith("_e"))
                    target = "em";

                image.SaveAsPng(JoinFile(outDir, target, png));
            }
            catch (Exception)
            {
                // ignore error
            }
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

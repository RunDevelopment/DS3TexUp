using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

#nullable enable

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
        public string SfxDir => Path.Join(GameDir, "sfx");
        public string SfxBackupDir => Path.Join(GameDir, "sfx_old");

        public string TextureDir { get; }
        public string ExtractDir => Path.Join(TextureDir, "extract");
        public string ExtractChrDir => Path.Join(ExtractDir, "chr");
        public string ExtractObjDir => Path.Join(ExtractDir, "obj");
        public string ExtractPartsDir => Path.Join(ExtractDir, "parts");
        public string ExtractSfxDir => Path.Join(ExtractDir, "sfx");

        public string OverwriteDir => Path.Join(TextureDir, "overwrite");
        public string UpscaleDir => Path.Join(TextureDir, "upscale");

        public string LastOverwritesFile => Path.Join(GameDir, "last-overwrites.json");

        public Workspace(string gameDir, string textureDir)
        {
            GameDir = gameDir;
            TextureDir = textureDir;
        }

        public string GetExtractPath(TexId id)
        {
            return Path.Join(ExtractDir, id.Category, $"{id.Name.ToString()}.dds");
        }
        public string GetGamePath(TexId id)
        {
            if (DS3.GamePath.TryGetValue(id, out var relative))
                return Path.Join(GameDir, relative);
            throw new Exception($"Unknown tex id: {id}");
        }
        public string GetOverwritePath(TexId id)
        {
            return Path.Join(OverwriteDir, id.Category, $"{id.Name.ToString()}.dds");
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
                ExtractPartsTexture,
                UnpackSfx,
                ExtractSfxTexture
            );
        }
        private void UnpackMap(SubProgressToken token)
        {
            UnpackMap(token, f => !f.EndsWith("_l.tpf.dcx"));
        }
        private void UnpackMap(SubProgressToken token, Func<string, bool> fileFilter)
        {
            token.SubmitStatus("Unpacking all map files");

            token.ForAll(DS3.Maps, (token, map) =>
            {
                token.SubmitStatus($"Unpacking {map} map files");

                var mapDir = Path.Join(MapsDir, map);
                Yabber.RunParallel(token, Directory.GetFiles(mapDir, $"{map}*.tpfbhd", SearchOption.TopDirectoryOnly));

                var files = new List<string>();
                foreach (var unpackedDir in GetUnpackedMapFileFolders(map))
                {
                    files.AddRange(Directory.GetFiles(unpackedDir, $"m*.tpf.dcx", SearchOption.TopDirectoryOnly).Where(fileFilter));
                }

                token.SubmitStatus($"Unpacking {map} textures ({files.Count})");
                Yabber.RunParallel(token, files.ToArray());
            });
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
                .Where(f => !f.EndsWith("_l.partsbnd.dcx"))
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
                    var type = GetPartsType(id);
                    return Path.Join(d, "parts", type, id, id + ".tpf");
                })
                .Where(File.Exists)
                .ToArray();

            Yabber.RunParallel(token, tpfFiles);

            token.SubmitStatus("Unpacking common body");
            Yabber.Run(Path.Join(PartsDir, "common_body.tpf.dcx"));
        }
        private void UnpackSfx(SubProgressToken token)
        {
            token.SubmitStatus("Unpacking all sfx files");

            var files = Directory.GetFiles(SfxDir, "*_resource.ffxbnd.dcx", SearchOption.TopDirectoryOnly);

            token.SubmitStatus($"Unpacking {files.Length} sfx files");

            Yabber.RunParallel(token.Reserve(0.5), files);

            token.SubmitStatus("Unpacking all sfx .tpf files");

            var tpfFiles = Directory
                .GetDirectories(SfxDir, "*_resource-ffxbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    var path = Path.Join(d, "sfx", "tex");
                    if (!Directory.Exists(path)) return new string[0];
                    return Directory.GetFiles(path, "*.tpf", SearchOption.TopDirectoryOnly);
                })
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
            var files = GetHighResMapTexFiles(token.Reserve(0.1), map);

            token.SubmitStatus($"Extracting {files.Count} textures from {map}");

            Directory.CreateDirectory(Path.Join(ExtractDir, map));
            token.ForAllParallel(files, file =>
            {
                File.Copy(file.GamePath, GetExtractPath(file.Id));
            });
        }
        private void ExtractChrTexture(SubProgressToken token)
        {
            var files = GetChrTexFiles(token.Reserve(0.1));

            token.SubmitStatus($"Extracting {files.Count} chr textures");

            Directory.CreateDirectory(Path.Join(ExtractChrDir));
            token.ForAllParallel(files, file =>
            {
                File.Copy(file.GamePath, GetExtractPath(file.Id));
            });
        }
        private void ExtractObjTexture(SubProgressToken token)
        {
            var files = GetObjTexFiles(token.Reserve(0.1));

            token.SubmitStatus($"Extracting {files.Count} obj textures");

            Directory.CreateDirectory(Path.Join(ExtractObjDir));
            token.ForAllParallel(files, file =>
            {
                File.Copy(file.GamePath, GetExtractPath(file.Id));
            });
        }
        private void ExtractPartsTexture(SubProgressToken token)
        {
            var files = GetPartsTexFiles(token.Reserve(0.1));

            token.SubmitStatus($"Extracting {files.Count} parts textures");

            Directory.CreateDirectory(Path.Join(ExtractPartsDir));
            token.ForAllParallel(files, file =>
            {
                File.Copy(file.GamePath, GetExtractPath(file.Id));
            });
        }
        private void ExtractSfxTexture(SubProgressToken token)
        {
            var files = GetSfxTexFiles(token.Reserve(0.1));

            token.SubmitStatus($"Extracting {files.Count} sfx textures");

            Directory.CreateDirectory(Path.Join(ExtractSfxDir));
            token.ForAllParallel(files, file =>
            {
                File.Copy(file.GamePath, GetExtractPath(file.Id));
            });
        }

        public List<TexFile> GetTexFiles(SubProgressToken token)
        {
            var result = new List<TexFile>();
            token.SplitEqually(
                token => result.AddRange(GetHighResMapTexFiles(token)),
                token => result.AddRange(GetChrTexFiles(token)),
                token => result.AddRange(GetObjTexFiles(token)),
                token => result.AddRange(GetPartsTexFiles(token)),
                token => result.AddRange(GetSfxTexFiles(token))
            );
            return result;
        }
        private List<TexFile> GetHighResMapTexFiles(SubProgressToken token)
        {
            var result = new List<TexFile>();
            token.ForAll(DS3.Maps, (token, map) => result.AddRange(GetHighResMapTexFiles(token, map)));
            return result;
        }
        private List<TexFile> GetHighResMapTexFiles(SubProgressToken token, string map)
        {
            token.SubmitStatus($"Searching for {map} texture files");
            token.SubmitProgress(0);

            var result = GetUnpackedMapTextureFolders(map)
                .Where((d) =>
                {
                    var name = Path.GetFileName(d);
                    return !name.StartsWith("m_ref_") && !name.EndsWith("_l-tpf-dcx");
                })
                .Select((d) =>
                {
                    var name = Path.GetFileName(d);
                    name = name.Substring(0, name.Length - "-tpf-dcx".Length);
                    return new TexFile()
                    {
                        GamePath = Path.Join(d, $"{name}.dds"),
                        Id = new TexId(map, name)
                    };
                });

            token.SubmitProgress(1);
            return result.ToList();
        }
        private List<TexFile> GetChrTexFiles(SubProgressToken token)
        {
            token.SubmitStatus($"Searching for chr texture files");
            token.SubmitProgress(0);

            var result = Directory
                .GetDirectories(ChrDir, "c*-texbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // cXXXX
                    var id = Path.GetFileName(d).Substring(0, 5);
                    return Directory
                        .GetFiles(Path.Join(d, "chr", id, id + "-tpf"), "*.dds", SearchOption.TopDirectoryOnly)
                        .Select(f =>
                        {
                            return new TexFile()
                            {
                                GamePath = f,
                                Id = new TexId("chr", $"{id}_{Path.GetFileNameWithoutExtension(f)}")
                            };
                        });
                });

            token.SubmitProgress(1);
            return result.ToList();
        }
        private List<TexFile> GetObjTexFiles(SubProgressToken token)
        {
            token.SubmitStatus($"Searching for obj texture files");
            token.SubmitProgress(0);

            var result = Directory
                .GetDirectories(ObjDir, "o*-objbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // oXXXXXX
                    var id = Path.GetFileName(d).Substring(0, 7);

                    var tpf = Path.Join(d, "obj", id.Substring(0, 3), id, id + "-tpf");
                    if (!Directory.Exists(tpf)) return new TexFile[0];

                    return Directory
                        .GetFiles(tpf, "*.dds", SearchOption.TopDirectoryOnly)
                        .Select(f =>
                        {
                            return new TexFile()
                            {
                                GamePath = f,
                                Id = new TexId("obj", $"{id}_{Path.GetFileNameWithoutExtension(f)}")
                            };
                        });
                });

            token.SubmitProgress(1);
            return result.ToList();
        }
        private List<TexFile> GetPartsTexFiles(SubProgressToken token)
        {
            token.SubmitStatus($"Searching for parts texture files");
            token.SubmitProgress(0);

            var result = Directory
                .GetDirectories(PartsDir, "*-partsbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // XX_X_XXXX
                    var id = Path.GetFileName(d).Substring(0, 9).ToUpperInvariant();
                    var type = GetPartsType(id);

                    var tpf = Path.Join(d, "parts", type, id, id + "-tpf");

                    if (!Directory.Exists(tpf)) return new TexFile[0];

                    return Directory
                        .GetFiles(tpf, "*.dds", SearchOption.TopDirectoryOnly)
                        .Select(f =>
                        {
                            return new TexFile()
                            {
                                GamePath = f,
                                Id = new TexId("parts", $"{id}_{Path.GetFileNameWithoutExtension(f)}"),
                            };
                        });
                })
                .ToList();

            var body = Directory
                .GetFiles(Path.Join(PartsDir, "common_body-tpf-dcx"), "*.dds")
                .Select(f =>
                {
                    return new TexFile()
                    {
                        GamePath = f,
                        Id = new TexId("parts", $"common_body_{Path.GetFileNameWithoutExtension(f)}"),
                    };
                })
                .ToList();
            result.AddRange(body);

            token.SubmitProgress(1);
            return result;
        }
        private List<TexFile> GetSfxTexFiles(SubProgressToken token)
        {
            token.SubmitStatus($"Searching for sfx texture files");
            token.SubmitProgress(0);

            var result = Directory
                .GetDirectories(SfxDir, "*_resource-ffxbnd-dcx", SearchOption.TopDirectoryOnly)
                .SelectMany(d =>
                {
                    // frpg_sfxbnd_XXXXX_resource-ffxbnd-dcx
                    var id = Path.GetFileName(d).Substring("frpg_sfxbnd_".Length);
                    id = id.Substring(0, id.Length - "_resource-ffxbnd-dcx".Length);

                    var path = Path.Join(d, "sfx", "tex");
                    if (!Directory.Exists(path)) return new TexFile[0];

                    return Directory
                        .GetDirectories(path, "*-tpf", SearchOption.TopDirectoryOnly)
                        .Select(d =>
                        {
                            var name = Path.GetFileName(d);
                            name = name.Substring(0, name.Length - "-tpf".Length);
                            return new TexFile()
                            {
                                GamePath = Path.Join(d, name + ".dds"),
                                Id = new TexId("sfx", $"{id}_{name}"),
                            };
                        });
                });

            token.SubmitProgress(1);
            return result.ToList();
        }

        public void Overwrite(SubProgressToken token, Func<TexId, bool>? assumeUnchanged = null)
        {
            token.SubmitStatus($"Searching for overwrite files");

            var dirs = DS3.Maps.Union(new string[] { "chr", "obj", "parts", "sfx" });
            var ids = dirs
                .Select(d => Path.Join(OverwriteDir, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.dds", SearchOption.TopDirectoryOnly))
                .Select(TexId.FromPath)
                .ToList();

            Overwrite(token, ids, assumeUnchanged);
        }
        public void Overwrite(SubProgressToken token, IEnumerable<TexId> textures, Func<TexId, bool>? assumeUnchanged = null)
        {
            token.SubmitStatus($"Filtering overwrite files");
            var valid = textures.Where(id => DS3.GamePath.ContainsKey(id)).ToHashSet();
            OverwriteValidSet(token, valid, assumeUnchanged ?? (id => false));
        }
        private void OverwriteValidSet(SubProgressToken token, HashSet<TexId> overwrite, Func<TexId, bool> assumeUnchanged)
        {
            token.SubmitStatus($"Restoring previous overwrites");
            var lastOverwrites = File.Exists(LastOverwritesFile)
                ? LastOverwritesFile.LoadJsonFile<Dictionary<TexId, string>>()
                : new Dictionary<TexId, string>();

            token.SubmitStatus($"Restoring previous overwrites");
            var restore = lastOverwrites.Keys.Except(overwrite).ToList();
            SetEmpty(lastOverwrites, restore);
            lastOverwrites.SaveAsJson(LastOverwritesFile, false);
            token.Reserve(0.33).ForAllParallel(restore, id => File.Copy(GetExtractPath(id), GetGamePath(id), true));
            token.SubmitLog($"Restored {restore.Count} files");

            token.SubmitStatus($"Filtering out unchanged overwrites");
            var newOverwrites = new Dictionary<TexId, string>();
            token.Reserve(0.1).ForAllParallel(overwrite.Where(id => !assumeUnchanged(id)), id =>
            {
                var oldHash = lastOverwrites.GetOrDefault(id, "");
                var newHash = HashFile(GetOverwritePath(id));
                if (oldHash != newHash)
                {
                    lock (newOverwrites)
                    {
                        newOverwrites[id] = newHash;
                    }
                }
            });

            token.SubmitStatus($"Overwriting textures");
            SetEmpty(lastOverwrites, newOverwrites.Keys);
            lastOverwrites.SaveAsJson(LastOverwritesFile, false);
            token.Reserve(0.33).ForAllParallel(newOverwrites.Keys, id => File.Copy(GetOverwritePath(id), GetGamePath(id), true));
            token.SubmitLog($"Overwrote {newOverwrites.Count} files");

            Repack(token, newOverwrites.Keys.Union(restore).ToHashSet());

            token.SubmitStatus($"Saving overwrites log");
            restore.ForEach(id => lastOverwrites.Remove(id));
            foreach (var (id, hash) in newOverwrites)
                lastOverwrites[id] = hash;
            lastOverwrites.SaveAsJson(LastOverwritesFile, false);

            static string HashFile(string path)
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(path);
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            static void SetEmpty(Dictionary<TexId, string> d, IEnumerable<TexId> ids)
            {
                foreach (var id in ids)
                    d[id] = "";
            }
        }
        private void Repack(SubProgressToken token, HashSet<TexId> textures)
        {
            token.SubmitStatus($"Repacking {textures.Count} textures");

            Repack(token, textures.Select(GetGamePath));
        }
        private void Repack(SubProgressToken token, IEnumerable<string> files)
        {
            token.CheckCanceled();

            static HashSet<string> GetYabberDirs(IEnumerable<string> dirs)
            {
                var result = new HashSet<string>();
                var seen = new HashSet<string>();

                var current = dirs.ToHashSet();
                while (current.Count > 0)
                {
                    var next = new HashSet<string>();
                    foreach (var d in current)
                    {
                        if (seen.Contains(d)) continue;
                        seen.Add(d);

                        if (Directory.GetFiles(d, "_yabber*.xml", SearchOption.TopDirectoryOnly).Any())
                            result.Add(d);

                        var p = Path.GetDirectoryName(d);
                        if (p != null && p != "")
                            next.Add(p);
                    }
                    current = next;
                }

                return result;
            }
            static int CountParts(string path) => path.Count(c => c == '/' || c == '\\');
            static HashSet<string> GetIndependentSubTrees(HashSet<string> dirs)
            {
                HashSet<string> dependedUpon = new HashSet<string>();
                void AddToDependedUpon(string? dir)
                {
                    while (dir != null && dir != "")
                    {
                        if (!dependedUpon.Add(dir)) break;
                        dir = Path.GetDirectoryName(dir);
                    }
                }

                var result = new HashSet<string>();
                foreach (var d in dirs.OrderByDescending(CountParts))
                {
                    if (!dependedUpon.Contains(d))
                        result.Add(d);
                    AddToDependedUpon(d);
                }
                return result;
            }

            static IEnumerable<string> GetParentDirs(IEnumerable<string> files)
            {
                return files.Select(f => Path.GetDirectoryName(f)!);
            }

            var dirs = GetYabberDirs(GetParentDirs(files));
            while (dirs.Count > 0)
            {
                var current = GetIndependentSubTrees(dirs);
                Yabber.RunParallel(token.Reserve(current.Count / (double)dirs.Count), current.ToArray());
                dirs.ExceptWith(current);
            }

            token.SubmitProgress(1);
        }

        private (string name, string original, string backup)[] GetBackups()
        {
            return new (string name, string original, string backup)[] {
                ("chr", ChrDir, ChrBackupDir),
                ("map", MapsDir, MapsBackupDir),
                ("obj", ObjDir, ObjBackupDir),
                ("parts", PartsDir, PartsBackupDir),
                ("sfx", SfxDir, SfxBackupDir),
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
                    Files.CopyFilesRecursively(token, new DirectoryInfo(original), Directory.CreateDirectory(backup));
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
            Files.CopyFilesRecursively(token, new DirectoryInfo(backup), Directory.CreateDirectory(original));
        }

        public void PrepareUpscale(SubProgressToken token)
        {
            var tasks = new List<Action<SubProgressToken>>();
            foreach (var map in DS3.Maps)
                tasks.Add(t => PrepareUpscaleMap(t, map));

            tasks.AddRange(new Action<SubProgressToken>[]{
                PrepareUpscaleChr,
                PrepareUpscaleObj,
                PrepareUpscaleParts,
                PrepareUpscaleSfx
            });

            token.SplitEqually(tasks.ToArray());
        }
        private void PrepareUpscaleMap(SubProgressToken token, string map)
        {
            PrepareUpscaleDirectory(token, map);
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
        private void PrepareUpscaleSfx(SubProgressToken token)
        {
            PrepareUpscaleDirectory(token, "sfx");
        }
        private void PrepareUpscaleDirectory(SubProgressToken token, string name, Func<string, bool>? ignore = null)
        {
            ignore ??= _ => false;

            var sourceDir = Path.Join(ExtractDir, name);

            token.SubmitStatus($"Preparing {name} for upscaling");

            var files = Directory.GetFiles(sourceDir, "*.dds", SearchOption.TopDirectoryOnly);

            token.SubmitStatus($"Preparing {name} for upscaling ({files.Length} files)");

            token.ForAllParallel(files, file => CategorizeTexture(file, UpscaleDir, ignore, token));
        }
        private static void CategorizeTexture(string file, string outDir, Func<string, bool> ignore, ILogger logger)
        {
            static string JoinFile(params string[] parts)
            {
                var dir = Path.Join(parts);
                Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
                return dir;
            }

            var id = TexId.FromPath(file);
            var png = id.Value + ".png";
            var kind = id.GetTexKind();

            // if (id.IsUnwanted()) return;

            try
            {
                using var image = DDSImage.Load(file);

                var target = kind.GetShortName();

                if (
                    // The CupScale cannot handle files with non-ASCII characters.
                    // Luckily, there are only 3 with such characters.
                    id.Value.Any(c => c >= 128) ||
                    // We want to ignore this file.
                    ignore(id.Name.ToString()) ||
                    // there is no point in upscaling a solid color.
                    id.IsSolidColor())
                    target = "ignore";

                if (target != "ignore")
                {
                    // handle normals
                    if (kind == TexKind.Normal)
                    {
                        var normalImage = DS3NormalMap.Of(image);

                        normalImage.Normals.SaveAsPng(JoinFile(outDir, "n_normal", png));
                        normalImage.Gloss.SaveAsPng(JoinFile(outDir, "n_gloss", png));
                        if (normalImage.Heights.IsNoticeable())
                            normalImage.Heights.SaveAsPng(JoinFile(outDir, "n_height", png));
                        return;
                    }

                    // handle transparency
                    var transparency = id.GetTransparency();
                    if (transparency == TransparencyKind.Binary || transparency == TransparencyKind.Full)
                    {
                        var texMap = image.ToTextureMap();

                        var alphaTarget = transparency == TransparencyKind.Binary ? "alpha_binary" : "alpha_full";
                        texMap.GetAlpha().SaveAsPng(JoinFile(outDir, alphaTarget, png));

                        texMap.FillSmallHoles3();
                        texMap.SetBackground(default);
                        texMap.SaveAsPng(JoinFile(outDir, target, png));
                        return;
                    }
                }

                image.SaveAsPng(JoinFile(outDir, target, png));
            }
            catch (Exception e)
            {
                logger.LogException($"Failed to categorize {file}", e);
            }
        }

        public void MoveBadNormals(SubProgressToken token, string outDir)
        {
            token.SubmitStatus("Searching for bad normals");
            var bad = DS3.OriginalFormat
                .Where(p =>
                {
                    var id = p.Key;
                    var format = p.Value;
                    return id.GetTexKind() == TexKind.Normal && format.FourCC == Pfim.CompressionAlgorithm.D3DFMT_DXT1;
                })
                .Select(p => p.Key)
                .ToArray();

            token.SubmitStatus("Copying bad normals");
            token.ForAllParallel(bad, id =>
            {
                var cat = id.Category.ToString();
                var name = id.Name.ToString();

                var source = Path.Join(ExtractDir, cat, name + ".dds");
                var target = Path.Join(outDir, cat, name + ".png");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                source.ToPNG(target);
            });
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

        internal static string GetPartsType(string name)
        {
            return name.Substring(0, 2).ToUpperInvariant() switch
            {
                "AM" => "FullBody",
                "BD" => "FullBody",
                "FC" => "Face",
                "FG" => "Face",
                "HD" => "FullBody",
                "HR" => "Hair",
                "LG" => "FullBody",
                "WP" => "Weapon",
                _ => throw new Exception("Invalid parts name: " + name)
            };
        }
    }

    public struct TexFile
    {
        public TexId Id { get; set; }
        public string GamePath { get; set; }
    }
}

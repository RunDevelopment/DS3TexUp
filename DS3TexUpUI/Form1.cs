using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;

#nullable enable

namespace DS3TexUpUI
{
    public partial class Form1 : Form
    {
        private IProgressToken progressToken;
        private bool isCanceled = false;
        private bool taskIsRunning = false;
        private DateTime lastStartTime;

        class Form1ProgressToken : IProgressToken
        {
            readonly Form1 _form;

            private string _status = "";
            private (int, int)? _subProgress = null;

            public bool IsCanceled => _form.isCanceled;
            public object Lock => this;

            public Form1ProgressToken(Form1 form)
            {
                _form = form;
            }

            public void CheckCanceled()
            {
                if (IsCanceled)
                    throw new CanceledException();
            }

            public void SubmitProgress(double current)
            {
                CheckCanceled();
                _form.Invoke(new Action(() =>
                {
                    const int Max = 10000;
                    _form.progressBar.Minimum = 0;
                    _form.progressBar.Maximum = Max;
                    _form.progressBar.Value = (int)Math.Min(Max, Math.Max(current * Max, 0));
                }));
            }

            public void SubmitSubProgress(int current, int total)
            {
                CheckCanceled();

                _subProgress = (current, total);
                _form.Invoke(new Action(() =>
                {
                    _form.statusTextBox.Text = GetStatusTex();
                }));
            }
            public void SubmitStatus(string status)
            {
                CheckCanceled();

                _status = status;
                _subProgress = null;
                _form.Invoke(new Action(() =>
                {
                    _form.statusTextBox.Text = GetStatusTex();
                }));
            }
            private string GetStatusTex()
            {
                if (_subProgress == null)
                    return _status;
                else
                    return $"{_status} ({_subProgress.Value.Item1}/{_subProgress.Value.Item2})";
            }

            public void SubmitLog(string message)
            {
                _form.Invoke(new Action(() =>
                {
                    var log = _form.logRichTextBox;
                    log.AppendText(message + "\n");
                    log.ScrollToCaret();
                }));
                CheckCanceled();
            }
        }

        public Form1()
        {
            InitializeComponent();

            comboBox1.SelectedIndex = 0;
            progressToken = new Form1ProgressToken(this);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            exeOpenFileDialog.FileName = exeTextBox.Text;
            if (exeOpenFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (exeOpenFileDialog.FileName.EndsWith("DarkSoulsIII.exe"))
            {
                exeTextBox.Text = exeOpenFileDialog.FileName;
            }
            else
            {
                MessageBox.Show("Please select \"DarkSoulsIII.exe\".");
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            workingDirBrowserDialog.SelectedPath = workingDirTextBox.Text;
            if (workingDirBrowserDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            workingDirTextBox.Text = workingDirBrowserDialog.SelectedPath;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", Path.GetDirectoryName(exeTextBox.Text));
        }
        private void button3_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", workingDirTextBox.Text);
        }

        Workspace GetWorkspace()
        {
            return new Workspace(Path.GetDirectoryName(exeTextBox.Text)!, workingDirTextBox.Text);
        }

        private void extractButton_Click(object sender, EventArgs e)
        {
            var text = "Are you sure that you want to extract? This operation cannot be undone.";
            if (MessageBox.Show(text, "You sure?", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            RunTask(GetWorkspace().Extract);
        }
        private void overwriteButton_Click(object sender, EventArgs e)
        {
            RunTask(Overwrite);
        }
        private void restoreButton_Click(object sender, EventArgs e)
        {
            var text = "Are you sure that you want to restore? This operation cannot be undone.";
            if (MessageBox.Show(text, "You sure?", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            RunTask(GetWorkspace().Restore);
        }
        private void abortButton_Click(object sender, EventArgs e)
        {
            isCanceled = true;
        }

        private void button6_Click(object sender, EventArgs e)
        {
            RunTask(GetWorkspace().PrepareUpscale);
        }
        private void button7_Click(object sender, EventArgs e)
        {
            RunTask(CreateUpscaledDDS);
        }
        private void button8_Click(object sender, EventArgs e)
        {
            const string ManualDir = @"C:\DS3TexUp\up-manual\";

            RunTask(token =>
            {
                token.SubmitStatus("Searching for invalid ids");
                var invalidFiles = Directory.GetFiles(ManualDir, "*.png", SearchOption.AllDirectories)
                    .Where(f => !DS3.OriginalSize.ContainsKey(TexId.FromPath(f)))
                    .ToList();
                if (invalidFiles.Count > 0)
                {
                    token.SubmitLog($"Found {invalidFiles.Count} invalid files");
                    foreach (var f in invalidFiles)
                        token.SubmitLog($"  {f}");
                    return;
                }

                token.SubmitStatus("Validating categories");
                var cagetories = new (string, TexKind)[]{
                    ("a", TexKind.Albedo),
                    ("n_normal", TexKind.Normal),
                    ("n_gloss", TexKind.Normal),
                    ("r", TexKind.Reflective),
                    ("em", TexKind.Emissive),
                    ("m", TexKind.Mask),
                };
                foreach (var (relPath, kind) in cagetories)
                {
                    var files = Directory.GetFiles(Path.Join(ManualDir, relPath), "*.png", SearchOption.AllDirectories)
                        .Where(f => TexId.FromPath(f).GetTexKind() != kind)
                        .ToList();
                    if (files.Count > 0)
                    {
                        token.SubmitLog($"Error: Found {files.Count} non-{kind} textures in {Path.Join(ManualDir, relPath)}:");
                        foreach (var f in files)
                            token.SubmitLog($"  {f}");
                        return;
                    }
                }

                FindUnusedManualOverwrites(token.Reserve(0));
                UpdateManualNormalAlbedo(token);
            });
        }
        private void FindUnusedManualOverwrites(IProgressToken token)
        {
            var p = GetProject();

            var unused = new List<(string type, string file, TexId id, TexId representative)>();
            void AddUnused(string type, TexOverrideList textures, Func<TexId, TexId> getRepresentative)
            {
                token.SubmitStatus(type);
                foreach (var (id, file) in textures.GetFiles())
                {
                    if (!file.StartsWith(@"C:\DS3TexUp\up-manual\")) continue;

                    var r = getRepresentative(id);
                    if (r != id)
                    {
                        unused.Add((type, file, id, r));
                        token.SubmitLog($"Unused {type} override: {id} ({file}) is represented by {r}");
                    }
                }
            }

            AddUnused("Albedo", p.Textures.Albedo, id => id.GetRepresentative());
            AddUnused("Alpha", p.Textures.Alpha, id => id.GetAlphaRepresentative());
            AddUnused("Normal normal", p.Textures.NormalNormal, id => id.GetNormalRepresentative());
            AddUnused("Normal gloss", p.Textures.NormalGloss, id => id.GetGlossRepresentative());
            AddUnused("Reflective", p.Textures.Reflective, id => id.GetRepresentative());
            AddUnused("Shininess", p.Textures.Shininess, id => id.GetRepresentative());
            AddUnused("Emissive", p.Textures.Emissive, id => id.GetRepresentative());

            if (unused.Count > 0) token.SubmitLog($"Found {unused.Count} unused manual texture overrides:");
        }
        private DateTime? lastFullNAValidation = null;
        private void UpdateManualNormalAlbedo(IProgressToken token)
        {
            const string ManualDir = @"C:\DS3TexUp\up-manual";
            const string ManualDirAlbedo = ManualDir + @"\a";
            const string ManualDirNormalAlbedo = @"C:\DS3TexUp\up-manual" + @"\n_albedo";
            const string ManualDirAlbedoTodo = ManualDir + @"\TODO-a";
            const string ManualDirNormalAlbedoTodo = @"C:\DS3TexUp\up-manual" + @"\TODO-n_albedo";

            bool FindDuplicates(IEnumerable<string> files, string dir)
            {
                token.SubmitStatus("Searching for duplicates in " + dir);
                var duplicate = files.GroupBy(TexId.FromPath).Where(g => g.Count() > 1).ToList();
                if (duplicate.Count > 0)
                {
                    token.SubmitLog($"Error: Found {duplicate.Count} duplicates:");
                    foreach (var g in duplicate.OrderBy(g => g.Key))
                    {
                        token.SubmitLog($"{g.Key}: {string.Join(" ", g.Select(f => Path.GetRelativePath(dir, f)))}");
                    }
                    return true;
                }
                return false;
            }
            bool ReadDir(string dir, out Dictionary<TexId, string> files)
            {
                files = new Dictionary<TexId, string>();

                var list = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories)
                    : new string[0];
                if (FindDuplicates(list, dir)) return false;
                files = list.ToDictionary(TexId.FromPath);
                return true;
            }

            if (!ReadDir(ManualDirAlbedo, out var albedo)) return;
            if (!ReadDir(ManualDirNormalAlbedo, out var normalAlbedo)) return;
            if (!ReadDir(ManualDirNormalAlbedoTodo, out var normalAlbedoTodo)) return;
            if (Directory.Exists(ManualDirAlbedoTodo)) Directory.Delete(ManualDirAlbedoTodo, true);

            var hashCache = new ConcurrentDictionary<string, string>();
            static string HashFile(string path)
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(path);
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            string GetHash(string file) => hashCache!.GetOrAdd(file, HashFile);

            token.SubmitStatus("Processing TODOs");
            token.ForAllParallel(normalAlbedoTodo, kv =>
            {
                var (id, file) = kv;

                if (!albedo.TryGetValue(id, out var albedoFile))
                {
                    token.SubmitLog($"No albedo for normal albedo TODO {id}");
                    return;
                }

                var hash = GetHash(albedoFile);
                var target = Path.Join(ManualDirNormalAlbedo, id.Category, $"{id.Name.ToString()}-@{hash}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(file, target, true);
                lock (normalAlbedo) normalAlbedo[id] = target;
            });

            token.SubmitStatus("Removing empty directories");
            if (Directory.Exists(ManualDirNormalAlbedoTodo))
            {
                foreach (var dir in Directory.GetDirectories(ManualDirNormalAlbedoTodo, "*", SearchOption.AllDirectories))
                {
                    if (Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                        Directory.Delete(dir, true);
                }
            }

            token.SubmitStatus("Detecting unused normal albedos");
            token.ForAllParallel(normalAlbedo, kv =>
            {
                var (id, file) = kv;

                static string ParseHash(string file)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var start = name.IndexOf("-@");
                    if (start == -1) return "";
                    return name.Substring(start + 2).ToLowerInvariant();
                }
                var hash = ParseHash(file);

                var unused =
                    !albedo.TryGetValue(id, out var albedoFile)
                    || (
                        (lastFullNAValidation == null || File.GetLastWriteTimeUtc(file) >= lastFullNAValidation.Value || File.GetLastWriteTimeUtc(albedoFile) >= lastFullNAValidation.Value) &&
                        hash != GetHash(albedoFile)
                    );

                if (unused)
                {
                    token.SubmitLog($"Delete unused normal albedo {id}");
                    File.Delete(file);
                    lock (normalAlbedo!) normalAlbedo.Remove(id);
                }
            });
            if (lastFullNAValidation == null) lastFullNAValidation = DateTime.UtcNow;

            token.SubmitStatus("Adding albedos without normal albedo to TODO");
            var albedoWithNA = albedo.Where(kv => !normalAlbedo.ContainsKey(kv.Key)).ToList();
            token.ForAllParallel(albedoWithNA, kv =>
            {
                var (id, file) = kv;

                var target = Path.Join(ManualDirAlbedoTodo, id.Category, $"{id.Name.ToString()}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Directory.CreateDirectory(ManualDirNormalAlbedoTodo);
                File.Copy(file, target);
            });
            if (albedoWithNA.Count > 0) {
                token.SubmitLog($"Added {albedoWithNA.Count} albedo with normal albedo to TODO dir.");
            }
        }

        void RunTask(Action<SubProgressToken> task)
        {
            if (taskIsRunning)
            {
                MessageBox.Show("Another task is already running and has to be completed first.");
                return;
            }

            taskIsRunning = true;

            isCanceled = false;
            abortButton.Enabled = true;
            statusTextBox.Text = "";
            logRichTextBox.Clear();
            progressBar.Value = progressBar.Minimum;
            lastStartTime = DateTime.UtcNow;

            string GetElapsedTime()
            {
                var d = DateTime.UtcNow - lastStartTime;

                if (d.Days != 0) return $"{d.Days}d {d.Hours}h {d.Minutes}m {d.Seconds}s";
                if (d.Hours != 0) return $"{d.Hours}h {d.Minutes}m {d.Seconds}s";
                if (d.Minutes != 0) return $"{d.Minutes}m {d.Seconds}s";
                return $"{d.Seconds}s";
            }

            Task.Run(() =>
            {
                try
                {
                    task(new SubProgressToken(progressToken));

                    Invoke(new Action(() =>
                    {
                        statusTextBox.Text = $"Done. Took {GetElapsedTime()}";
                        progressBar.Value = progressBar.Maximum;
                    }));
                }
                catch (Exception e)
                {
                    if (e is CanceledException)
                    {
                        Invoke(new Action(() =>
                        {
                            statusTextBox.Text = $"Canceled. Took {GetElapsedTime()}";
                            progressBar.Value = progressBar.Minimum;
                        }));
                    }
                    else
                    {
                        progressToken.LogException(e);
                        progressToken.SubmitLog("Failed.");
                    }
                }
                finally
                {
                    GC.Collect();

                    Invoke(new Action(() =>
                    {
                        taskIsRunning = false;
                        abortButton.Enabled = false;
                    }));
                }
            });
        }

        private (HashSet<TexId> ids, List<string> prefixes) ParseCurrent()
        {
            string GetCurrentFile()
            {
                string? dir = AppDomain.CurrentDomain.BaseDirectory;
                while (dir != null && dir != "")
                {
                    var f = Path.Join(dir, @"current.txt");
                    if (File.Exists(f)) return f;
                    dir = Path.GetDirectoryName(dir);
                }
                throw new Exception("Unable to find 'current.txt'.");
            }

            var ignoreChars = new char[] { ' ', '\t', '\r', '\n', '[', ']', '"', ':', ',' };
            var lines = File.ReadAllText(GetCurrentFile()).Split('\n').Select(s => s.Trim(ignoreChars)).Where(s => s.Length > 0);

            var ids = new HashSet<TexId>();
            var prefixes = new List<string>();

            foreach (var line in lines)
            {
                if (line.EndsWith("*"))
                    prefixes.Add(line.Substring(0, line.Length - 1));
                else
                    ids.Add(TexId.FromPath(line));
            }

            // if the file was empty, accept all
            if (ids.Count == 0 && prefixes.Count == 0) prefixes.Add("");

            return (ids, prefixes);
        }
        private void Overwrite(SubProgressToken token)
        {
            var (currentIds, prefixes) = ParseCurrent();
            GetWorkspace().Overwrite(token, id =>
            {
                if (currentIds.Contains(id)) return false;
                return !prefixes.Any(p => id.Value.StartsWith(p));
            });
        }

        private UpscaleProject GetProject()
        {
            return new UpscaleProject(GetWorkspace())
            {
                Textures = new UpscaledTextures()
                {
                    Albedo = new TexOverrideList() {
                        @"D:\DS3\albedo-processed\sharpest",
                        @"D:\DS3\albedo-raw\GroundTextures",
                        @"D:\DS3\albedo-processed\ground-moss",
                        @"D:\DS3\er\albedo-processed\sharpest",
                        @"D:\DS3\er\albedo-raw\GroundTextures",
                        @"D:\DS3\er\albedo-processed\ground-moss",
                        @"C:\DS3TexUp\up-manual\a",
                    },
                    Alpha = new TexOverrideList() {
                        @"D:\DS3\upscaled\alpha",
                        @"D:\DS3\upscaled\alpha_binary",
                        @"D:\DS3\upscaled\n_height",
                        @"D:\DS3\er\upscaled\alpha_full",
                        @"D:\DS3\er\upscaled\alpha_binary",
                        @"D:\DS3\er\upscaled\n_height",
                        @"C:\DS3TexUp\up-manual\alpha",
                    },

                    NormalNormal = new TexOverrideList() {
                        @"D:\DS3\upscaled\n_normal",
                        @"D:\DS3\upscaled\n_normal-v2",
                        @"D:\DS3\er\upscaled\n_normal",
                        @"C:\DS3TexUp\up-manual\n_normal",
                    },
                    NormalAlbedo = new TexOverrideList() {
                        @"D:\DS3\normal-albedo",
                        @"D:\DS3\er\normal-albedo",
                        @"C:\DS3TexUp\up-manual\n_albedo",
                    },
                    NormalGloss = new TexOverrideList() {
                        @"D:\DS3\upscaled\n_gloss",
                        @"D:\DS3\er\upscaled\n_gloss",
                        @"C:\DS3TexUp\up-manual\n_gloss",
                    },
                    NormalHeight = new TexOverrideList(){
                        @"D:\DS3\upscaled\n_height",
                        @"D:\DS3\er\upscaled\n_height",
                    },

                    Reflective = new TexOverrideList() {
                        @"D:\DS3\upscaled\r",
                        @"D:\DS3\er\upscaled\r",
                        @"C:\DS3TexUp\up-manual\r",
                    },
                    Shininess = @"D:\DS3\upscaled\s",
                    Emissive = new TexOverrideList() {
                        @"D:\DS3\upscaled\em",
                        @"C:\DS3TexUp\up-manual\em",
                    },
                    Mask = @"C:\DS3TexUp\up-manual\m",
                },
                TemporaryDir = @"C:\DS3TexUp\temp"
            };
        }
        private void CreateUpscaledDDS(IProgressToken token)
        {
            token.SubmitStatus("Loading project");
            var p = GetProject();

            token.SubmitStatus("Loading upscale factors");
            var dir = Data.Dir(Data.Source.Local);
            var outputUpscale = DS3.UpscaleFactor.LoadFromDir(dir);
            p.FormatOverrides = Path.Join(dir, @"output-format-override.json").LoadJsonFile<Dictionary<TexId, DDSFormat>>();
            if (Data.HasLocal)
                token.SubmitLog("Using local data dir for upscale factors: " + Path.GetFullPath(dir));
            else
                token.SubmitLog("Using application data dir for upscale factors");

            token.SubmitStatus("Selecting ids");
            var (currentIds, prefixes) = ParseCurrent();

            var ids = DS3.OriginalSize.Keys
                // no solid colors
                .Where(id => !id.IsSolidColor())
                // no unused textures
                .Except(DS3.Unused)
                // no ignored textures
                .Except(outputUpscale.Ignore)
                .Where(id =>
                {
                    // allow everything with a manually set factor
                    if (outputUpscale.Upscale.ContainsKey(id))
                        return true;

                    var kind = id.GetTexKind();
                    return kind == TexKind.Albedo || kind == TexKind.Normal || kind == TexKind.Emissive || kind == TexKind.Reflective;
                });

            if (prefixes.Count == 0)
            {
                // ignore ids
                ids = new TexId[0];
            }
            else if (prefixes.Count == 1 && prefixes.First() == "")
            {
                // keep ids as is
            }
            else
            {
                // filter ids
                ids = ids.Where(id => prefixes.Any(p => id.Value.StartsWith(p)));
            }

            token.SubmitStatus("Creating DDS overwrite files");
            token.ForAllParallel(ids.Union(currentIds), id =>
            {
                try
                {
                    int upscale = outputUpscale[id];
                    p.WriteDDS(id, upscale, token);
                }
                catch (System.Exception e)
                {
                    token.LogException(e);
                }
            });
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // m37_00_00_00_020151
            var w = GetWorkspace();

            foreach (var f in Directory.GetFiles(@"C:\Users\micha\Desktop\n"))
            {
                var image = f.LoadTextureMap();
                var blurred = image.Blur(3, Average.Rgba32);

                for (int i = 0; i < image.Data.Length; i++)
                {
                    var pi = image[i];
                    var pb = blurred[i];
                    var ni = Normal.FromRG(pi.R, pi.G);
                    var nb = Normal.FromRG(pb.R, pb.G);
                    var (r, g) = Normal.HeightMapAddition(ni, 1, nb, -1).ToRG();
                    image.Data[i].R = r;
                    image.Data[i].G = g;
                }

                blurred = image.Blur(2, Average.Rgba32);

                for (int i = 0; i < image.Data.Length; i++)
                {
                    var pi = image[i];
                    var pb = blurred[i];
                    var ni = Normal.FromRG(pi.R, pi.G);
                    var nb = Normal.FromRG(pb.R, pb.G);
                    var (r, g) = Normal.HeightMapAddition(ni, 1, nb, -1).ToRG();
                    image.Data[i].R = r;
                    image.Data[i].G = g;
                }

                image.SaveAsPng(Path.Join(@"C:\Users\micha\Desktop\n\high-3-2", $"{Path.GetFileNameWithoutExtension(f)}.png"));
            }

            // var ids = ParseCurrent().ids;
            // var rep = DS3.RepresentativeOf.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
            // var na = DS3.NormalAlbedo.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
            // ids = ids.SelectMany(id => rep.GetOrNew(id).Append(id)).SelectMany(id => na.GetOrNew(id).Append(id)).ToHashSet();
            // Clipboard.SetText(string.Join("\n", ids));

            // var tiles = new Dictionary<TexId, List<Tile>>();
            // foreach (var id in ParseCurrent().ids)
            // {
            //     tiles.Add(id, new List<Tile>() {
            //         new Tile(2, 0, 0, 1, 1),
            //         new Tile(2, 0, 1, 1, 1),
            //         new Tile(2, 1, 0, 1, 1),
            //         new Tile(2, 1, 1, 1, 1),
            //     });

            //     var t = Path.Join(@"C:\DS3TexUp\bar", $"{id.Category.ToString()}-{id.Name.ToString()}.dds");
            //     File.Copy(w.GetExtractPath(id), t);
            // }
            // tiles.SaveAsJson("tiles-2.json");

            // var l = @"C:\DS3TexUp\up-manual-new\o962120_o2120_a.png".LoadTextureMap();
            // var s = @"C:\DS3TexUp\up-manual-new\m30_00_o962120_02_a-1x_BC1-smooth2.png".LoadTextureMap();
            // l.CopyColorFrom(s, downScale: 1);
            // l.SaveAsPng(@"C:\DS3TexUp\up-manual-new\m30_00_o962120_02_a.png");


            // RunTask(token =>
            // {
            //     var hr = @"C:\Users\micha\Desktop\train\data6\hr";
            //     var lr = @"C:\Users\micha\Desktop\train\data6\lr";
            //     var lrBc1 = @"C:\Users\micha\Desktop\train\data6\lr-bc1";
            //     var lrBc7 = @"C:\Users\micha\Desktop\train\data6\lr-bc7";
            //     // Training.CreateHR(token, @"C:\Users\micha\Desktop\train\raw-data", hr);
            //     // Training.CreateLR(token, hr, lr, Training.LRCompression.Uncompressed);
            //     // Training.CreateLR(token, hr, lrBc1, Training.LRCompression.BC1);
            //     Training.CreateLR(token, hr, lrBc7, Training.LRCompression.BC7);
            //     Training.PickValidation(token, new string[] { hr, lr, lrBc1, lrBc7 }, 0.005);
            // });
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            TexId GetTexId()
            {
                var re = new Regex(@"[^\w/\\.]+");
                var text = re.Replace(textBox1.Text, "");

                var colorCode = new Regex(@"^(?i:[rgbymc]){6}$");
                if (colorCode.IsMatch(text))
                {
                    var cc = ColorCode6x6.Parse(text);
                    foreach (var kv in DS3.ColorCode)
                        if (kv.Value == cc)
                            return kv.Key;
                }

                if (!text.Contains('/') && !text.Contains('\\'))
                {
                    if (DS3.Homographs.TryGetValue(text, out var homographs) && homographs.Count == 1)
                        return homographs.First();

                    var byName = DS3.OriginalSize.Keys.Where(id => id.Name.SequenceEqual(text)).ToList();
                    if (byName.Count == 1)
                        return byName.Single();

                    throw new Exception();
                }

                return TexId.FromPath(text.Replace("\\\\", "\\"));
            }

            bool TryOpenFileDirectly()
            {
                var text = textBox1.Text.Replace("\\\\", "\\");
                if (File.Exists(text))
                {
                    using var process = Process.Start(@"C:\Program Files\paint.net\paintdotnet.exe", text);
                    textBox1.Text = "";

                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return true;
                }
                return false;
            }

            if (e.KeyCode == Keys.Enter)
            {
                TexId id;
                try
                {
                    id = GetTexId();
                }
                catch (System.Exception)
                {
                    if (TryOpenFileDirectly()) return;

                    MessageBox.Show($"\"{textBox1.Text}\" does not map to a unique TexId.");
                    return;
                }

                var w = GetWorkspace();
                var selected = comboBox1.SelectedItem.ToString();
                var dir = selected switch
                {
                    "Extract" => w.ExtractDir,
                    "Overwrite" => w.OverwriteDir,
                    _ => throw new Exception("Unknown open mode " + selected)
                };

                var p = Path.Join(dir, id.Category, id.Name.ToString() + ".dds");
                if (!File.Exists(p)) p = Path.ChangeExtension(p, "png");

                if (File.Exists(p))
                {
                    using var process = Process.Start(@"C:\Program Files\paint.net\paintdotnet.exe", p);
                    textBox1.Text = "";

                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else
                {
                    if (TryOpenFileDirectly()) return;

                    MessageBox.Show("No such file");
                }
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            var downScale = 1 << (int)numericUpDown1.Value;
            var refFile = copyColorRefTextbox.Text;
            var smallFile = copyColorSmallTextbox.Text;

            RunTask(token =>
            {
                token.SubmitStatus("Loading files");
                if (!File.Exists(refFile))
                {
                    token.SubmitLog("Error: ref file does not exist: " + refFile);
                    return;
                }
                if (!File.Exists(smallFile))
                {
                    token.SubmitLog("Error: small file does not exist: " + smallFile);
                    return;
                }
                var r = refFile.LoadTextureMap();
                var s = smallFile.LoadTextureMap();

                token.SubmitStatus("Copying color");
                r.CopyColorFrom(s, downScale);

                token.SubmitStatus("Saving result");
                const string TargetDir = @"C:\DS3TexUp\up-manual-new";
                var name = $"{Path.GetFileNameWithoutExtension(smallFile)}-adjusted{downScale}";
                var target = Path.Join(TargetDir, $"{name}.png");
                for (var i = 2; File.Exists(target); i++)
                    target = Path.Join(TargetDir, $"{name}-{i}.png");
                r.SaveAsPng(target);
            });
        }
        private void AllowFilesDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false) == true)
                e.Effect = DragDropEffects.Link;
        }
        private void AcceptFileTextboxDragDrop(object sender, DragEventArgs e)
        {
            if (!(sender is TextBox textbox)) return;

            var data = e.Data.GetData(DataFormats.FileDrop);
            if (data is string file) textbox.Text = file;
            if (data is string[] files) textbox.Text = files.Length == 0 ? "no files" : files[0];
        }
    }
}

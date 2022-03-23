using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
                        @"C:\DS3TexUp\up-manual\a",
                    },
                    Alpha = new TexOverrideList() {
                        @"D:\DS3\upscaled\alpha",
                        @"D:\DS3\upscaled\alpha_binary",
                        @"D:\DS3\upscaled\n_height",
                        @"C:\DS3TexUp\up-manual\alpha",
                    },

                    NormalNormal = new TexOverrideList() {
                        @"D:\DS3\upscaled\n_normal",
                        @"C:\DS3TexUp\up-manual\n_normal",
                    },
                    NormalAlbedo = new TexOverrideList() {
                        @"D:\DS3\normal-albedo",
                        @"C:\DS3TexUp\up-manual\n_albedo",
                    },
                    NormalGloss = new TexOverrideList() {
                        @"D:\DS3\upscaled\n_gloss",
                        @"C:\DS3TexUp\up-manual\n_gloss",
                    },
                    NormalHeight = @"D:\DS3\upscaled\n_height",

                    Reflective = new TexOverrideList() {
                        @"D:\DS3\upscaled\r",
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
            DS3.UpscaleFactor outputUpscale;
            var localDir = Path.GetFullPath("data");
            if (Directory.Exists(localDir))
            {
                token.SubmitLog("Using local data dir for upscale factors: " + Path.GetFullPath(localDir));
                outputUpscale = DS3.UpscaleFactor.LoadFromDir(localDir);
            }
            else
            {
                token.SubmitLog("Using application data dir for upscale factors");
                outputUpscale = DS3.OutputUpscale;
            }

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
        private void FindUnusedManualOverwrites(IProgressToken token)
        {
            var p = GetProject();

            var unused = new List<string>();
            void AddUnused(IEnumerable<KeyValuePair<TexId, string>> files)
            {
                unused.AddRange(files.Select(kv => kv.Value));
            }

            token.SubmitStatus("Albedo");
            AddUnused(p.Textures.Albedo.GetFiles().Where(kv => kv.Key.GetRepresentative() != kv.Key));

            token.SubmitStatus("Alpha");
            AddUnused(p.Textures.Alpha.GetFiles().Where(kv => kv.Key.GetAlphaRepresentative() != kv.Key));

            token.SubmitStatus("Normal normal");
            AddUnused(p.Textures.NormalNormal.GetFiles().Where(kv => kv.Key.GetNormalRepresentative() != kv.Key));

            token.SubmitStatus("Normal gloss");
            AddUnused(p.Textures.NormalGloss.GetFiles().Where(kv => kv.Key.GetGlossRepresentative() != kv.Key));

            token.SubmitStatus("Normal albedo");
            var na = DS3.NormalAlbedo.Values.ToHashSet();
            AddUnused(p.Textures.NormalAlbedo.GetFiles().Where(kv => !na.Contains(kv.Key)));

            token.SubmitStatus("Reflective");
            AddUnused(p.Textures.Reflective.GetFiles().Where(kv => kv.Key.GetRepresentative() != kv.Key));
            token.SubmitStatus("Shininess");
            AddUnused(p.Textures.Shininess.GetFiles().Where(kv => kv.Key.GetRepresentative() != kv.Key));
            token.SubmitStatus("Emissive");
            AddUnused(p.Textures.Emissive.GetFiles().Where(kv => kv.Key.GetRepresentative() != kv.Key));

            token.SubmitStatus("Filtering manual");
            var manual = @"C:\DS3TexUp\up-manual\";
            var unusedManual = unused.Where(f => f.StartsWith(manual)).ToHashSet().ToList();
            unusedManual.Sort();
            unusedManual.SaveAsJson("unused-manual.json");
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // m37_00_00_00_020151
            var w = GetWorkspace();

            // RunTask(token =>
            // {
            //     var pairs = new (string, string, bool isEqual)[]{
            //         ("m34/m34_o1240_01_n", "m33/m33_00_o1240_n", true),
            //         ("m31/m31_00_spiderweb_01_n", "m38/m38_00_m22_00_grass_502_n", false),
            //         ("m31/m31_00_ruinedge_01_a", "m33/m33_00_ruinedge_01_a", true),
            //         ("m33/m33_00_ruinedge_01_a", "chr/c1350_c1350_weapon2_a", false),
            //         ("chr/c2190_c2190_wing_n", "obj/o399910_c2190_wing_n", true),
            //         ("chr/c1446_c1446_tunic_r", "chr/c1441_c1441_tunic_r", true),
            //         ("chr/c3080_c3080arms_r", "obj/o302593_o302593_c3080arms_r", true),
            //         ("m37/m37_00_firebrand_01_r", "m38/m38_00_o9569_r", true),
            //         ("m41/m41_00_m36_00_Test_branch_tiling_01_r", "chr/c1109_c1109_hood_r", false),
            //         ("m31/m31_00_spiderweb_03_n", "m30/m30_00_o344883_03_n",false),
            //         ("m31/m31_00_woodbox_00_n","obj/o001080_o001080_woodenbox_n",true),
            //         ("m32/m32_00_o324005_railing_n","m40/m40_00_railing_000_n",false),
            //         ("m32/m32_00_o329222_2_r", "m30/m30_00_o329222_2_rubble_01_r", true),
            //         ("chr/c6260_c6260_Shield_Small_n","parts/WP_A_2031_WP_A_2031_n",true),
            //         ("chr/c1070_c1070_tree_n", "m31/m31_00_statue_00_n", true),
            //         ("chr/c1370_c1370_hair_n", "chr/c1240_c1240_cloths_n", true),
            //         ("chr/c1102_c1102_shield2_n", "obj/o398720_o398720_c1100_shield2_n", true),
            //         ("chr/c1070_c1070_wp_a_0404_n", "chr/c1071_c1071_wp_a_0404_n", true),
            //     };

            //     var result = new List<string>();
            //     token.ForAll(pairs, pair =>
            //     {
            //         var a = w.GetExtractPath(new TexId(pair.Item1)).LoadTextureMap();
            //         var b = w.GetExtractPath(new TexId(pair.Item2)).LoadTextureMap();
            //         var sim = a.GetSimilarityScore(b);
            //         result.Add($"{sim} E:{pair.isEqual} A:{sim.color < 0.04 && sim.feature < 0.12}");
            //     });

            //     MessageBox.Show(string.Join("\n", result));
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
    }
}

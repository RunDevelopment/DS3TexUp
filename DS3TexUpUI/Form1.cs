using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;

namespace DS3TexUpUI
{
    public partial class Form1 : Form
    {
        private IProgressToken progressToken;
        private bool isCanceled = false;
        private bool taskIsRunning = false;

        private Timer _exceptionTimer = new Timer() { Enabled = true, Interval = 10 };
        private Exception? _exception = null;

        class Form1ProgressToken : IProgressToken
        {
            readonly Form1 _form;

            public bool IsCanceled
            {
                get
                {
                    Application.DoEvents();
                    return _form.isCanceled;
                }
            }

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
                    _form.progressBar.Minimum = 0;
                    _form.progressBar.Maximum = 1000;
                    _form.progressBar.Value = (int)Math.Min(1000, Math.Max(current * 1000, 0));
                }));
            }

            public void SubmitStatus(string status)
            {
                CheckCanceled();
                _form.Invoke(new Action(() =>
                {
                    _form.statusTextBox.Text = status;
                }));

            }
        }

        public Form1()
        {
            InitializeComponent();

            progressToken = new Form1ProgressToken(this);

            _exceptionTimer.Tick += (s, ev) =>
            {
                var e = _exception;
                _exception = null;
                if (e != null) throw e;
            };
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
            return new Workspace(Path.GetDirectoryName(exeTextBox.Text), workingDirTextBox.Text);
        }

        private void extractButton_Click(object sender, EventArgs e)
        {
            RunTask(GetWorkspace().Extract);
        }
        private void overwriteButton_Click(object sender, EventArgs e)
        {
            RunTask(GetWorkspace().Overwrite);
        }
        private void restoreButton_Click(object sender, EventArgs e)
        {
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
            progressBar.Value = progressBar.Minimum;

            Task.Run(() =>
            {
                try
                {
                    task(new SubProgressToken(progressToken));

                    Invoke(new Action(() =>
                    {
                        statusTextBox.Text = "Done.";
                        progressBar.Value = progressBar.Maximum;
                    }));
                }
                catch (Exception e)
                {
                    if (e is CanceledException)
                    {
                        Invoke(new Action(() => progressBar.Value = progressBar.Minimum));
                        return;
                    }

                    Invoke(new Action(() => _exception = new Exception(e.ToString())));
                }
                finally
                {
                    Invoke(new Action(() =>
                    {
                        taskIsRunning = false;
                        abortButton.Enabled = false;
                    }));
                }
            });
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // m37_00_00_00_020151
            var w = GetWorkspace();

            // Yabber.RunParallel(Directory.GetFiles(Path.Join(w.MapsDir, "m30_00_00_00"), "*.mapbnd.dcx"));

            // var files = Directory
            //     .GetDirectories(Path.Join(w.MapsDir, "m30_00_00_00"), "*-mapbnd-dcx")
            //     .Select(d =>
            //     {
            //         // e.g. m37_00_00_00_020151
            //         var meshId = Path.GetFileName(d).Substring(0, 19);
            //         // e.g. m37_00_00_00_020151
            //         var mapId = meshId.Substring(0, 12);

            //         return Path.Join(d, "map", mapId, meshId, "Model", meshId + ".flver");
            //     })
            //     .ToArray();

            // var relevant = files
            //     .Where((file, i) =>
            //     {
            //         Text = "" + i;
            //         var f = FLVER.Read(files[i]);
            //         return f.Materials.Any(m => m.Params.Any(p => p.Value.Contains("m30_00_o309110_wall_02_n")));
            //     })
            //     .ToArray();


            // var files = Directory.GetFiles(@"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS III\Game\mtd\allmaterialbnd-mtdbnd-dcx", "*.mtd");

            // foreach (var file in files)
            // {
            //     var mat = MTD.Read(file);
            //     if (mat.ShaderPath.Contains("pom", StringComparison.OrdinalIgnoreCase)) {
            //         int i = 0;
            //     }
            // }

            //             var mat = MTD.Read(@"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS III\Game\mtd\allmaterialbnd-mtdbnd-dcx\M[ARSN]_l_p_m.mtd");

            //             var relevant = new string[]{
            // @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS III\Game\map\m37_00_00_00\m37_00_00_00_000800-mapbnd-dcx\map\m37_00_00_00\m37_00_00_00_000800\Model\m37_00_00_00_000800.flver",
            // @"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS III\Game\map\m37_00_00_00\m37_00_00_00_000908-mapbnd-dcx\map\m37_00_00_00\m37_00_00_00_000908\Model\m37_00_00_00_000908.flver"
            //             };

            //             foreach (var file in relevant)
            //             {
            //                 var f = FLVER2.Read(file);
            //                 var m = f.Materials[4];
            //                 var bytes = m.GXIndex;

            //                 // var views = bytes.Chunks(4).Select(b => new Views(b)).ToArray();

            //                 // var s = string.Join("\n", views.Select((v, i) =>
            //                 // {
            //                 //     return $"{i * 4} {{ }} {v.I32} {v.F32} [{v.Bytes[0]} {v.Bytes[1]} {v.Bytes[2]} {v.Bytes[3]}]";
            //                 // }));
            //                 // Clipboard.SetText(s);
            //             }


            // var res = string.Join("\n", relevant);
            // MessageBox.Show(res);
            // Clipboard.SetText(res);

            // var f = FLVER.Read(@"C:\Program Files (x86)\Steam\steamapps\common\DARK SOULS III\Game\map\m53_00_00_00\m53_00_00_00_002000-mapbnd-dcx\map\m53_00_00_00\m53_00_00_00_002000\Model\m53_00_00_00_002000.flver");
            // MessageBox.Show(string.Join("\n", f.Materials.Select(m => m.Name)));
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                var w = GetWorkspace();
                var p = Path.Join(w.ExtractDir, textBox1.Text + ".dds");
                if (File.Exists(p))
                {
                    using var process = Process.Start(@"C:\Program Files\paint.net\paintdotnet.exe", p);
                    textBox1.Text = "";
                }
                else
                {
                    MessageBox.Show("No such file");
                }
            }
        }
    }
}

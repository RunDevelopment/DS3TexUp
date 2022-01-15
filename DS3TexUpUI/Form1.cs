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
                    const int Max = 10000;
                    _form.progressBar.Minimum = 0;
                    _form.progressBar.Maximum = Max;
                    _form.progressBar.Value = (int)Math.Min(Max, Math.Max(current * Max, 0));
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

            public void SubmitLog(string message)
            {
                CheckCanceled();
                _form.Invoke(new Action(() =>
                {
                    _form.logRichTextBox.AppendText(message + "\n");
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
            logRichTextBox.Clear();
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

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

namespace DS3TexUpUI
{
    public partial class Form1 : Form
    {
        private IProgressToken progressToken;
        private bool isCanceled = false;
        private bool taskIsRunning = false;

        class Form1ProgressToken : IProgressToken
        {
            public bool IsCanceled
            {
                get
                {
                    Application.DoEvents();
                    return _form.isCanceled;
                }
            }

            readonly Form1 _form;
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
            RunTask(GetWorkspace().Overwrtite);
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

            try
            {
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
                    catch (CanceledException)
                    {
                        Invoke(new Action(() =>
                        {
                            progressBar.Value = progressBar.Minimum;
                        }));
                    }

                    Invoke(new Action(() =>
                    {
                        abortButton.Enabled = false;
                    }));
                });
            }
            finally
            {
                taskIsRunning = false;
            }

        }

        private void button5_Click(object sender, EventArgs e)
        {
            var outputDir = @"C:\DS3TexUp\overwrite\m31";

            // RunTask(token =>
            // {
            //     var files = Directory.GetFiles(@"C:\Users\micha\Desktop\up_res_m31_a");
            //     token.SubmitStatus($"Converting {files.Length} files");
            //
            //     var done = 0;
            //     files.AsParallel().ForAll(file =>
            //     {
            //         lock (token)
            //         {
            //             if (token.IsCanceled) return;
            //         }
            //
            //         var dest = Path.GetFileNameWithoutExtension(file);
            //         var index = dest.IndexOf("-4x");
            //         if (index != -1) dest = dest.Substring(0, index);
            //         dest = Path.Join(outputDir, dest + ".dds");
            //         DDSConverter.ToDDS(file, dest);
            //
            //         lock (token)
            //         {
            //             if (token.IsCanceled) return;
            //             done++;
            //             token.SubmitProgress(done / (double)files.Length);
            //         }
            //     });
            // });

            //using var n = DS3NormalMap.Load(@"C:\DS3TexUp\extract\m31\m31_00_woodplank_10_n.dds");
            //n.SaveNormalAsPng(@"C:\Users\micha\Desktop\test\normal.png");
            //n.SaveGlossAsPng(@"C:\Users\micha\Desktop\test\gloss.png");
            //n.SaveHeightAsPng(@"C:\Users\micha\Desktop\test\height.png");

            var nAi = DS3NormalMap.Load(@"C:\Users\micha\Desktop\test\normal-ai.png");
            var nUp = DS3NormalMap.Load(@"C:\Users\micha\Desktop\test\normal-up.png");
            var start = DateTime.Now;
            nAi.Normals.CombineWith(nUp.Normals, 1f);
            MessageBox.Show((DateTime.Now - start).ToString());
            nAi.Normals.SaveAsPng(@"C:\Users\micha\Desktop\test\normal-ai-x-up.png");

            //DDSConverter.ToDDS(@"C:\Users\micha\Desktop\up_res_m31_a\m31_00_woodplank_10_a-4x-UltraSharp.50.4x_UniversalUpscalerV2-Neutral_115000_swaG.50.png", @"C:\DS3TexUp\overwrite\m31\m31_00_woodplank_10_a.dds");

            // var output = @"C:\Users\micha\Desktop\up\in\";
            // DDSConverter.ToPNG(@"C:\DS3TexUp\extract\m31\m31_00_woodplank_10_a.dds", output);
            // DDSConverter.ToPNG(@"C:\DS3TexUp\extract\m31\m31_00_grass_00_a.dds", output);
            // 
            // DDSConverter.ToDDS(@"C:\Users\micha\Desktop\up\in\m31_00_grass_00_a.png", output, DDSFormat.BC1_UNORM);
            // DDSConverter.ToDDS(@"C:\Users\micha\Desktop\up\in\m31_00_woodplank_10_a.png", output, DDSFormat.BC1_UNORM);

            // DDSConverter.ToPNG(@"C:\Users\micha\Desktop\up\m31_00_woodenhouse_05_a-4x-UltraSharp.png", @"C:\Users\micha\Desktop\up\in\m31_00_woodenhouse_05_a.png");

        }
    }
}

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

            // var nLow = DS3NormalMap.Load(@"C:\Users\micha\Desktop\test\m31_00_woodplank_10_n-bilinear-up.png");
            // var nHigh = DS3NormalMap.Load(@"C:\Users\micha\Desktop\test\normal-ai-x-up.png");
            // nLow.Normals.Set(nHigh);
            // nLow.SaveAsPng(@"C:\Users\micha\Desktop\test\final.png");


            var t = new TilingForm(GetWorkspace());
            t.ShowDialog();
        }
    }
}

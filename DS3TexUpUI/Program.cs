using System;
using System.Text;
using System.Windows.Forms;

namespace DS3TexUpUI
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Register encoding provider for Shift-JIS
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Application logic
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}

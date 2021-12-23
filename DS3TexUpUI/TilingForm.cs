using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DS3TexUpUI
{
    public partial class TilingForm : Form
    {
        public Workspace Workspace;
        private List<string> _allFiles;
        private HashSet<string> _tiles;
        private HashSet<string> _nonTiles;

        public string TilesFile => Path.Join(Workspace.TextureDir, "tiles.txt");
        public string NonTilesFile => Path.Join(Workspace.TextureDir, "non-tiles.txt");

        private string current = null;
        private IEnumerable<string> Remaining => _allFiles.Where(f => !_tiles.Contains(f) && !_nonTiles.Contains(f));

        public TilingForm(Workspace workspace)
        {
            InitializeComponent();

            textBox1.KeyPress += textBox1_KeyPress;

            Workspace = workspace;

            Shown += (sender, e) =>
            {
                _allFiles = GetAllFile(Workspace);
                _tiles = ReadTilesFile(TilesFile);
                _nonTiles = ReadTilesFile(NonTilesFile);
            };
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;

            if (current != null)
            {
                switch (e.KeyChar)
                {
                    case 't':
                        _tiles.Add(current);
                        break;
                    case 'n':
                        _nonTiles.Add(current);
                        break;
                    default:
                        return;
                }
            }

            Text = e.KeyChar.ToString();
            ShowNext();
        }

        void ShowNext()
        {
            var next = Remaining.FirstOrDefault();
            current = next;
            if (next == null)
            {
                MessageBox.Show("All done");
                return;
            }

            var file = GetFile(next);

            var t1 = LoadTileAsync(file);
            var t2 = SaveDataAsync();
            Task.WaitAll(new[] { t1, t2 });

            pictureBox1.Image = t1.Result;
        }

        string GetFile(string name)
        {
            return Path.Join(Workspace.ExtractDir, name.Replace('/', '\\') + ".dds");
        }

        async Task<Bitmap> LoadTileAsync(string file)
        {
            using var ddsImage = DDSImage.Load(file);
            using var image = ddsImage.ToImage();
            using var mb = new MemoryStream();
            SixLabors.ImageSharp.ImageExtensions.SaveAsBmp(image, mb);
            using var img = Image.FromStream(mb);

            var bmp = new Bitmap(img.Width, img.Height);
            var g = Graphics.FromImage(bmp);

            var tileW = bmp.Width / 2;
            var tileH = bmp.Height / 2;
            var tl = new Rectangle(0, 0, tileW, tileH);
            var tr = new Rectangle(tileW, 0, tileW, tileH);
            var bl = new Rectangle(0, tileH, tileW, tileH);
            var br = new Rectangle(tileW, tileH, tileW, tileH);
            g.DrawImage(img, tl, br, GraphicsUnit.Pixel);
            g.DrawImage(img, tr, bl, GraphicsUnit.Pixel);
            g.DrawImage(img, bl, tr, GraphicsUnit.Pixel);
            g.DrawImage(img, br, tl, GraphicsUnit.Pixel);

            return bmp;
        }

        Task SaveDataAsync()
        {
            var t1 = Task.Run(() => WriteTilesFile(TilesFile, _tiles));
            var t2 = Task.Run(() => WriteTilesFile(NonTilesFile, _nonTiles));
            return Task.WhenAll(new[] { t1, t2 });
        }


        static List<string> GetAllFile(Workspace workspace)
        {
            var files = new List<string>();
            foreach (var map in DS3.Maps)
            {
                files.AddRange(Directory.GetFiles(Path.Join(workspace.UpscaleDir, map, "a")).Select(Path.GetFileNameWithoutExtension).Select(n => map + "/" + n));
            }
            return files;
        }
        static void WriteTilesFile(string path, IEnumerable<string> data)
        {
            var list = data.ToList();
            list.Sort();
            File.WriteAllText(path, string.Join('\n', list), Encoding.UTF8);
        }
        static HashSet<string> ReadTilesFile(string path)
        {
            if (!File.Exists(path)) return new HashSet<string>();

            return File.ReadAllText(path, Encoding.UTF8).Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet();
        }
    }
}

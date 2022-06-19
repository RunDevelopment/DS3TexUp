using System;
using SixLabors.ImageSharp;

namespace DS3TexUpUI
{
    public struct Tile : IEquatable<Tile>
    {
        public int Fraction { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public Size Size
        {
            get => new Size(Width, Height);
            set => (Width, Height) = value;
        }

        public Tile(int fraction, int x, int y, int width, int height)
            => (Fraction, X, Y, Width, Height) = (fraction, x, y, width, height);
        public Tile(int fraction, int x, int y, Size size)
            => (Fraction, X, Y, Width, Height) = (fraction, x, y, size.Width, size.Height);

        public override bool Equals(object obj) => obj is Tile other ? Equals(other) : false;
        public bool Equals(Tile other)
        {
            return Fraction == other.Fraction
                && X == other.X
                && Y == other.Y
                && Width == other.Width
                && Height == other.Height;
        }
        public override int GetHashCode() => HashCode.Combine(Fraction, X, Y, Width, Height);

        public override string ToString() => $"Tile {Fraction} x:{X} y:{Y} w:{Width} h:{Height}";
        public static Tile Parse(string s) => Parse(s.AsSpan());
        public static Tile Parse(ReadOnlySpan<char> s)
        {
            if (!s.StartsWith("Tile ")) throw new FormatException();

            var parts = s.Slice(5).ToString().Split(' ');
            if (parts.Length != 5) throw new FormatException();

            var f = int.Parse(parts[0]);

            if (!parts[1].StartsWith("x:")) throw new FormatException();
            if (!parts[2].StartsWith("y:")) throw new FormatException();
            if (!parts[3].StartsWith("w:")) throw new FormatException();
            if (!parts[4].StartsWith("h:")) throw new FormatException();

            var x = int.Parse(parts[1].AsSpan().Slice(2));
            var y = int.Parse(parts[2].AsSpan().Slice(2));
            var w = int.Parse(parts[3].AsSpan().Slice(2));
            var h = int.Parse(parts[4].AsSpan().Slice(2));

            return new Tile(f, x, y, w, h);
        }

        public static bool operator ==(Tile l, Tile r) => l.Equals(r);
        public static bool operator !=(Tile l, Tile r) => !l.Equals(r);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SoulsFormats;

namespace DS3TexUpUI
{
    public readonly struct TexId : IEquatable<TexId>, IComparable<TexId>
    {
        public readonly string Value;
        private readonly int _sepIndex;

        public ReadOnlySpan<char> Category => Value.AsSpan(0, _sepIndex);
        public ReadOnlySpan<char> Name => Value.AsSpan(_sepIndex + 1);

        public TexId(string value)
        {
            Value = value;
            _sepIndex = value.IndexOf('/');

            if (_sepIndex == -1)
                throw new ArgumentException($"The given value '{value}' is invalid.");
        }

        public static TexId FromPath(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var index = name.IndexOf('-');
            if (index != -1) name = name.Substring(0, index);

            var dir = Path.GetFileName(Path.GetDirectoryName(path));

            return new TexId($"{dir}/{name}");
        }

        private static readonly Regex _mapPattern = new Regex(@"\A(?i:m)\d{2}\z");
        private static readonly Regex _chrPattern = new Regex(@"\A(?i:c)\d{4}\z");
        private static readonly Regex _partsPattern = new Regex(@"\A(?i:\w{2})_(?i:[AFM])_\d+\z");
        public static TexId? FromTexture(FLVER2.Texture texture, string? flverPath = null)
        {
            var name = Path.GetFileNameWithoutExtension(texture.Path);

            // Maps: N:\FDP\data\Model\map\m{00}\tex\name.ext
            // Chr: N:\FDP\data\Model\chr\c{0000}\tex\name.ext
            // Sfx: N:\FDP\data\Sfx\Tex\name.ext
            // Armor: N:\FDP\data\Model\parts\FullBody\FB_M_8800\BD_M_8800\tex\name.ext
            // Weapon: N:\FDP\data\Model\parts\Weapon\WP_A_1419\tex\name.ext

            var p = Path.GetDirectoryName(texture.Path);
            if (!Path.GetFileName(p).Equals("tex", StringComparison.OrdinalIgnoreCase)) return null;

            p = Path.GetDirectoryName(p);
            var n = Path.GetFileName(p);

            if (_mapPattern.IsMatch(n))
                return new TexId($"{n.ToLowerInvariant()}/{name}");

            if (_chrPattern.IsMatch(n))
                return new TexId($"chr/{n.ToLowerInvariant()}_{name}");

            if (_partsPattern.IsMatch(n))
                return new TexId($"parts/{n.ToUpperInvariant()}_{name}");

            if (n.Equals("sfx", StringComparison.OrdinalIgnoreCase))
            {
                // Interestingly, sfx paths do NOT uniquely identify a texture on disk.
                // We need the path of the flver file to get the texture id.

                // Example FLVER path: sfx\frpg_sfxbnd_m51_resource-ffxbnd-dcx\sfx\model\s09460.flver

                if (flverPath != null && Path.GetExtension(flverPath).Equals(".flver", StringComparison.OrdinalIgnoreCase))
                {
                    var d = Path.GetDirectoryName(flverPath);
                    if (Path.GetFileName(d).Equals("model", StringComparison.OrdinalIgnoreCase))
                    {
                        d = Path.GetDirectoryName(d);
                        if (Path.GetFileName(d).Equals("sfx", StringComparison.OrdinalIgnoreCase))
                        {
                            var dName = Path.GetFileName(Path.GetDirectoryName(d));
                            if (dName.StartsWith("frpg_sfxbnd_") && dName.EndsWith("_resource-ffxbnd-dcx"))
                            {
                                var id = dName.Substring("frpg_sfxbnd_".Length);
                                id = id.Substring(0, id.Length - "_resource-ffxbnd-dcx".Length);

                                return new TexId($"sfx/{id}_{name}");
                            }
                        }
                    }
                }
            }

            return null;
        }

        public bool Equals(TexId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TexId other ? Equals(other) : false;
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;
        public int CompareTo(TexId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);


        public TexKind GetTexKind()
        {
            if (DS3.KnownTexKinds.TryGetValue(this, out var kind))
                return kind;
            return GuessTexKind();
        }
        private TexKind GuessTexKind()
        {
            var n = Name;

            // by suffix
            if (n.EndsWith("_a")) return TexKind.Albedo;
            if (n.EndsWith("_n")) return TexKind.Normal;
            if (n.EndsWith("_r")) return TexKind.Reflective;
            if (n.EndsWith("_s")) return TexKind.Shininess;
            if (n.EndsWith("_em") || n.EndsWith("_e")) return TexKind.Emissive;
            if (n.EndsWith("_h") || n.EndsWith("_d")) return TexKind.Height;
            if (n.EndsWith("_v")) return TexKind.VertexOffset;
            if (n.EndsWith("_m") || n.EndsWith("_sm") || n.EndsWith("_mask")) return TexKind.Mask;

            // by substring
            if (n.Contains("_mask_".AsSpan(), StringComparison.OrdinalIgnoreCase)) return TexKind.Mask;
            if (n.Contains("_n_".AsSpan(), StringComparison.OrdinalIgnoreCase)) return TexKind.Normal;

            return TexKind.Unknown;
        }

        public TransparencyKind GetTransparency() => DS3.Transparency.GetOrDefault(this, TransparencyKind.Full);
    }

    public enum TexKind
    {
        Unknown = 0,
        Albedo = 1,
        Normal = 2,
        Reflective = 3,
        Shininess = 4,
        Emissive = 5,
        Mask = 6,
        /// A displacement and/or height map.
        Height = 7,
        /// A flor and/or vertex offset map.
        VertexOffset = 8,
    }

    public static class TexKindExtensions
    {
        public static string GetShortName(this TexKind textureKind)
        {
            switch (textureKind)
            {
                case TexKind.Albedo:
                    return "a";
                case TexKind.Normal:
                    return "n";
                case TexKind.Reflective:
                    return "r";
                case TexKind.Shininess:
                    return "s";
                case TexKind.Emissive:
                    return "em";
                case TexKind.Mask:
                    return "m";
                case TexKind.Height:
                    return "h";
                case TexKind.VertexOffset:
                    return "v";
                default:
                    return "other";
            }
        }
    }
}

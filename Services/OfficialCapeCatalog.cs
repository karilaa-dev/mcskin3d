using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace MCSkin3D
{
    // Reference artwork lives in the application, never in the user's editable library.
    public sealed class OfficialCape
    {
        public string Id { get; }
        public string Name { get; }
        public string Source { get; }
        public DateOnly Released { get; }
        public string ReleaseLabel { get; }
        internal OfficialCape(XElement entry)
        {
            Id = (string)entry.Attribute("id");
            Name = (string)entry.Attribute("name");
            Source = (string)entry.Attribute("source");
            ReleaseLabel = (string)entry.Attribute("released");
            // A few historical capes only have a documented release year.
            Released = DateOnly.ParseExact(ReleaseLabel.Length == 4 ? ReleaseLabel + "-01-01" : ReleaseLabel,
                "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public Bitmap OpenTexture()
        {
            using var stream = OfficialCapeCatalog.OpenResource(Id + ".png");
            using var image = new Bitmap(stream);
            return new Bitmap(image);
        }

        public override string ToString() => Name;
    }

    public static class OfficialCapeCatalog
    {
        private static readonly Lazy<IReadOnlyList<OfficialCape>> Catalog = new Lazy<IReadOnlyList<OfficialCape>>(() =>
        {
            using var stream = OpenResource("catalog.xml");
            return Array.AsReadOnly(XDocument.Load(stream).Root.Elements("cape")
                .Select(entry => new OfficialCape(entry)).OrderByDescending(cape => cape.Released)
                .ThenBy(cape => cape.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        });

        public static IReadOnlyList<OfficialCape> All => Catalog.Value;
        internal static Stream OpenResource(string name) => typeof(OfficialCapeCatalog).Assembly
            .GetManifestResourceStream("MCSkin3D.OfficialCapes." + name)
            ?? throw new InvalidDataException("Missing official cape asset: " + name);
    }
}

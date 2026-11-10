using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace HaiView
{
    /// <summary>Bilingual strings: L("English", "Tiếng Việt").</summary>
    public static class Lang
    {
        public static bool Vietnamese { get; private set; }

        public static void Init(string? forced)
        {
            Vietnamese = forced switch
            {
                "vi" => true,
                "en" => false,
                _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi",
            };
        }

        public static string L(string en, string vi) => Vietnamese ? vi : en;
    }

    /// <summary>
    /// Layout tree: a node is either a cell (leaf) or a split into children with ratios.
    /// Axis "h" = children side by side (left → right), "v" = stacked (top → bottom).
    /// </summary>
    public sealed class LayoutNode
    {
        public string? Axis { get; set; }
        public List<double>? Ratios { get; set; }
        public List<LayoutNode>? Children { get; set; }

        [JsonIgnore] public bool IsLeaf => Children == null || Children.Count == 0;

        public static LayoutNode Leaf() => new LayoutNode();

        public static LayoutNode Split(string axis, double[] ratios, params LayoutNode[] children) =>
            new LayoutNode { Axis = axis, Ratios = ratios.ToList(), Children = children.ToList() };

        public static LayoutNode Row(int n) => n <= 1 ? Leaf()
            : Split("h", Enumerable.Repeat(1.0 / n, n).ToArray(), Enumerable.Range(0, n).Select(_ => Leaf()).ToArray());

        public static LayoutNode Column(int n) => n <= 1 ? Leaf()
            : Split("v", Enumerable.Repeat(1.0 / n, n).ToArray(), Enumerable.Range(0, n).Select(_ => Leaf()).ToArray());

        public static LayoutNode Grid(int rows, int cols) => rows <= 1 ? Row(cols)
            : Split("v", Enumerable.Repeat(1.0 / rows, rows).ToArray(), Enumerable.Range(0, rows).Select(_ => Row(cols)).ToArray());

        public int LeafCount => IsLeaf ? 1 : Children!.Sum(c => c.LeafCount);

        public LayoutNode Clone() => JsonSerializer.Deserialize<LayoutNode>(JsonSerializer.Serialize(this))!;

        public string Signature() => JsonSerializer.Serialize(this);

        public sealed record LeafInfo(int Index, int[] Path, Rect Rect);

        public sealed record DividerInfo(int[] Path, int Index, bool Horizontal, Rect Parent, double Position)
        {
            public string Id => string.Join(".", Path) + "#" + Index;
        }

        public (List<LeafInfo> Leaves, List<DividerInfo> Dividers) Layout(Rect rect)
        {
            var leaves = new List<LeafInfo>();
            var dividers = new List<DividerInfo>();
            int counter = 0;

            void Walk(LayoutNode n, Rect r, int[] path)
            {
                if (n.IsLeaf)
                {
                    leaves.Add(new LeafInfo(counter++, path, r));
                    return;
                }
                bool h = n.Axis == "h";
                double offset = 0;
                for (int i = 0; i < n.Children!.Count; i++)
                {
                    double f = n.Ratios![i];
                    var sub = h
                        ? new Rect(r.X + offset * r.Width, r.Y, f * r.Width, r.Height)
                        : new Rect(r.X, r.Y + offset * r.Height, r.Width, f * r.Height);
                    Walk(n.Children[i], sub, path.Append(i).ToArray());
                    offset += f;
                    if (i < n.Children.Count - 1)
                    {
                        double pos = h ? r.X + offset * r.Width : r.Y + offset * r.Height;
                        dividers.Add(new DividerInfo(path, i, h, r, pos));
                    }
                }
            }

            Walk(this, rect, Array.Empty<int>());
            return (leaves, dividers);
        }

        public LayoutNode NodeAt(int[] path)
        {
            var n = this;
            foreach (var i in path)
            {
                if (n.IsLeaf || i >= n.Children!.Count) break;
                n = n.Children[i];
            }
            return n;
        }

        /// <summary>Split a cell in two. Joins the parent if it already splits the same way.</summary>
        public void SplitLeaf(int[] path, string axis)
        {
            if (path.Length > 0)
            {
                var parent = NodeAt(path[..^1]);
                int last = path[^1];
                if (!parent.IsLeaf && parent.Axis == axis)
                {
                    double half = parent.Ratios![last] / 2;
                    parent.Ratios[last] = half;
                    parent.Ratios.Insert(last + 1, half);
                    parent.Children!.Insert(last + 1, Leaf());
                    return;
                }
            }
            var node = NodeAt(path);
            node.Axis = axis;
            node.Ratios = new List<double> { 0.5, 0.5 };
            node.Children = new List<LayoutNode> { Leaf(), Leaf() };
        }

        /// <summary>Remove a cell; its space goes to its siblings.</summary>
        public void RemoveLeaf(int[] path)
        {
            if (path.Length == 0) return;
            var parent = NodeAt(path[..^1]);
            int last = path[^1];
            if (parent.IsLeaf) return;
            double freed = parent.Ratios![last];
            parent.Ratios.RemoveAt(last);
            parent.Children!.RemoveAt(last);
            double rest = parent.Ratios.Sum();
            for (int i = 0; i < parent.Ratios.Count; i++)
                parent.Ratios[i] += freed * (parent.Ratios[i] / Math.Max(rest, 0.0001));
            if (parent.Children.Count == 1)
            {
                var only = parent.Children[0];
                parent.Axis = only.Axis;
                parent.Ratios = only.Ratios;
                parent.Children = only.Children;
            }
        }
    }

    public sealed class LayoutPreset
    {
        public string Title { get; set; } = "";
        public LayoutNode Tree { get; set; } = LayoutNode.Leaf();

        public static List<LayoutPreset> BuiltIn() => new()
        {
            new() { Title = Lang.L("1 page", "1 trang"), Tree = LayoutNode.Leaf() },
            new() { Title = Lang.L("2 side by side", "2 trang cạnh nhau"), Tree = LayoutNode.Row(2) },
            new() { Title = Lang.L("2 stacked", "2 trang trên dưới"), Tree = LayoutNode.Column(2) },
            new() { Title = Lang.L("3 side by side", "3 trang cạnh nhau"), Tree = LayoutNode.Row(3) },
            new() { Title = Lang.L("2 top, 1 bottom", "2 trên, 1 dưới"), Tree = LayoutNode.Split("v", new[] { 0.5, 0.5 }, LayoutNode.Row(2), LayoutNode.Leaf()) },
            new() { Title = Lang.L("1 top, 2 bottom", "1 trên, 2 dưới"), Tree = LayoutNode.Split("v", new[] { 0.5, 0.5 }, LayoutNode.Leaf(), LayoutNode.Row(2)) },
            new() { Title = Lang.L("1 large left, 2 right", "1 lớn trái, 2 nhỏ phải"), Tree = LayoutNode.Split("h", new[] { 0.62, 0.38 }, LayoutNode.Leaf(), LayoutNode.Column(2)) },
            new() { Title = Lang.L("2 left, 1 large right", "2 nhỏ trái, 1 lớn phải"), Tree = LayoutNode.Split("h", new[] { 0.38, 0.62 }, LayoutNode.Column(2), LayoutNode.Leaf()) },
            new() { Title = Lang.L("Grid 2 × 2", "Lưới 2 × 2"), Tree = LayoutNode.Grid(2, 2) },
            new() { Title = Lang.L("Grid 2 × 3", "Lưới 2 × 3"), Tree = LayoutNode.Grid(2, 3) },
        };
    }

    /// <summary>User settings, saved to %APPDATA%\HaiView\settings.json.</summary>
    public sealed class Settings
    {
        public LayoutNode? Tree { get; set; }
        public string? LayoutName { get; set; }
        public List<LayoutPreset> Saved { get; set; } = new();
        public int GridRows { get; set; } = 2;
        public int GridCols { get; set; } = 2;
        public double Gap { get; set; } = 6;
        public bool ShowLabels { get; set; } = true;
        public bool StepByGroup { get; set; } = true;
        public string? Language { get; set; }

        public static string Folder =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HaiView");

        static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}

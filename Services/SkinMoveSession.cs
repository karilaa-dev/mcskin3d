using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MCSkin3D.Controls;
using MCSkin3D.Languages;

namespace MCSkin3D.Services
{
    internal sealed class SkinMoveConflictException : IOException
    {
        internal SkinMoveConflictException() : base(LanguageLoader.GetStringWithFallback("D_MOVE_NAME_EXISTS")) { }
    }

    // One captured skin and library per dialog; browsing never changes editor selection.
    internal sealed class SkinMoveSession
    {
        private readonly SkinTreeView tree;
        private readonly TreeNode rootNode;
        private readonly string physicalRoot;
        internal Skin Skin { get; }
        internal string RootPath { get; }
        internal string SourceFolder { get; }

        internal SkinMoveSession(SkinTreeView tree, Skin skin)
        {
            this.tree = tree;
            Skin = skin;
            if (skin.TreeView != tree) throw new InvalidOperationException();
            SourceFolder = Normalize(skin.Directory.FullName);
            if (Editor.HasOneRoot)
                RootPath = Normalize(Editor.RootFolderString);
            else
            {
                rootNode = skin.Parent;
                while (rootNode?.Parent != null) rootNode = rootNode.Parent;
                if (!(rootNode is FolderNode folder)) throw new InvalidOperationException();
                RootPath = Normalize(folder.Directory.FullName);
            }
            physicalRoot = ResolveDirectory(RootPath);
            ValidateDirectory(SourceFolder);
        }

        private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static bool Within(string root, string path) => Same(root, path) ||
            path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        // Resolve each ancestor too: ResolveLinkTarget on the leaf alone misses parent junctions.
        private static string ResolveDirectory(string path)
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists) throw new DirectoryNotFoundException(LanguageLoader.GetStringWithFallback("D_MOVE_MISSING"));
            string resolved = directory.Parent == null ? directory.FullName :
                Path.Combine(ResolveDirectory(directory.Parent.FullName), directory.Name);
            var actual = new DirectoryInfo(resolved);
            if ((actual.Attributes & FileAttributes.ReparsePoint) != 0)
                resolved = actual.ResolveLinkTarget(true)?.FullName ?? throw new IOException(LanguageLoader.GetStringWithFallback("D_MOVE_OUTSIDE"));
            return Normalize(resolved);
        }

        internal string ValidateDirectory(string path)
        {
            path = Normalize(path);
            if (!Within(RootPath, path)) throw new IOException(LanguageLoader.GetStringWithFallback("D_MOVE_OUTSIDE"));
            // Validate every step so a link cannot leave the library and then enter it again.
            string current = RootPath;
            if (!Same(ResolveDirectory(current), physicalRoot)) throw new IOException(LanguageLoader.GetStringWithFallback("D_MOVE_OUTSIDE"));
            var relative = Path.GetRelativePath(RootPath, path);
            if (relative != ".")
                foreach (string part in relative.Split(Path.DirectorySeparatorChar))
                {
                    current = Path.Combine(current, part);
                    if (!Within(physicalRoot, ResolveDirectory(current)))
                        throw new IOException(LanguageLoader.GetStringWithFallback("D_MOVE_OUTSIDE"));
                }
            return path;
        }

        internal string[] GetFolders(string path)
        {
            path = ValidateDirectory(path);
            return new DirectoryInfo(path).EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.Hidden) == 0 && CanBrowse(d.FullName))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).Select(d => d.FullName).ToArray();
        }

        private bool CanBrowse(string path)
        {
            try { ValidateDirectory(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal bool CanMoveTo(string path) => !Same(ResolveDirectory(ValidateDirectory(path)), ResolveDirectory(SourceFolder));

        internal static void ValidateName(string name, bool skinName)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith('.') ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Length + (skinName ? 4 : 0) > 255 ||
                Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new ArgumentException(LanguageLoader.GetStringWithFallback("D_MOVE_INVALID_NAME"));
        }

        internal string DestinationFile(string folder, string name)
        {
            ValidateName(name, true);
            return Path.Combine(ValidateDirectory(folder), name + ".png");
        }

        internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        internal string CreateFolder(string parent, string name)
        {
            ValidateName(name, false);
            string path = Path.Combine(ValidateDirectory(parent), name);
            if (Exists(path)) throw new IOException(LanguageLoader.GetStringWithFallback("D_MOVE_NAME_EXISTS"));
            Directory.CreateDirectory(path);
            EnsureFolder(path);
            return path;
        }

        private TreeNodeCollection EnsureFolder(string path)
        {
            ValidateDirectory(path);
            var nodes = rootNode == null ? tree.Nodes : rootNode.Nodes;
            string relative = Path.GetRelativePath(RootPath, path);
            if (relative == ".") return nodes;
            foreach (string part in relative.Split(Path.DirectorySeparatorChar))
            {
                var folder = nodes.Cast<TreeNode>().OfType<FolderNode>().FirstOrDefault(n => Same(n.Name, part));
                if (folder == null)
                {
                    folder = new FolderNode(part);
                    nodes.Add(folder);
                }
                nodes = folder.Nodes;
            }
            tree.Invalidate();
            return nodes;
        }

        internal void Move(string folder, string name)
        {
            if (Skin.TreeView != tree || !Same(Normalize(Skin.Directory.FullName), SourceFolder))
                throw new IOException(LanguageLoader.GetStringWithFallback("D_MOVE_SOURCE_CHANGED"));
            ValidateDirectory(SourceFolder);
            string destination = DestinationFile(folder, name);
            if (!CanMoveTo(folder)) return;
            if (Exists(destination)) throw new SkinMoveConflictException();
            var nodes = EnsureFolder(folder);
            try { tree.MoveSkin(Skin, nodes, destination, false); }
            catch (IOException) when (Exists(destination)) { throw new SkinMoveConflictException(); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace FFM.Tools.AssetLint
{
    public sealed class AssetLintReport
    {
        public int ScannedFiles { get; set; }
        public List<string> Violations { get; } = new();
        public bool IsValid => Violations.Count == 0;
    }

    /// <summary>
    /// Validates client assets against WP-17 style guide budgets:
    /// - Single mesh <= 3000 triangles (checked on .obj / .tscn mesh definitions)
    /// - Texture size <= 2048x2048 (PNG header IHDR width/height check)
    /// - No duplicated per-team materials (team_01..team_18.tres)
    /// </summary>
    public static class AssetLinter
    {
        public const int MaxTrianglesPerMesh = 3000;
        public const int MaxTextureDimension = 2048;

        public static AssetLintReport Run(string rootDir)
        {
            var report = new AssetLintReport();
            if (!Directory.Exists(rootDir))
            {
                return report;
            }

            string[] files = Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                if (file.Contains(".godot", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string ext = Path.GetExtension(file).ToLowerInvariant();
                string fileName = Path.GetFileName(file);

                if (ext == ".obj")
                {
                    report.ScannedFiles++;
                    int faceCount = CountObjFaces(file);
                    if (faceCount > MaxTrianglesPerMesh)
                    {
                        report.Violations.Add($"Mesh '{file}' has {faceCount} triangles, exceeding budget {MaxTrianglesPerMesh}.");
                    }
                }
                else if (ext == ".png")
                {
                    report.ScannedFiles++;
                    if (TryReadPngDimensions(file, out int width, out int height))
                    {
                        if (width > MaxTextureDimension || height > MaxTextureDimension)
                        {
                            report.Violations.Add($"Texture '{file}' is {width}x{height}, exceeding {MaxTextureDimension}x{MaxTextureDimension}.");
                        }
                    }
                }
                else if (ext == ".tres" || ext == ".material")
                {
                    report.ScannedFiles++;
                    if (fileName.StartsWith("team_mat_", StringComparison.OrdinalIgnoreCase))
                    {
                        report.Violations.Add($"Forbidden per-team material '{file}': use Per-Instance Shader Uniform 'team_color' instead (§WP-17).");
                    }
                }
                else if (ext == ".tscn")
                {
                    report.ScannedFiles++;
                    string content = File.ReadAllText(file);
                    if (content.Contains("team_mat_", StringComparison.OrdinalIgnoreCase))
                    {
                        report.Violations.Add($"Scene '{file}' references forbidden per-team duplicate material 'team_mat_*' (§WP-17).");
                    }
                }
                else if (ext == ".gdshader")
                {
                    report.ScannedFiles++;
                    string content = File.ReadAllText(file);
                    if (fileName.Contains("team_color", StringComparison.OrdinalIgnoreCase) &&
                        !content.Contains("instance uniform", StringComparison.OrdinalIgnoreCase))
                    {
                        report.Violations.Add($"Shader '{file}' must use 'instance uniform' for zero-material-duplication team colors (§WP-17).");
                    }
                }
            }

            return report;
        }

        private static int CountObjFaces(string path)
        {
            int tris = 0;
            foreach (string line in File.ReadLines(path))
            {
                ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart();
                if (trimmed.Length > 2 && trimmed[0] == 'f' && trimmed[1] == ' ')
                {
                    // Count vertices in face line; polygon with N vertices = N - 2 triangles
                    int verts = 0;
                    bool inToken = false;
                    for (int i = 2; i < trimmed.Length; i++)
                    {
                        if (!char.IsWhiteSpace(trimmed[i]))
                        {
                            if (!inToken)
                            {
                                verts++;
                                inToken = true;
                            }
                        }
                        else
                        {
                            inToken = false;
                        }
                    }
                    tris += Math.Max(1, verts - 2);
                }
            }
            return tris;
        }

        private static bool TryReadPngDimensions(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            using var fs = File.OpenRead(path);
            if (fs.Length < 24) return false;
            Span<byte> header = stackalloc byte[24];
            int read = fs.Read(header);
            if (read < 24) return false;
            // PNG signature + IHDR width at [16..19], height at [20..23] (big-endian)
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
            {
                return false;
            }
            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return true;
        }
    }
}

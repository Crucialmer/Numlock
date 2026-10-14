using System.Drawing.Imaging;

namespace Numlock;

/// <summary>生成多尺寸应用图标 assets\Numlock.ico（Numlock.exe --genico &lt;路径&gt;）。</summary>
internal static class AppIcoBuilder
{
    /// <summary>返回进程退出码：0 成功、1 失败（错误写入 &lt;路径&gt;.error.txt）。</summary>
    public static int Run(string icoPath)
    {
        string full = Path.GetFullPath(icoPath);
        string tmp = full + ".tmp";
        try
        {
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 覆盖常见 DPI 档位（100%–400%）
            int[] sizes = { 16, 20, 24, 28, 32, 36, 40, 48, 64, 96, 128, 256 };
            var frames = new List<(int Size, byte[] Png)>();
            foreach (int s in sizes)
            {
                using var bmp = IconFactory.RenderAppTile(s);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                frames.Add((s, ms.ToArray()));
            }

            // 先写临时文件再原子替换：中途失败不会毁掉上一份可用的 ico
            using (var fs = File.Create(tmp))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((ushort)0); // reserved
                bw.Write((ushort)1); // type: icon
                bw.Write((ushort)frames.Count);

                int offset = 6 + 16 * frames.Count;
                foreach (var (size, png) in frames)
                {
                    bw.Write((byte)(size >= 256 ? 0 : size));
                    bw.Write((byte)(size >= 256 ? 0 : size));
                    bw.Write((byte)0);    // 调色板
                    bw.Write((byte)0);    // reserved
                    bw.Write((ushort)1);  // planes
                    bw.Write((ushort)32); // bpp
                    bw.Write(png.Length);
                    bw.Write(offset);
                    offset += png.Length;
                }
                foreach (var (_, png) in frames)
                {
                    bw.Write(png);
                }
            }
            File.Move(tmp, full, overwrite: true);
            return 0;
        }
        catch (Exception ex)
        {
            try { File.Delete(tmp); } catch { } // 失败路径不残留 .tmp
            try { File.WriteAllText(full + ".error.txt", ex.ToString()); } catch { }
            return 1;
        }
    }
}


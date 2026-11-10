using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HaiView
{
    /// <summary>PDF document rendered with the PDF engine built into Windows (Windows.Data.Pdf).</summary>
    public sealed class PdfSource
    {
        readonly PdfDocument doc;
        readonly Dictionary<(int, int), BitmapSource> cache = new();
        readonly Queue<(int, int)> order = new();
        readonly Dictionary<int, System.Windows.Size> sizes = new();
        readonly SemaphoreSlim gate = new(1, 1);

        PdfSource(PdfDocument d) { doc = d; }

        public int PageCount => (int)doc.PageCount;

        public static async Task<PdfSource> LoadAsync(string path)
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var d = await PdfDocument.LoadFromFileAsync(file);
            return new PdfSource(d);
        }

        public System.Windows.Size PageSize(int i)
        {
            if (sizes.TryGetValue(i, out var s)) return s;
            using var page = doc.GetPage((uint)i);
            s = new System.Windows.Size(page.Size.Width, page.Size.Height);
            sizes[i] = s;
            return s;
        }

        /// <summary>Render a page at about <paramref name="pixelWidth"/> pixels wide (rounded up to 64 px steps).</summary>
        public async Task<BitmapSource?> RenderAsync(int index, int pixelWidth)
        {
            if (index < 0 || index >= PageCount) return null;
            int bucket = Math.Clamp((pixelWidth + 63) / 64 * 64, 64, 4096);
            if (cache.TryGetValue((index, bucket), out var hit)) return hit;

            await gate.WaitAsync();
            try
            {
                if (cache.TryGetValue((index, bucket), out hit)) return hit;
                using var page = doc.GetPage((uint)index);
                using var stream = new InMemoryRandomAccessStream();
                var opts = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)bucket,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                };
                await page.RenderToStreamAsync(stream, opts);
                stream.Seek(0);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream.AsStream();
                bmp.EndInit();
                bmp.Freeze();

                cache[(index, bucket)] = bmp;
                order.Enqueue((index, bucket));
                while (order.Count > 120) cache.Remove(order.Dequeue());
                return bmp;
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>Converts PowerPoint files to PDF using Microsoft PowerPoint installed on this PC (COM automation).</summary>
    public static class PowerPointConverter
    {
        public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
            { ".ppt", ".pptx", ".pps", ".ppsx", ".pptm", ".potx" };

        static string CacheFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HaiView", "cache");

        static string CachedPdfPath(string src)
        {
            var fi = new FileInfo(src);
            string key = $"{fi.FullName}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
            return Path.Combine(CacheFolder, Path.GetFileNameWithoutExtension(src) + "-" + hash + ".pdf");
        }

        public static Task<string> ConvertAsync(string src, bool force)
        {
            string output = CachedPdfPath(src);
            if (!force && File.Exists(output)) return Task.FromResult(output);
            Directory.CreateDirectory(CacheFolder);

            var tcs = new TaskCompletionSource<string>();
            // COM automation needs a single-threaded apartment thread.
            var thread = new Thread(() =>
            {
                try
                {
                    var type = Type.GetTypeFromProgID("PowerPoint.Application");
                    if (type == null)
                        throw new InvalidOperationException(Lang.L(
                            "Microsoft PowerPoint was not found on this PC. Export your slides to PDF and open the PDF instead.",
                            "Không thấy Microsoft PowerPoint trên máy. Hãy xuất slide ra PDF rồi mở file PDF."));
                    bool wasRunning = Process.GetProcessesByName("POWERPNT").Length > 0;
                    dynamic app = Activator.CreateInstance(type)!;
                    // Open(FileName, ReadOnly = msoTrue, Untitled = msoFalse, WithWindow = msoFalse)
                    dynamic pres = app.Presentations.Open(src, -1, 0, 0);
                    if (File.Exists(output)) File.Delete(output);
                    pres.SaveAs(output, 32); // 32 = ppSaveAsPDF
                    pres.Close();
                    if (!wasRunning && app.Presentations.Count == 0) app.Quit();
                    tcs.SetResult(output);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }
    }
}

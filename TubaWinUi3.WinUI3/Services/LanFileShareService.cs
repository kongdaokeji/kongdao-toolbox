using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services;

public static class LanFileShareService
{
    private static HttpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static readonly ConcurrentDictionary<string, SharedFileInfo> _sharedFiles = new();
    private static string _shareDir = "";
    private static int _port = 18080;
    private static bool _isRunning;

    public static bool IsRunning => _isRunning;
    public static int Port => _port;
    public static string ShareDir => _shareDir;
    public static IReadOnlyDictionary<string, SharedFileInfo> SharedFiles => _sharedFiles;
    public static event Action? StateChanged;

    public static string GetLocalIp()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    public static void Initialize()
    {
        _shareDir = Path.Combine(ConfigManager.GetDataDir(), "LanShare");
        Directory.CreateDirectory(_shareDir);
    }

    public static void SetPort(int port)
    {
        _port = port;
        StateChanged?.Invoke();
    }

    public static async Task StartAsync()
    {
        if (_isRunning) return;
        Initialize();

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
                break;
            }
            catch
            {
                _listener.Close();
                _listener = new HttpListener();
                _port++;
                if (attempt == 9) throw;
            }
        }

        _isRunning = true;
        RefreshSharedFiles();
        StateChanged?.Invoke();

        _ = Task.Run(() => ListenLoop(_cts.Token));
        await Task.CompletedTask;
    }

    public static void Stop()
    {
        if (!_isRunning) return;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _isRunning = false;
        StateChanged?.Invoke();
    }

    public static void RefreshSharedFiles()
    {
        _sharedFiles.Clear();
        if (!Directory.Exists(_shareDir)) return;

        foreach (var file in Directory.GetFiles(_shareDir, "*", SearchOption.AllDirectories))
        {
            var fi = new FileInfo(file);
            var relPath = Path.GetRelativePath(_shareDir, file);
            _sharedFiles[relPath.Replace('\\', '/')] = new SharedFileInfo
            {
                RelativePath = relPath.Replace('\\', '/'),
                FileName = fi.Name,
                Size = fi.Length,
                LastModified = fi.LastWriteTimeUtc,
                ContentType = GetContentType(fi.Extension)
            };
        }
    }

    public static async Task AddFileAsync(string sourcePath, bool copy = true)
    {
        if (!File.Exists(sourcePath)) return;
        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(_shareDir, fileName);

        var counter = 1;
        while (File.Exists(destPath))
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(_shareDir, $"{nameNoExt} ({counter}){ext}");
            counter++;
        }

        if (copy)
            File.Copy(sourcePath, destPath);
        else
            File.Move(sourcePath, destPath);

        RefreshSharedFiles();
        StateChanged?.Invoke();
    }

    public static async Task AddFolderAsync(string sourceDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        var folderName = Path.GetFileName(sourceDir);
        var destDir = Path.Combine(_shareDir, folderName);

        var counter = 1;
        while (Directory.Exists(destDir))
        {
            destDir = Path.Combine(_shareDir, $"{folderName} ({counter})");
            counter++;
        }

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile);
        }

        RefreshSharedFiles();
        StateChanged?.Invoke();
    }

    public static void RemoveFile(string relativePath)
    {
        var fullPath = Path.Combine(_shareDir, relativePath.Replace('/', '\\'));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            RefreshSharedFiles();
            StateChanged?.Invoke();
        }
    }

    public static void RemoveFolder(string relativePath)
    {
        var fullPath = Path.Combine(_shareDir, relativePath.Replace('/', '\\'));
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, true);
            RefreshSharedFiles();
            StateChanged?.Invoke();
        }
    }

    public static void ClearAll()
    {
        if (Directory.Exists(_shareDir))
        {
            Directory.Delete(_shareDir, true);
            Directory.CreateDirectory(_shareDir);
        }
        RefreshSharedFiles();
        StateChanged?.Invoke();
    }

    private static async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(ctx, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
            catch { }
        }
    }

    private static async Task HandleRequest(HttpListenerContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;

        try
        {
            if (path == "/" || path == "/index.html")
            {
                await ServeHtml(ctx);
            }
            else if (path == "/api/info")
            {
                await ApiInfo(ctx);
            }
            else if (path == "/api/files")
            {
                if (method == "GET")
                    await ApiListFiles(ctx);
                else if (method == "DELETE")
                    await ApiDeleteFile(ctx);
            }
            else if (path == "/api/upload" && method == "POST")
            {
                await ApiUpload(ctx, ct);
            }
            else if (path == "/api/folder" && method == "POST")
            {
                await ApiCreateFolder(ctx);
            }
            else if (path == "/api/clear" && method == "DELETE")
            {
                await ApiClearAll(ctx);
            }
            else if (path == "/qr")
            {
                await ServeQrCode(ctx);
            }
            else if (path.StartsWith("/download/"))
            {
                await ServeFile(ctx, path["/download/".Length..]);
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch (Exception ex)
        {
            try
            {
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }));
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(errBytes, ct);
                ctx.Response.Close();
            }
            catch { }
        }
    }

    private static async Task ServeHtml(HttpListenerContext ctx)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var html = FluentPageHtml.GetHtml();
        var bytes = Encoding.UTF8.GetBytes(html);
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ApiInfo(HttpListenerContext ctx)
    {
        string ip;
        try { ip = GetLocalIp(); }
        catch { ip = "127.0.0.1"; }

        var url = $"http://{ip}:{_port}/";
        var json = JsonSerializer.Serialize(new { ip, port = _port, url });
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = 200;
        var bytes = Encoding.UTF8.GetBytes(json);
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ApiListFiles(HttpListenerContext ctx)
    {
        RefreshSharedFiles();
        var list = _sharedFiles.Values.Select(f => new
        {
            f.RelativePath,
            f.FileName,
            f.Size,
            LastModified = f.LastModified.ToString("o"),
            f.ContentType,
            IsFolder = false
        }).ToList();

        if (Directory.Exists(_shareDir))
        {
            foreach (var dir in Directory.GetDirectories(_shareDir, "*", SearchOption.TopDirectoryOnly))
            {
                var relPath = Path.GetRelativePath(_shareDir, dir).Replace('\\', '/');
                if (list.Any(x => x.RelativePath.StartsWith(relPath + "/"))) continue;
                list.Add(new
                {
                    RelativePath = relPath,
                    FileName = Path.GetFileName(dir),
                    Size = (long)0,
                    LastModified = Directory.GetLastWriteTimeUtc(dir).ToString("o"),
                    ContentType = "",
                    IsFolder = true
                });
            }
        }

        var json = JsonSerializer.Serialize(list);
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ApiDeleteFile(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream);
        var body = await reader.ReadToEndAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(body);
        var relPath = data.GetProperty("path").GetString() ?? "";

        var fullPath = Path.Combine(_shareDir, relPath.Replace('/', '\\'));
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);

        RefreshSharedFiles();
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ApiUpload(HttpListenerContext ctx, CancellationToken ct)
    {
        var contentType = ctx.Request.ContentType ?? "";
        if (contentType.Contains("multipart/form-data"))
        {
            var boundary = ExtractBoundary(contentType);
            await ParseMultipartUpload(ctx, boundary, ct);
        }
        else
        {
            var fileName = ctx.Request.Headers["X-File-Name"] ?? "upload";
            fileName = Uri.UnescapeDataString(fileName);
            var destPath = Path.Combine(_shareDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var fs = File.Create(destPath);
            await ctx.Request.InputStream.CopyToAsync(fs, ct);
        }

        RefreshSharedFiles();
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ApiCreateFolder(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream);
        var body = await reader.ReadToEndAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(body);
        var folderName = data.GetProperty("name").GetString() ?? "New Folder";
        var fullPath = Path.Combine(_shareDir, folderName.Replace('/', '\\'));
        Directory.CreateDirectory(fullPath);
        RefreshSharedFiles();
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ApiClearAll(HttpListenerContext ctx)
    {
        ClearAll();
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task ServeQrCode(HttpListenerContext ctx)
    {
        var ip = GetLocalIp();
        var url = $"http://{ip}:{_port}/";
        var png = QrCodeGenerator.GeneratePng(url, 200);
        ctx.Response.ContentType = "image/png";
        ctx.Response.ContentLength64 = png.Length;
        await ctx.Response.OutputStream.WriteAsync(png);
        ctx.Response.Close();
    }

    private static async Task ServeFile(HttpListenerContext ctx, string relPath)
    {
        relPath = Uri.UnescapeDataString(relPath);
        var fullPath = Path.Combine(_shareDir, relPath.Replace('/', '\\'));

        if (!File.Exists(fullPath))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var fi = new FileInfo(fullPath);
        ctx.Response.ContentType = GetContentType(fi.Extension);
        ctx.Response.ContentLength64 = fi.Length;
        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(fi.Name)}\"");

        using var fs = fi.OpenRead();
        await fs.CopyToAsync(ctx.Response.OutputStream);
        ctx.Response.Close();
    }

    private static string ExtractBoundary(string contentType)
    {
        var parts = contentType.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                return trimmed["boundary=".Length..].Trim('"');
        }
        return "----boundary";
    }

    private static async Task ParseMultipartUpload(HttpListenerContext ctx, string boundary, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await ctx.Request.InputStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        var data = ms.ToArray();
        var boundaryBytes = Encoding.UTF8.GetBytes($"--{boundary}");
        var endBoundaryBytes = Encoding.UTF8.GetBytes($"--{boundary}--");

        var pos = 0;
        while (pos < data.Length)
        {
            var boundaryStart = IndexOf(data, boundaryBytes, pos);
            if (boundaryStart < 0) break;

            var headerStart = boundaryStart + boundaryBytes.Length + 2;
            var headerEnd = IndexOf(data, Encoding.UTF8.GetBytes("\r\n\r\n"), headerStart);
            if (headerEnd < 0) break;

            var headerText = Encoding.UTF8.GetString(data, headerStart, headerEnd - headerStart);
            var contentStart = headerEnd + 4;

            var nextBoundary = IndexOf(data, boundaryBytes, contentStart);
            if (nextBoundary < 0) break;

            var contentEnd = nextBoundary - 2;
            if (contentEnd < contentStart) { pos = nextBoundary; continue; }

            var fileName = ExtractFileNameFromHeader(headerText);
            if (fileName is not null)
            {
                var destPath = Path.Combine(_shareDir, fileName.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                using var fs = File.Create(destPath);
                fs.Write(data, contentStart, contentEnd - contentStart);
            }

            pos = nextBoundary;
        }
    }

    private static string? ExtractFileNameFromHeader(string header)
    {
        foreach (var line in header.Split("\r\n"))
        {
            if (!line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(';');
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    var name = trimmed["filename=".Length..].Trim('"');
                    return Uri.UnescapeDataString(name);
                }
            }
        }
        return null;
    }

    private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
    {
        for (var i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            var found = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j]) { found = false; break; }
            }
            if (found) return i;
        }
        return -1;
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".json" => "application/json",
            ".xml" => "text/xml",
            ".js" => "text/javascript",
            ".doc" or ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" or ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" or ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".exe" => "application/octet-stream",
            ".msi" => "application/octet-stream",
            ".iso" => "application/x-iso9660-image",
            _ => "application/octet-stream"
        };
    }
}

public class SharedFileInfo
{
    public string RelativePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = "";
}

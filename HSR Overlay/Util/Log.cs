using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Util;

internal static class Log
{
    private static readonly object _gate = new();
    private static StreamWriter? _writer;
    private static LogLevel _minLevel = LogLevel.Information;
    private static string _basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private static string _fileName = "overlay.log";
    private static long _maxBytes = 1_000_000; // ~1MB
    private static int _maxFiles = 5;
    private static string _source = "app";
    private static readonly string _proc = $"{Environment.ProcessId}";

    public static void Init(string? source = null, LogLevel? min = null,
                            string? dir = null, string? name = null,
                            long? maxBytes = null, int? maxFiles = null)
    {
        lock (_gate)
        {
            if (dir is not null) _basePath = dir;
            if (name is not null) _fileName = name;
            if (min is not null) _minLevel = min.Value;
            if (maxBytes is not null) _maxBytes = Math.Max(100_000, maxBytes.Value);
            if (maxFiles is not null) _maxFiles = Math.Max(1, maxFiles.Value);
            if (!string.IsNullOrWhiteSpace(source)) _source = source!;

            Directory.CreateDirectory(_basePath);
            _writer?.Dispose();
            _writer = new StreamWriter(CurrentPath, append: true, Encoding.UTF8) { AutoFlush = true };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Error("UnhandledException", e.ExceptionObject?.ToString() ?? "(null)");
        }
        Info("Log.Init", $"level={_minLevel}, dir={_basePath}, file={_fileName}, max={_maxBytes}, keep={_maxFiles}");
    }

    private static string CurrentPath => Path.Combine(_basePath, _fileName);
    private static FileStream OpenCurrentPath() =>
        new(CurrentPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(CurrentPath);
            if (!fi.Exists || fi.Length < _maxBytes) return;

            _writer?.Flush();
            _writer?.Dispose();

            // rotate: overlay.log -> overlay.1.log ... up to maxFiles
            for (int i = _maxFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(_basePath, i == 1 ? _fileName : $"{Path.GetFileNameWithoutExtension(_fileName)}.{i}.log");
                var dst = Path.Combine(_basePath, $"{Path.GetFileNameWithoutExtension(_fileName)}.{i + 1}.log");
                if (File.Exists(src)) { TryMove(src, dst); }
            }
            var first = Path.Combine(_basePath, $"{Path.GetFileNameWithoutExtension(_fileName)}.1.log");
            TryMove(CurrentPath, first);

            _writer = new StreamWriter(CurrentPath, append: false, Encoding.UTF8) { AutoFlush = true };
        }
        catch {  }
    }
    private static void TryMove(string src, string dst)
    {
        try { if (File.Exists(dst)) File.Delete(dst); File.Move(src, dst); } catch { }
    }

    private static void Write(LogLevel level, string tag, string message)
    {
        if (level < _minLevel) return;
        var ts = DateTimeOffset.Now;
        var line = $"{ts:yyyy-MM-dd HH:mm:ss.fff zzz}\t{_proc}\t{_source}\t{level}\t{tag}\t{message}";
        lock (_gate)
        {
            _writer ??= new StreamWriter(CurrentPath, append: true, Encoding.UTF8) { AutoFlush = true };
            _writer.WriteLine(line);
            RotateIfNeeded();
        }
        // also mirror to Debug output for live dev inspection
        System.Diagnostics.Debug.WriteLine(line);
    }

    public static void Trace(string tag, string msg) => Write(LogLevel.Trace, tag, msg);
    public static void Debug(string tag, string msg) => Write(LogLevel.Debug, tag, msg);
    public static void Info(string tag, string msg) => Write(LogLevel.Information, tag, msg);
    public static void Warn(string tag, string msg) => Write(LogLevel.Warning, tag, msg);
    public static void Error(string tag, string msg) => Write(LogLevel.Error, tag, msg);
}
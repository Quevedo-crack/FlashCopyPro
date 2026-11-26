using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Security.Cryptography; using System.Text; using System.Threading; using System.Threading.Tasks;
namespace FlashCopy {
public enum Mode { Copy, Move, Test, Delete }
public enum FState { Pending, Working, OK, Verified, Failed, Mismatch, Skipped }
public enum Conflict { Ask, Overwrite, Skip, Older, Size, Rename, RenameOld }
public class FItem { public string Src, Rel, Dst, SrcHash, DstHash, Error; public long Size; public bool IsDir; public DateTime MTime; public FState State; public long PartialBytes; }
public class Opts {
  public Conflict Conflict = Conflict.Ask; public string Hash = "CRC32"; public int Buffer = 4 << 20;
  public bool Verify = true, Data = true, Times = true, Attrs = true, Acl, Unattended, SysCache = true, FreeSpace = true, SaveChecksum, ExportHtml, Recycle = true;
  public Opts Clone() => (Opts)MemberwiseClone();
}
public class Job {
  static int seq; public int Id = ++seq;
  public List<string> Sources = new List<string>(); public List<FItem> Files = new List<FItem>();
  public string Target; public Mode Mode; public bool Running, Done, Stopped, Started; public Opts O = new Opts();
  public long Total, DoneBytes, CurDone, LastBytes; public FItem Cur; public int Processed; public string Phase = "";
  public List<double> Speed = new List<double>(); public double LastSpeed; public StringBuilder Log = new StringBuilder();
  public DateTime Start, End; public ManualResetEventSlim Gate = new ManualResetEventSlim(true);
  public volatile bool Cancel, SkipCur; public int CurIndex = -1; public HashSet<string> Made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  public bool Paused => !Gate.IsSet;
  public void L(string s) { lock (Log) Log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}"); }
  public int Count(params FState[] s) => Files.Count(f => !f.IsDir && s.Contains(f.State));
  public string ModeName => Mode == Mode.Move ? "Moviendo" : Mode == Mode.Test ? "Probando" : Mode == Mode.Delete ? "Eliminando" : "Copiando";
  public string Title {
    get {
      if (Files.Count == 0) return "Sin archivos añadidos";
      if (Running) return Paused ? $"En pausa ({Pct:0.0} %)" : $"{ModeName} ({Pct:0.0} %)";
      if (Done) return (Mode == Mode.Move ? "Mover" : Mode == Mode.Test ? "Prueba" : Mode == Mode.Delete ? "Eliminar" : "Copia") + (Stopped ? " - Detenido" : Count(FState.Failed, FState.Mismatch) > 0 ? " - Con errores" : " - Hecho");
      return "Listo para copiar";
    }
  }
  public double Pct => Total > 0 ? Math.Min(100, DoneBytes * 100.0 / Total) : (Done ? 100 : 0);
}
public class Crc32 : HashAlgorithm {
  static readonly uint[][] T = Make();
  static uint[][] Make() { var t = new uint[8][]; for (int k = 0; k < 8; k++) t[k] = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[0][i] = c; } for (int k = 1; k < 8; k++) for (int i = 0; i < 256; i++) t[k][i] = (t[k - 1][i] >> 8) ^ t[0][t[k - 1][i] & 0xFF]; return t; }
  uint h = 0xFFFFFFFF; public override void Initialize() { h = 0xFFFFFFFF; }
  protected override void HashCore(byte[] a, int o, int n) {
    uint c = h; int i = o, end = o + n; uint[] t0 = T[0], t1 = T[1], t2 = T[2], t3 = T[3], t4 = T[4], t5 = T[5], t6 = T[6], t7 = T[7];
    while (end - i >= 8) {
      c ^= (uint)(a[i] | a[i + 1] << 8 | a[i + 2] << 16 | a[i + 3] << 24); uint hi = (uint)(a[i + 4] | a[i + 5] << 8 | a[i + 6] << 16 | a[i + 7] << 24);
      c = t7[c & 0xFF] ^ t6[(c >> 8) & 0xFF] ^ t5[(c >> 16) & 0xFF] ^ t4[c >> 24] ^ t3[hi & 0xFF] ^ t2[(hi >> 8) & 0xFF] ^ t1[(hi >> 16) & 0xFF] ^ t0[hi >> 24]; i += 8;
    }
    for (; i < end; i++) c = t0[(c ^ a[i]) & 0xFF] ^ (c >> 8); h = c;
  }
  protected override byte[] HashFinal() { var v = ~h; return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }
  public override int HashSize => 32;
}
public static class Engine {
  public static readonly string[] Algos = { "CRC32", "MD5", "SHA1", "SHA256", "SHA512" };
  public static HashAlgorithm MakeHash(string n) => n == "CRC32" ? new Crc32() : n == "MD5" ? MD5.Create() : n == "SHA1" ? (HashAlgorithm)SHA1.Create() : n == "SHA512" ? SHA512.Create() : SHA256.Create();
  static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("X2")));
  public static string Short(string h) => string.IsNullOrEmpty(h) ? "" : h.Length <= 8 ? h.Insert(4, ":") : h.Substring(0, 4) + ":" + h.Substring(4, 4);
  public static string Fmt(double b) { if (b < 1024) return $"{b:0} B"; string[] u = { "KB", "MB", "GB", "TB" }; int i = -1; do { b /= 1024; i++; } while (b >= 1024 && i < 3); return $"{b:0.0} {u[i]}"; }

  public static void AddSources(Job j, IEnumerable<string> paths) {
    foreach (var p0 in paths) {
      var p = p0.TrimEnd('\\'); if (p.Length == 2 && p[1] == ':') p += "\\";
      if (j.Sources.Contains(p)) continue;
      try {
        if (Directory.Exists(p)) {
          j.Sources.Add(p); var baseDir = Path.GetDirectoryName(p) ?? p; Walk(j, p, baseDir);
        } else if (File.Exists(p)) {
          j.Sources.Add(p); var fi = new FileInfo(p);
          j.Files.Add(new FItem { Src = p, Rel = fi.Name, Size = fi.Length, MTime = fi.LastWriteTimeUtc }); j.Total += fi.Length;
        }
      } catch (Exception e) { j.L("Error al leer " + p + ": " + e.Message); }
    }
  }
  static void Walk(Job j, string dir, string baseDir) {
    string rel(string x) => baseDir.Length == 0 || x.Length <= baseDir.Length ? Path.GetFileName(x) : x.Substring(baseDir.Length).TrimStart('\\');
    j.Files.Add(new FItem { Src = dir, Rel = rel(dir) == "" ? new DirectoryInfo(dir).Name.TrimEnd(':', '\\') : rel(dir), IsDir = true });
    string[] files = new string[0], dirs = new string[0];
    try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); } catch (Exception e) { j.L("Sin acceso: " + dir + " - " + e.Message); }
    foreach (var f in files) { try { var fi = new FileInfo(f); j.Files.Add(new FItem { Src = f, Rel = rel(f), Size = fi.Length, MTime = fi.LastWriteTimeUtc }); j.Total += fi.Length; } catch { } }
    foreach (var d in dirs) { try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; } Walk(j, d, baseDir); }
  }

  public static Func<Job, FItem, Tuple<Conflict, bool>> Ask;

  public static void Run(Job j, bool resume = false) {
    j.Running = true; j.Started = true; j.Done = false; j.Stopped = false; j.Cancel = false; j.Start = DateTime.Now; j.DoneBytes = 0; j.LastBytes = 0; j.Processed = 0; j.Speed.Clear(); j.Made.Clear();
    lock (j.Files) foreach (var f in j.Files) {
      if (resume && (f.State == FState.OK || f.State == FState.Verified || f.State == FState.Skipped)) { j.DoneBytes += f.Size; continue; }
      if (!resume) f.PartialBytes = 0; f.State = FState.Pending; f.Error = null; f.DstHash = null; if (j.Mode != Mode.Test) f.SrcHash = null;
    }
    if (resume) j.L("Reanudando: se saltan los archivos ya terminados");
    var policy = j.O.Conflict; if (j.O.Unattended && policy == Conflict.Ask) policy = Conflict.Skip;
    j.L($"{j.ModeName} {j.Files.Count(x => !x.IsDir)} archivos ({Fmt(j.Total)}) → {j.Target ?? "(sin destino)"} · hash {j.O.Hash} · búfer {Fmt(j.O.Buffer)}");
    try {
      if ((j.Mode == Mode.Copy || j.Mode == Mode.Move) && j.O.FreeSpace) {
        var root = Path.GetPathRoot(j.Target); var di = new DriveInfo(root);
        bool sameVol = j.Mode == Mode.Move && j.Sources.All(s => string.Equals(Path.GetPathRoot(s), root, StringComparison.OrdinalIgnoreCase));
        if (!sameVol && di.AvailableFreeSpace < j.Total) { j.L($"Espacio insuficiente: necesitas {Fmt(j.Total)}, libres {Fmt(di.AvailableFreeSpace)}"); j.Stopped = true; throw new OperationCanceledException(); }
      }
      for (int idx = 0; ; idx++) {
        FItem f; lock (j.Files) { if (idx >= j.Files.Count) break; f = j.Files[idx]; j.CurIndex = idx; }
        if (f.State != FState.Pending) { j.Processed++; continue; }
        if (j.Cancel) break; j.Gate.Wait(); if (j.Cancel) break; j.Cur = f; j.CurDone = 0; j.SkipCur = false; f.State = FState.Working;
        try {
          switch (j.Mode) {
            case Mode.Test: if (!f.IsDir) { j.Phase = "Probando"; f.SrcHash = HashFile(j, f.Src, true); f.State = j.SkipCur ? FState.Skipped : FState.OK; } else f.State = FState.OK; break;
            case Mode.Delete: if (!f.IsDir) { Del(j, f.Src); j.DoneBytes += f.Size; } f.State = FState.OK; break;
            default: CopyItem(j, f, ref policy); break;
          }
        } catch (OperationCanceledException) { if (f.State == FState.Working) f.State = FState.Pending; break; }
        catch (Exception e) { f.State = FState.Failed; f.Error = e.Message; j.L("Fallo: " + f.Src + " - " + e.Message); }
        if (f.State == FState.Mismatch) j.L("Hashes no coinciden: " + f.Src);
        j.Processed++;
      }
      if (j.Mode == Mode.Move || j.Mode == Mode.Delete)
        foreach (var d in j.Files.Where(x => x.IsDir).Reverse()) { try { if (Directory.Exists(d.Src) && !Directory.EnumerateFileSystemEntries(d.Src).Any()) { if (j.Mode == Mode.Delete) Del(j, d.Src, true); else Directory.Delete(d.Src); } } catch { } }
      if (j.O.SaveChecksum && !j.Cancel) SaveChecksum(j);
    } catch (OperationCanceledException) { }
    j.Stopped |= j.Cancel; j.Cur = null; j.CurIndex = -1; j.Running = false; j.Done = true; j.End = DateTime.Now; j.Phase = "";
    j.L(j.Stopped ? "Detenido" : $"Terminado en {(j.End - j.Start):hh\\:mm\\:ss} · OK {j.Count(FState.OK, FState.Verified)} · fallidos {j.Count(FState.Failed, FState.Mismatch)}");
    if (j.O.ExportHtml) try { ExportHtml(j, null); } catch { }
  }

  static void Del(Job j, string p, bool dir = false) {
    if (j.O.Recycle) { if (dir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); }
    else { if (dir) Directory.Delete(p); else { File.SetAttributes(p, FileAttributes.Normal); File.Delete(p); } }
  }

  static string Unique(string p) { var d = Path.GetDirectoryName(p); var n = Path.GetFileNameWithoutExtension(p); var e = Path.GetExtension(p); int i = 2; string r; do r = Path.Combine(d, $"{n} ({i++}){e}"); while (File.Exists(r)); return r; }

  static void CopyItem(Job j, FItem f, ref Conflict policy) {
    var dst = Path.Combine(j.Target, f.Rel); f.Dst = dst;
    if (f.IsDir) { Directory.CreateDirectory(dst); if (j.O.Times) try { Directory.SetLastWriteTimeUtc(dst, Directory.GetLastWriteTimeUtc(f.Src)); } catch { } f.State = FState.OK; return; }
    if (string.Equals(Path.GetFullPath(f.Src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) { dst = Unique(dst); f.Dst = dst; }
    var dd = Path.GetDirectoryName(dst); if (j.Made.Add(dd)) Directory.CreateDirectory(dd);
    if (f.PartialBytes == 0 && File.Exists(dst)) {
      var c = policy;
      if (c == Conflict.Ask) { var r = Ask(j, f); c = r.Item1; if (r.Item2) policy = c; }
      var di = new FileInfo(dst);
      if (c == Conflict.Skip || (c == Conflict.Older && di.LastWriteTimeUtc >= f.MTime) || (c == Conflict.Size && di.Length == f.Size)) { f.State = FState.Skipped; j.DoneBytes += f.Size; return; }
      if (c == Conflict.Rename) { dst = Unique(dst); f.Dst = dst; }
      else if (c == Conflict.RenameOld) File.Move(dst, Unique(dst));
      else File.SetAttributes(dst, FileAttributes.Normal);
    }
    if (j.Mode == Mode.Move && f.PartialBytes == 0 && string.Equals(Path.GetPathRoot(f.Src), Path.GetPathRoot(dst), StringComparison.OrdinalIgnoreCase)) {
      if (File.Exists(dst)) File.Delete(dst); File.Move(f.Src, dst); j.DoneBytes += f.Size; f.State = FState.OK; return;
    }
    j.Phase = j.ModeName;
    f.SrcHash = CopyData(j, f, dst);
    if (f.SrcHash == null) { f.State = FState.Skipped; j.L("Saltado: " + f.Src); return; }
    if (j.O.Times) { File.SetCreationTimeUtc(dst, File.GetCreationTimeUtc(f.Src)); File.SetLastWriteTimeUtc(dst, f.MTime); }
    if (j.O.Acl) try { File.SetAccessControl(dst, File.GetAccessControl(f.Src)); } catch (Exception e) { j.L("ACL: " + e.Message); }
    if (j.O.Attrs) File.SetAttributes(dst, File.GetAttributes(f.Src));
    f.State = FState.OK;
    if (j.O.Verify) {
      j.Phase = "Verificando"; f.DstHash = HashFile(j, dst, false);
      f.State = f.DstHash == f.SrcHash ? FState.Verified : FState.Mismatch;
    }
    if (j.Mode == Mode.Move && f.State != FState.Mismatch) { File.SetAttributes(f.Src, FileAttributes.Normal); File.Delete(f.Src); }
  }

  static string CopyData(Job j, FItem f, string dst) {
    int bs = j.O.Buffer; bool aborted = false; string hash; long written = 0;
    using (var h = MakeHash(j.O.Hash)) {
      var wo = j.O.SysCache ? FileOptions.SequentialScan : FileOptions.WriteThrough;
      using (var src = new FileStream(f.Src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
      using (var ds = new FileStream(dst, f.PartialBytes > 0 ? FileMode.OpenOrCreate : FileMode.Create, FileAccess.Write, FileShare.None, 1, wo)) {
        var b = new[] { new byte[bs], new byte[bs] }; int i = 0; Task w = Task.FromResult(0); int n;
        // Reanudar un archivo a medias: rehash del tramo ya copiado y continuar desde ahí
        long start = Math.Min(f.PartialBytes, Math.Min(ds.Length, src.Length)); start -= start % bs;
        if (start > 0) {
          j.L($"Reanudando {f.Rel} desde {Fmt(start)}");
          while (src.Position < start && (n = src.Read(b[0], 0, (int)Math.Min(bs, start - src.Position))) > 0) { if (j.Cancel) throw new OperationCanceledException(); h.TransformBlock(b[0], 0, n, null, 0); j.DoneBytes += n; j.CurDone += n; }
          ds.Position = start; written = start;
        } else if (f.Size > 0) ds.SetLength(f.Size);
        f.PartialBytes = 0;
        while ((n = src.Read(b[i], 0, bs)) > 0) {
          if (j.Cancel || j.SkipCur) { aborted = true; break; }
          j.Gate.Wait();
          w.Wait(); if (w != null) written = ds.Position;
          var buf = b[i]; var len = n;
          // hash y escritura en paralelo mientras se lee el siguiente bloque
          w = Task.WhenAll(Task.Run(() => h.TransformBlock(buf, 0, len, null, 0)), ds.WriteAsync(buf, 0, len));
          j.DoneBytes += n; j.CurDone += n; i ^= 1;
        }
        w.Wait(); written = ds.Position;
        if (!aborted) ds.SetLength(src.Length);
        h.TransformFinalBlock(new byte[0], 0, 0); hash = Hex(h.Hash);
      }
    }
    if (aborted) {
      if (j.Cancel) { f.PartialBytes = written; f.State = FState.Pending; j.DoneBytes -= j.CurDone; throw new OperationCanceledException(); }
      try { File.Delete(dst); } catch { } j.DoneBytes += f.Size - j.CurDone; return null;
    }
    return hash;
  }

  public static string HashFile(Job j, string p, bool count) {
    using (var h = MakeHash(j.O.Hash))
    using (var s = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan)) {
      var b = new byte[j.O.Buffer]; int n; j.CurDone = 0;
      while ((n = s.Read(b, 0, b.Length)) > 0) {
        if (j.Cancel) throw new OperationCanceledException(); if (j.SkipCur) { if (count) j.DoneBytes += s.Length - s.Position; break; }
        j.Gate.Wait(); h.TransformBlock(b, 0, n, null, 0); j.CurDone += n; if (count) j.DoneBytes += n;
      }
      h.TransformFinalBlock(new byte[0], 0, 0); return Hex(h.Hash);
    }
  }

  public static string SaveChecksum(Job j) {
    var dir = j.Target ?? Path.GetDirectoryName(j.Sources.First());
    var p = Path.Combine(dir, $"FlashCopy_{DateTime.Now:yyyyMMdd_HHmmss}.{j.O.Hash.ToLower()}");
    File.WriteAllLines(p, j.Files.Where(f => !f.IsDir && f.SrcHash != null).Select(f => $"{f.SrcHash.ToLower()} *{f.Rel}"));
    j.L("Checksums guardados en " + p); return p;
  }

  public static string ExportHtml(Job j, string path) {
    path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"FlashCopy_informe_{DateTime.Now:yyyyMMdd_HHmmss}.html");
    var sb = new StringBuilder("<!doctype html><meta charset=utf-8><title>Informe FlashCopy</title><style>body{font:13px Segoe UI;background:#202020;color:#ddd}td,th{padding:4px 10px;text-align:left}tr:nth-child(even){background:#2a2a2a}.bad{color:#f66}.ok{color:#6d6}</style>");
    sb.Append($"<h2>{j.Title}</h2><p>Origen: {string.Join(", ", j.Sources)}<br>Destino: {j.Target}<br>Inicio: {j.Start} · Fin: {j.End} · Hash: {j.O.Hash}</p><table><tr><th>Estado</th><th>Archivo</th><th>Tamaño</th><th>Hash origen</th><th>Hash destino</th></tr>");
    foreach (var f in j.Files.Where(x => !x.IsDir)) sb.Append($"<tr><td class={(f.State == FState.Failed || f.State == FState.Mismatch ? "bad" : "ok")}>{f.State}</td><td>{System.Net.WebUtility.HtmlEncode(f.Src)}</td><td>{Fmt(f.Size)}</td><td>{f.SrcHash}</td><td>{f.DstHash}</td></tr>");
    File.WriteAllText(path, sb.Append("</table>").ToString()); j.L("Informe guardado en " + path); return path;
  }
}
}

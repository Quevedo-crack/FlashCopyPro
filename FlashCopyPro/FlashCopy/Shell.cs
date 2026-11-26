using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Runtime.InteropServices; using System.Windows.Forms; using Microsoft.Win32;
namespace FlashCopy {
public static class Settings {
  static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlashCopy");
  static readonly string FileP = Path.Combine(Dir, "settings.ini"); static Dictionary<string, string> d;
  public static string HistoryFile => Path.Combine(Dir, "historial.txt");
  static void Load() { if (d != null) return; d = new Dictionary<string, string>(); try { foreach (var l in File.ReadAllLines(FileP)) { var i = l.IndexOf('='); if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1); } } catch { } }
  public static string Get(string k, string def = "") { Load(); return d.TryGetValue(k, out var v) ? v : def; }
  public static bool B(string k, bool def) => Get(k, def ? "1" : "0") == "1";
  public static void Set(string k, string v) { Load(); d[k] = v; try { Directory.CreateDirectory(Dir); File.WriteAllLines(FileP, d.Select(x => x.Key + "=" + x.Value)); } catch { } }
  public static void SetB(string k, bool v) => Set(k, v ? "1" : "0");
  public static List<string> Recent { get => Get("recent").Split('|').Where(x => x.Length > 0).ToList(); set => Set("recent", string.Join("|", value.Take(12))); }
  public static void AddRecent(string p) { var r = Recent; r.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)); r.Insert(0, p); Recent = r; }
  public static void History(string line) { try { Directory.CreateDirectory(Dir); File.AppendAllText(HistoryFile, line + Environment.NewLine); } catch { } }
}
public static class Shell {
  static string Exe => System.Reflection.Assembly.GetExecutingAssembly().Location;
  static void Verb(string bas, string key, string text, string cmd, bool multi = true) {
    using (var k = Registry.CurrentUser.CreateSubKey(bas + "\\" + key)) { k.SetValue("", text); k.SetValue("MUIVerb", text); k.SetValue("Icon", Exe); if (multi) k.SetValue("MultiSelectModel", "Player"); using (var c = k.CreateSubKey("command")) c.SetValue("", cmd); }
  }
  static readonly string[] Bases = { @"Software\Classes\*\shell", @"Software\Classes\Directory\shell", @"Software\Classes\Drive\shell" };
  public static void ContextMenu(bool on) {
    foreach (var b in Bases) foreach (var v in new[] { "FlashCopy.1Copy", "FlashCopy.2Move", "FlashCopy.3Test", "FlashCopy.Paste" }) try { Registry.CurrentUser.DeleteSubKeyTree(b + "\\" + v, false); } catch { }
    try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shell\FlashCopy.Paste", false); } catch { }
    if (!on) { Notify(); return; }
    foreach (var b in Bases.Take(2)) {
      Verb(b, "FlashCopy.1Copy", "Copiar con FlashCopy…", $"\"{Exe}\" /copy \"%1\"");
      Verb(b, "FlashCopy.2Move", "Mover con FlashCopy…", $"\"{Exe}\" /move \"%1\"");
      Verb(b, "FlashCopy.3Test", "Comprobar con FlashCopy", $"\"{Exe}\" /test \"%1\"");
    }
    Verb(@"Software\Classes\Directory\Background\shell", "FlashCopy.Paste", "Pegar con FlashCopy", $"\"{Exe}\" /paste \"%V\"", false);
    Verb(@"Software\Classes\Directory\shell", "FlashCopy.Paste", "Pegar aquí con FlashCopy", $"\"{Exe}\" /paste \"%1\"", false);
    Verb(@"Software\Classes\Drive\shell", "FlashCopy.Paste", "Pegar con FlashCopy", $"\"{Exe}\" /paste \"%1\"", false);
    Notify();
  }
  public static bool ContextMenuOn => Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\FlashCopy.1Copy") != null;
  public static void Handler(bool on) {
    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (on) k.SetValue("FlashCopy", $"\"{Exe}\" /tray"); else k.DeleteValue("FlashCopy", false); }
  }
  const string Classic = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
  public static bool ClassicOn => Registry.CurrentUser.OpenSubKey(Classic + "\\InprocServer32") != null;
  public static void ClassicMenu(bool on) { if (on) using (var k = Registry.CurrentUser.CreateSubKey(Classic + "\\InprocServer32")) k.SetValue("", ""); else try { Registry.CurrentUser.DeleteSubKeyTree(Classic, false); } catch { } }
  [DllImport("shell32.dll")] static extern void SHChangeNotify(int e, int f, IntPtr a, IntPtr b);
  static void Notify() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static string ForegroundFolder() => FolderOf(GetForegroundWindow());
  public static string FolderOf(IntPtr fg) {
    var sb = new System.Text.StringBuilder(64); GetClassName(fg, sb, 64);
    var cls = sb.ToString(); if (cls == "Progman" || cls == "WorkerW") return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    var tb = new System.Text.StringBuilder(512); GetWindowText(fg, tb, 512); var title = tb.ToString();
    string first = null;
    try {
      dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
      foreach (dynamic w in sh.Windows()) { try {
        if (new IntPtr((long)w.HWND) != fg) continue; string p = w.Document.Folder.Self.Path; if (!Directory.Exists(p)) continue;
        // Windows 11 con pestañas: varias pestañas comparten ventana; la activa es la que coincide con el título
        string name = w.LocationName; if (first == null) first = p;
        if (!string.IsNullOrEmpty(name) && title.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return p;
      } catch { } }
    } catch { }
    return first;
  }
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
  public static void Dark(IntPtr h) { int one = 1; DwmSetWindowAttribute(h, 20, ref one, 4); int cap = 0x202020; DwmSetWindowAttribute(h, 35, ref cap, 4); int c = 2; DwmSetWindowAttribute(h, 33, ref c, 4); }
  [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string a, string b);
}
}

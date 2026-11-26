using System; using System.IO; using System.IO.Pipes; using System.Text; using System.Threading; using System.Windows.Forms;
namespace FlashCopy {
static class Program {
  [STAThread] static void Main(string[] args) {
    if (args.Length > 0) {
      var a = args[0].ToLower();
      if (a == "/register") { Shell.ContextMenu(true); return; }
      if (a == "/unregister") { Shell.ContextMenu(false); Shell.Handler(false); Shell.ClassicMenu(false); return; }
      if (a == "/handler-on") { Settings.Set("handler", "1"); Settings.Set("replace", "1"); Shell.Handler(true); return; }
    }
    Application.ThreadException += (o, e) => Crash(e.Exception);
    AppDomain.CurrentDomain.UnhandledException += (o, e) => Crash(e.ExceptionObject as Exception);
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    try { Run(args); } catch (Exception ex) { Crash(ex); }
  }
  static void Crash(Exception ex) {
    var t = "FlashCopy Pro ha encontrado un error:\n\n" + ex;
    try { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlashCopy"); Directory.CreateDirectory(d); File.AppendAllText(Path.Combine(d, "error.txt"), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
    MessageBox.Show(t, "FlashCopy Pro", MessageBoxButtons.OK, MessageBoxIcon.Error);
  }
  static void Run(string[] args) {
    bool created; var m = new Mutex(true, "FlashCopyPro.SingleInstance", out created);
    if (!created) { if (!Send(args, 40)) { foreach (var p in System.Diagnostics.Process.GetProcessesByName("FlashCopy")) if (p.Id != System.Diagnostics.Process.GetCurrentProcess().Id && !p.Responding) try { p.Kill(); p.WaitForExit(2000); } catch { } m.Dispose(); m = new Mutex(true, "FlashCopyPro.SingleInstance", out created); } else return; }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    var f = new MainForm(args);
    Listen(a => { try { f.BeginInvoke(new Action(() => f.ReceiveArgs(a))); } catch { } });
    Application.Run(f); GC.KeepAlive(m);
  }
  static bool Send(string[] args, int tries) {
    for (int t = 0; t < tries; t++) try {
      using (var c = new NamedPipeClientStream(".", "FlashCopyPro.Pipe", PipeDirection.Out)) { c.Connect(1000); var d = Encoding.UTF8.GetBytes(string.Join("\n", args)); c.Write(d, 0, d.Length); return true; }
    } catch { Thread.Sleep(250); }
    return false;
  }
  static void Listen(Action<string[]> cb) {
    new Thread(() => { while (true) try {
      var s = new NamedPipeServerStream("FlashCopyPro.Pipe", PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances); s.WaitForConnection();
      ThreadPool.QueueUserWorkItem(_ => { try { using (s) using (var r = new StreamReader(s, Encoding.UTF8)) cb(r.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)); } catch { } });
    } catch { Thread.Sleep(200); } }) { IsBackground = true }.Start();
  }
}
}

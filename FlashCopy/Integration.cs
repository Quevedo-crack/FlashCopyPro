using System; using System.Drawing; using System.Drawing.Text; using System.IO; using System.Runtime.InteropServices; using System.Text; using System.Windows.Forms;
namespace FlashCopy {
// Barra de progreso y porcentaje sobre el icono de la barra de tareas (ITaskbarList3)
public static class Taskbar {
  [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface ITaskbarList3 {
    void HrInit(); void AddTab(IntPtr h); void DeleteTab(IntPtr h); void ActivateTab(IntPtr h); void SetActiveAlt(IntPtr h);
    void MarkFullscreenWindow(IntPtr h, [MarshalAs(UnmanagedType.Bool)] bool f);
    void SetProgressValue(IntPtr h, ulong done, ulong total); void SetProgressState(IntPtr h, int state);
    void RegisterTab(IntPtr a, IntPtr b); void UnregisterTab(IntPtr a); void SetTabOrder(IntPtr a, IntPtr b); void SetTabActive(IntPtr a, IntPtr b, uint r);
    void ThumbBarAddButtons(IntPtr h, uint c, IntPtr b); void ThumbBarUpdateButtons(IntPtr h, uint c, IntPtr b); void ThumbBarSetImageList(IntPtr h, IntPtr l);
    void SetOverlayIcon(IntPtr h, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string desc);
  }
  [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")] class TaskbarRCW { }
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
  static ITaskbarList3 tb; static int lastPct = -2; static int lastState = -1; static IntPtr lastIcon;
  public const int None = 0, Normal = 2, Error = 4, Paused = 8;
  public static void Set(IntPtr h, int state, double pct) {
    try {
      if (tb == null) { tb = (ITaskbarList3)new TaskbarRCW(); tb.HrInit(); }
      int p = (int)pct;
      if (state != lastState) { tb.SetProgressState(h, state); lastState = state; }
      if (state == None) { if (lastPct != -1) { tb.SetOverlayIcon(h, IntPtr.Zero, null); Free(); lastPct = -1; } return; }
      tb.SetProgressValue(h, (ulong)(pct * 10), 1000);
      if (p != lastPct) { var ic = MakeIcon(p, 16, state == Paused ? Color.FromArgb(0xE0, 0xA0, 0x20) : state == Error ? Color.FromArgb(0xD0, 0x40, 0x40) : Color.FromArgb(0x2E, 0x8B, 0x57)); tb.SetOverlayIcon(h, ic, p + " %"); Free(); lastIcon = ic; lastPct = p; }
    } catch { }
  }
  static void Free() { if (lastIcon != IntPtr.Zero) { DestroyIcon(lastIcon); lastIcon = IntPtr.Zero; } }
  // Icono pequeño con el número del porcentaje
  public static IntPtr MakeIcon(int pct, int size, Color bg) {
    using (var bmp = new Bitmap(size, size)) using (var g = Graphics.FromImage(bmp)) {
      g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.Clear(Color.Transparent);
      using (var b = new SolidBrush(bg)) g.FillRectangle(b, 0, 0, size, size);
      var txt = pct >= 100 ? "OK" : pct.ToString();
      using (var f = new Font("Segoe UI", txt.Length > 1 ? size * 0.5f : size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
      using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        g.DrawString(txt, f, Brushes.White, new RectangleF(-1, 0, size + 2, size), sf);
      return bmp.GetHicon();
    }
  }
}

// Sustituye Ctrl+V del Explorador: si hay archivos copiados/cortados, pega con FlashCopy
public static class PasteHook {
  delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr mod, uint tid);
  [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern short GetAsyncKeyState(int k);
  [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint f);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO i);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public RECT rc; }
  static IntPtr hook; static Proc proc; static bool passNext; static Action<IntPtr> onPaste;
  public static bool On => hook != IntPtr.Zero;
  public static void Enable(Action<IntPtr> paste) { if (hook != IntPtr.Zero) return; onPaste = paste; proc = Cb; hook = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); }
  public static void Disable() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
  static string Cls(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }
  static IntPtr Cb(int code, IntPtr w, IntPtr l) {
    try {
      if (code >= 0 && ((int)w == 0x100 || (int)w == 0x104) && Marshal.ReadInt32(l) == 0x56) {
        if (passNext) { passNext = false; return CallNextHookEx(hook, code, w, l); }
        bool ctrl = GetAsyncKeyState(0x11) < 0, shift = GetAsyncKeyState(0x10) < 0, alt = GetAsyncKeyState(0x12) < 0;
        if (ctrl && !shift && !alt && IsClipboardFormatAvailable(15)) {
          var fg = Shell.GetForegroundWindow(); var c = Cls(fg);
          if (c == "CabinetWClass" || c == "Progman" || c == "WorkerW" || c == "ExploreWClass") {
            var tid = GetWindowThreadProcessId(fg, out _); var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            var fc = GetGUIThreadInfo(tid, ref gi) ? Cls(gi.focus) : "";
            // solo en la vista de archivos (no al renombrar ni en la barra de direcciones)
            if (fc == "DirectUIHWND" || fc == "SysListView32") { onPaste?.Invoke(fg); return (IntPtr)1; }
          }
        }
      }
    } catch { }
    return CallNextHookEx(hook, code, w, l);
  }
  // Si FlashCopy no puede averiguar la carpeta, devuelve la pulsación a Windows
  public static void PassToWindows() { passNext = true; keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero); }
}
}

using System; using System.Drawing; using System.IO; using System.Runtime.InteropServices; using System.Windows.Forms;
namespace FlashCopy {
public class ConflictForm : DarkForm {
  public Conflict Choice = Conflict.Skip; public bool All;
  public ConflictForm(FItem f) {
    Text = "El archivo ya existe"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(T.S(560), T.S(260));
    FileInfo d = null; try { d = new FileInfo(f.Dst); } catch { }
    Controls.Add(new Label { Text = $"{f.Rel}\n\nOrigen:   {Engine.Fmt(f.Size)} · {f.MTime.ToLocalTime():g}\nDestino: {(d != null && d.Exists ? Engine.Fmt(d.Length) + " · " + d.LastWriteTime.ToString("g") : "-")}", Bounds = new Rectangle(T.S(16), T.S(12), T.S(530), T.S(100)), ForeColor = T.Fg });
    var all = new FCheck("Aplicar a todos", true) { Bounds = new Rectangle(T.S(16), T.S(118), T.S(200), T.S(26)) }; Controls.Add(all);
    (string, Conflict)[] opts = { ("Sobrescribir", Conflict.Overwrite), ("Saltar", Conflict.Skip), ("Sobrescribir más antiguos", Conflict.Older), ("Renombrar nuevo", Conflict.Rename), ("Renombrar antiguo", Conflict.RenameOld), ("Si tamaño distinto", Conflict.Size) };
    for (int i = 0; i < opts.Length; i++) { var o = opts[i]; var b = new FButton(o.Item1) { Bounds = new Rectangle(T.S(16) + i % 3 * T.S(180), T.S(156) + i / 3 * T.S(44), T.S(170), T.S(34)) }; b.Click += (s, e) => { Choice = o.Item2; All = all.Checked; DialogResult = DialogResult.OK; }; Controls.Add(b); }
  }
}
public class PrefsForm : DarkForm {
  public PrefsForm() {
    Text = "Preferencias"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(T.S(760), T.S(420));
    int y = T.S(14), x2 = T.S(440);
    FCheck C(string t, bool v, int x, int yy, int w = 380) { var c = new FCheck(t, v) { Bounds = new Rectangle(x, yy, T.S(w), T.S(26)) }; Controls.Add(c); return c; }
    Controls.Add(new Header("Integración con Windows") { Location = new Point(T.S(16), y) });
    var ctx = C("Añadir al menú contextual del Explorador", Shell.ContextMenuOn, T.S(16), y + T.S(30));
    Controls.Add(new Label { Text = "Copiar / Mover / Comprobar / Pegar con FlashCopy al hacer clic derecho.", ForeColor = T.Mut, Bounds = new Rectangle(T.S(42), y + T.S(56), T.S(380), T.S(20)) });
    var handler = C("Gestor de copia: Ctrl+Mayús+V pega con FlashCopy", Settings.B("handler", false), T.S(16), y + T.S(84));
    Controls.Add(new Label { Text = "Se inicia con Windows en la bandeja del sistema.", ForeColor = T.Mut, Bounds = new Rectangle(T.S(42), y + T.S(110), T.S(380), T.S(20)) });
    var classic = C("Mostrar menú clásico completo (sin \"Mostrar más opciones\")", Shell.ClassicOn, T.S(16), y + T.S(138), 420);
    var cdrop = C("Confirmar arrastrar y soltar", Settings.B("confirmdrop", true), T.S(16), y + T.S(172));
    var repl = C("Sustituir el pegado de Windows (Ctrl+V en el Explorador)", Settings.B("replace", true), T.S(16), y + T.S(240), 420);
    Controls.Add(new Label { Text = "Al pegar archivos con Ctrl+V se copian con FlashCopy.", ForeColor = T.Mut, Bounds = new Rectangle(T.S(42), y + T.S(266), T.S(380), T.S(20)) });
    var clip = C("Comprobar archivos contra checksum del portapapeles", Settings.B("clip", true), T.S(16), y + T.S(206));
    Controls.Add(new Header("Ventana y al terminar") { Location = new Point(x2, y) });
    var top = C("Siempre visible", Settings.B("top", false), x2, y + T.S(30), 300); var notify = C("Mostrar notificaciones", Settings.B("notify", false), x2, y + T.S(64), 300);
    var sound = C("Reproducir sonido", Settings.B("sound", false), x2, y + T.S(98), 300); var alw = C("Verificar siempre tras copiar", Settings.B("alwaystest", false), x2, y + T.S(140), 300);
    var free = C("Comprobar espacio libre", Settings.B("freespace", true), x2, y + T.S(174), 300); var cache = C("Usar caché de escritura del sistema", Settings.B("syscache", true), x2, y + T.S(208), 300);
    var ok = new FButton("Aceptar") { Primary = true, Bounds = new Rectangle(T.S(500), T.S(366), T.S(118), T.S(34)) }; var cancel = new FButton("Cancelar") { Bounds = new Rectangle(T.S(628), T.S(366), T.S(118), T.S(34)) };
    cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
    ok.Click += (s, e) => {
      try { Shell.ContextMenu(ctx.Checked); Shell.Handler(handler.Checked); bool wasClassic = Shell.ClassicOn; Shell.ClassicMenu(classic.Checked);
        if (wasClassic != classic.Checked && MessageBox.Show(this, "Hay que reiniciar el Explorador para ver el cambio. ¿Reiniciar ahora?", "FlashCopy", MessageBoxButtons.YesNo) == DialogResult.Yes) { foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer")) p.Kill(); System.Diagnostics.Process.Start("explorer.exe"); } } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
      Settings.SetB("handler", handler.Checked || repl.Checked); Settings.SetB("replace", repl.Checked); if (repl.Checked) Shell.Handler(true); Settings.SetB("confirmdrop", cdrop.Checked); Settings.SetB("clip", clip.Checked); Settings.SetB("top", top.Checked); Settings.SetB("notify", notify.Checked); Settings.SetB("sound", sound.Checked); Settings.SetB("alwaystest", alw.Checked); Settings.SetB("freespace", free.Checked); Settings.SetB("syscache", cache.Checked);
      DialogResult = DialogResult.OK; };
    Controls.Add(ok); Controls.Add(cancel);
  }
}
public class FolderPicker : IDisposable {
  public string Title;
  public string Show(IntPtr owner) {
    var d = (IFileOpenDialog)new FileOpenDialogRCW(); try {
      d.SetOptions(0x20 | 0x40 | 0x800); if (Title != null) d.SetTitle(Title);
      if (d.Show(owner) != 0) return null; d.GetResult(out var item); item.GetDisplayName(0x80058000, out var p); return p;
    } catch { return null; } finally { Marshal.ReleaseComObject(d); }
  }
  public void Dispose() { }
  [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogRCW { }
  [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IFileOpenDialog {
    [PreserveSig] int Show(IntPtr parent); void SetFileTypes(); void SetFileTypeIndex(); void GetFileTypeIndex(); void Advise(); void Unadvise();
    void SetOptions(uint fos); void GetOptions(); void SetDefaultFolder(); void SetFolder(); void GetFolder(); void GetCurrentSelection(); void SetFileName();
    void GetFileName(); void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string t); void SetOkButtonLabel(); void SetFileNameLabel(); void GetResult(out IShellItem i);
  }
  [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellItem { void BindToHandler(); void GetParent(); void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string n); }
}
}

using System; using System.Collections.Generic; using System.Collections.Specialized; using System.Drawing; using System.Drawing.Drawing2D; using System.IO; using System.Linq; using System.Media; using System.Threading; using System.Windows.Forms;
namespace FlashCopy {
public class MainForm : DarkForm {
  readonly List<Job> jobs = new List<Job>(); Job sel; readonly ToolTip tt = new ToolTip();
  Panel toolbar, side, pane, tabHost; Base header, jobList; FButton b1, b2, b3, b4; FCheck cUnatt, cVerify; FTabs tabs; Label finishLbl;
  Control[] pages = new Control[5]; ListView list; Label emptyLbl; FCombo filter; FCheck autoScroll; TextBox logBox; Base chart, summary; ListBox recent;
  NotifyIcon tray; string onFinish = Settings.Get("onfinish", "keep"); bool trayMode, reallyClose;
  readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 }; int tick, trayPct = -1;
  string pendingVerb; readonly List<string> pending = new List<string>(); readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 700 };
  List<FItem> view = new List<FItem>();
  // Opciones (pestaña Opciones)
  FCheck oData, oTimes, oAttrs, oAcl, oHtml, oVerifyAfter, oSaveSum, oRecycle; FCombo oConflict, oHash, oBuf, oFinish;
  static readonly string[] ConflictNames = { "Preguntar", "Sobrescribir todo", "Saltar todo", "Sobrescribir más antiguos", "Sobrescribir si tamaño distinto", "Renombrar todos", "Renombrar los antiguos" };
  static readonly int[] Buffers = { 1, 2, 4, 8, 16, 32, 64 };

  public MainForm(string[] args) {
    using (var g = CreateGraphics()) T.Scale = g.DpiX / 96f;
    Text = "FlashCopy Pro"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(T.S(820), T.S(480));
    Size = new Size(T.S(1180), T.S(700)); AllowDrop = true; KeyPreview = true;
    tray = new NotifyIcon { Icon = Icon, Text = "FlashCopy Pro", Visible = false };
    Engine.Ask = AskConflict; Build(); NewJob();
    tray.Visible = Settings.B("handler", false);
    timer.Tick += (s, e) => Refresh2(); timer.Start(); debounce.Tick += (s, e) => { debounce.Stop(); Flush(); };
    TopMost = Settings.B("top", false);
    var tm = new DarkMenu(); tm.Add("Abrir FlashCopy", ShowMain); tm.Add("Pegar con FlashCopy (Ctrl+Mayús+V)", () => PasteInto(null)); tm.Items.Add(new ToolStripSeparator()); tm.Add("Salir", () => { reallyClose = true; Close(); });
    tray.ContextMenuStrip = tm; tray.DoubleClick += (s, e) => ShowMain();
    if (args.Length > 0 && args[0].ToLower() == "/tray") trayMode = true; else if (args.Length > 0) ReceiveArgs(args);
  }
  protected override void SetVisibleCore(bool v) { if (trayMode && !IsHandleCreated) { CreateHandle(); v = false; } base.SetVisibleCore(v); }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (Settings.B("handler", false)) Shell.RegisterHotKey(Handle, 1, 0x2 | 0x4 | 0x4000, (uint)Keys.V); HookState(); }
  void HookState() {
    if (Settings.B("replace", true)) PasteHook.Enable(fg => BeginInvoke(new Action(() => { var p = Shell.FolderOf(fg); if (p == null) PasteHook.PassToWindows(); else PasteInto(p); })));
    else PasteHook.Disable();
  }
  protected override void WndProc(ref Message m) { if (m.Msg == 0x0312) { var p = Shell.ForegroundFolder(); if (p != null) PasteInto(p); } base.WndProc(ref m); }
  void ShowMain() { trayMode = false; Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); var t = TopMost; TopMost = true; TopMost = t; }
  protected override void OnFormClosing(FormClosingEventArgs e) {
    if (!reallyClose && Settings.B("handler", false) && e.CloseReason == CloseReason.UserClosing && !jobs.Any(j => j.Running)) { e.Cancel = true; Hide(); return; }
    if (jobs.Any(j => j.Running) && e.CloseReason == CloseReason.UserClosing && MessageBox.Show("Hay transferencias en curso. ¿Detenerlas y salir?", "FlashCopy", MessageBoxButtons.YesNo) == DialogResult.No) { e.Cancel = true; reallyClose = false; return; }
    foreach (var j in jobs) { j.Cancel = true; j.Gate.Set(); } PasteHook.Disable(); tray.Visible = false; base.OnFormClosing(e);
  }

  // ---------- construcción de la interfaz ----------
  void Build() {
    SuspendLayout();
    toolbar = new Panel { Dock = DockStyle.Top, Height = T.S(44), BackColor = T.Bg };
    int x = T.S(8); void Tb(string g, string tip, Action a) { var b = new IconBtn(g, tip, tt) { Bounds = new Rectangle(x, T.S(6), T.S(36), T.S(32)) }; b.Click += (s, e) => a(); toolbar.Controls.Add(b); x += T.S(38); }
    void Sep() { toolbar.Controls.Add(new Panel { BackColor = T.Line, Bounds = new Rectangle(x + T.S(4), T.S(12), 1, T.S(20)) }); x += T.S(10); }
    Tb("\uE700", "Mostrar/ocultar panel de tareas", () => side.Visible = !side.Visible); Sep();
    Tb("\uE710", "Nueva tarea", () => NewJob()); Tb("\uE8C8", "Duplicar tarea", DuplicateJob); Tb("\uE81C", "Historial", ShowHistory); Sep();
    Tb("\uE749", "Imprimir / exportar informe", () => { if (sel != null && sel.Files.Count > 0) System.Diagnostics.Process.Start(Engine.ExportHtml(sel, null)); });
    Tb("\uE713", "Preferencias", () => { using (var p = new PrefsForm()) if (p.ShowDialog(this) == DialogResult.OK) ApplyPrefs(); }); Sep();
    Tb("\uE946", "Acerca de", () => MessageBox.Show(this, "FlashCopy Pro 1.0\n\nCopia, mueve, comprueba y elimina archivos con verificación por hash.\nTodas las funciones son gratuitas.\n\nOptimizado para Windows 11 x64 · Ryzen 7 · 24 GB RAM · SSD (búfer 4 MB, doble búfer asíncrono).", "Acerca de FlashCopy Pro"));
    finishLbl = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = T.Mut, Anchor = AnchorStyles.Top | AnchorStyles.Right, Cursor = Cursors.Hand, Bounds = new Rectangle(0, T.S(6), T.S(300), T.S(32)) };
    finishLbl.Click += (s, e) => FinishMenu().Show(finishLbl, new Point(finishLbl.Width - T.S(200), finishLbl.Height)); toolbar.Controls.Add(finishLbl);
    toolbar.Resize += (s, e) => finishLbl.Left = toolbar.Width - finishLbl.Width - T.S(12); UpdateFinishLbl();
    toolbar.Paint += (s, e) => { using (var p = new Pen(T.Line)) e.Graphics.DrawLine(p, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1); };

    side = new Panel { Dock = DockStyle.Left, Width = T.S(300), BackColor = T.Side };
    jobList = new JobListCtl(this) { Dock = DockStyle.Fill }; side.Controls.Add(jobList);
    var splitLine = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = T.Line };

    pane = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg };
    header = new HeaderCtl(this) { BackColor = T.Bg };
    b1 = new FButton("Copiar…"); b2 = new FButton("Mover…"); b3 = new FButton("Probar"); b4 = new FButton("Eliminar…");
    b1.Click += (s, e) => { if (Busy) Pause(); else if (CanResume) Start(sel.Mode, sel.Target); else TargetMenu(Mode.Copy).Show(b1, 0, b1.Height); };
    b2.Click += (s, e) => { if (Busy) { if (sel.Cur != null) sel.SkipCur = true; } else TargetMenu(Mode.Move).Show(b2, 0, b2.Height); };
    b3.Click += (s, e) => { if (Busy) { sel.Cancel = true; sel.Gate.Set(); } else Start(Mode.Test, null); };
    b4.Click += (s, e) => { if (!Busy && sel.Files.Count > 0 && MessageBox.Show(this, $"¿Eliminar {sel.Files.Count(f => !f.IsDir)} archivos? (irán a la Papelera)", "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) Start(Mode.Delete, null); };
    cUnatt = new FCheck("Desatendido"); cVerify = new FCheck("Verificar", Settings.B("verify", true)); cVerify.Changed += (s, e) => Settings.SetB("verify", cVerify.Checked);
    tabs = new FTabs("Lista de archivos", "Destino", "Opciones", "Estado", "Registro"); tabs.Changed += (s, e) => { for (int i = 0; i < 5; i++) pages[i].Visible = i == tabs.Selected; };
    tabHost = new Panel { BackColor = T.Bg };
    pages[0] = BuildFileList(); pages[1] = BuildTarget(); pages[2] = BuildOptions(); pages[3] = BuildStatus();
    logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = T.Bg, ForeColor = T.Fg, Font = T.Mono, WordWrap = false };
    logBox.HandleCreated += (s, e) => Shell.SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null); pages[4] = logBox;
    foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; tabHost.Controls.Add(p); } pages[0].Visible = true;
    pane.Controls.AddRange(new Control[] { header, b1, b2, b3, b4, cUnatt, cVerify, tabs, tabHost });
    pane.Resize += (s, e) => LayoutPane();
    Controls.Add(pane); Controls.Add(splitLine); Controls.Add(side); Controls.Add(toolbar);
    ResumeLayout(); LayoutPane();
  }
  void LayoutPane() {
    int w = pane.ClientSize.Width, p = T.S(14); header.Bounds = new Rectangle(p, T.S(10), w - 2 * p, T.S(104));
    int y = header.Bottom + T.S(8), bw = T.S(110), bh = T.S(32); int x = p;
    foreach (var b in new[] { b1, b2, b3, b4 }) { if (b.Visible) { b.Bounds = new Rectangle(x, y, bw, bh); x += bw + T.S(12); } }
    cVerify.Bounds = new Rectangle(w - p - cVerify.PreferredW, y, cVerify.PreferredW, bh); cUnatt.Bounds = new Rectangle(cVerify.Left - T.S(24) - cUnatt.PreferredW, y, cUnatt.PreferredW, bh);
    tabs.Bounds = new Rectangle(0, y + bh + T.S(12), w, T.S(44)); tabHost.Bounds = new Rectangle(0, tabs.Bottom, w, pane.ClientSize.Height - tabs.Bottom);
  }
  Control BuildFileList() {
    var host = new Panel { BackColor = T.Bg };
    var top = new Panel { Dock = DockStyle.Top, Height = T.S(50), BackColor = T.Bg };
    var menu = new FButton("Menú") { Bounds = new Rectangle(T.S(14), T.S(9), T.S(96), T.S(32)) };
    menu.Click += (s, e) => FileMenu().Show(menu, 0, menu.Height);
    autoScroll = new FCheck("Desplazamiento automático", true); filter = new FCombo("Todos", "Pendientes", "Correctos", "Fallidos", "Saltados", "Carpetas") { Width = T.S(160) };
    filter.SelectedIndexChanged += (s, e) => RebuildView();
    top.Controls.AddRange(new Control[] { menu, autoScroll, filter });
    top.Resize += (s, e) => { filter.Location = new Point(top.Width - filter.Width - T.S(14), T.S(12)); autoScroll.Bounds = new Rectangle(filter.Left - autoScroll.PreferredW - T.S(16), T.S(9), autoScroll.PreferredW, T.S(32)); };
    list = new ListView { View = View.Details, VirtualMode = true, OwnerDraw = true, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None, BackColor = T.Bg, ForeColor = T.Fg, Dock = DockStyle.Fill, Font = T.UI };
    list.Columns.Add("", 100); list.SmallImageList = new ImageList { ImageSize = new Size(1, T.S(34)) };
    list.HandleCreated += (s, e) => Shell.SetWindowTheme(list.Handle, "DarkMode_Explorer", null);
    list.Resize += (s, e) => list.Columns[0].Width = list.ClientSize.Width;
    list.RetrieveVirtualItem += (s, e) => e.Item = new ListViewItem("");
    list.DrawItem += DrawRow; list.DrawSubItem += (s, e) => { }; list.DrawColumnHeader += (s, e) => { };
    list.DoubleClick += (s, e) => { var f = SelFile(); if (f != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{(f.Dst != null && File.Exists(f.Dst) ? f.Dst : f.Src)}\""); };
    list.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) FileMenu().Show(list, e.Location); };
    list.AllowDrop = true;
    list.ItemDrag += (s, e) => { var it = SelItems(); if (it.Count > 0 && it.All(Movable)) list.DoDragDrop(new DataObject("FlashCopy.Items", it), DragDropEffects.Move); };
    list.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent("FlashCopy.Items") ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    list.DragOver += (s, e) => { if (e.Data.GetDataPresent("FlashCopy.Items")) { e.Effect = DragDropEffects.Move; var pt = list.PointToClient(new Point(e.X, e.Y)); var hit = list.GetItemAt(pt.X, pt.Y); list.InsertionMark.Index = hit?.Index ?? -1; } };
    list.DragDrop += (s, e) => {
      list.InsertionMark.Index = -1;
      if (e.Data.GetData("FlashCopy.Items") is List<FItem> it) { var pt = list.PointToClient(new Point(e.X, e.Y)); var hit = list.GetItemAt(pt.X, pt.Y); MoveTo(it, hit != null && hit.Index < view.Count ? view[hit.Index] : null); }
      else OnDragDrop(e);
    };
    emptyLbl = new Label { Text = "Arrastra archivos o carpetas aquí", ForeColor = T.Mut, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, BackColor = T.Bg };
    host.Controls.Add(list); host.Controls.Add(emptyLbl); host.Controls.Add(top); return host;
  }
  List<FItem> SelItems() => list.SelectedIndices.Cast<int>().Where(i => i < view.Count).Select(i => view[i]).ToList();
  int Floor => Busy ? sel.CurIndex + 1 : 0;
  bool Movable(FItem f) { if (!Busy) return true; lock (sel.Files) return f.State == FState.Pending && sel.Files.IndexOf(f) >= Floor; }
  void Reselect(List<FItem> it) { RebuildView(); list.SelectedIndices.Clear(); foreach (var f in it) { int i = view.IndexOf(f); if (i >= 0) list.SelectedIndices.Add(i); } var first = it.Select(x => view.IndexOf(x)).Where(i => i >= 0).DefaultIfEmpty(-1).Min(); if (first >= 0) list.EnsureVisible(first); }
  void MoveSel(int d) {
    var it = SelItems(); if (it.Count == 0 || !it.All(Movable)) return; var L = sel.Files;
    lock (L) {
      int fl = Floor;
      if (d == -2 || d == 2) { L.RemoveAll(it.Contains); L.InsertRange(d < 0 ? Math.Max(fl, 0) : L.Count, it); }
      else { var idx = it.Select(x => L.IndexOf(x)).OrderBy(i => d < 0 ? i : -i).ToList();
        foreach (var i in idx) { int k = i + d; if (k < fl || k >= L.Count || it.Contains(L[k])) continue; var t = L[k]; L[k] = L[i]; L[i] = t; } }
    }
    Reselect(it);
  }
  void MoveTo(List<FItem> it, FItem before) {
    if (it.Count == 0 || !it.All(Movable) || it.Contains(before)) return; var L = sel.Files;
    lock (L) { L.RemoveAll(it.Contains); int at = before == null ? L.Count : Math.Max(Floor, L.IndexOf(before)); L.InsertRange(at, it); }
    Reselect(it);
  }
  void SortBy(Func<FItem, string> key) { if (Busy) return; lock (sel.Files) { var l = sel.Files.OrderBy(key, StringComparer.OrdinalIgnoreCase).ToList(); var dirs = l.Where(x => x.IsDir).OrderBy(x => x.Rel.Length).ToList(); sel.Files.Clear(); sel.Files.AddRange(dirs); sel.Files.AddRange(l.Where(x => !x.IsDir)); } RebuildView(); }
  void RemoveItems(List<FItem> it, bool exact = false) {
    if (sel == null || it.Count == 0) return;
    var all = new HashSet<FItem>(it.Where(Movable));
    if (!exact) foreach (var d in it.Where(x => x.IsDir).ToList()) { var pre = d.Src.TrimEnd('\\') + "\\"; foreach (var c in sel.Files.Where(x => x.Src.StartsWith(pre, StringComparison.OrdinalIgnoreCase))) if (Movable(c)) all.Add(c); }
    if (all.Count == 0) return;
    lock (sel.Files) { sel.Files.RemoveAll(all.Contains); sel.Total = sel.Files.Sum(x => x.Size); }
    sel.Sources.RemoveAll(src => !sel.Files.Any(x => string.Equals(x.Src, src, StringComparison.OrdinalIgnoreCase)));
    if (!Busy && sel.Files.Count == 0) sel.Done = false;
    RebuildView(); Refresh2(true);
  }
  FItem SelFile() => list.SelectedIndices.Count > 0 && list.SelectedIndices[0] < view.Count ? view[list.SelectedIndices[0]] : null;
  void DrawRow(object s, DrawListViewItemEventArgs e) {
    if (e.ItemIndex >= view.Count) return; var f = view[e.ItemIndex]; var g = e.Graphics; var r = e.Bounds; bool selc = e.Item.Selected;
    using (var b = new SolidBrush(selc ? T.Acc : T.Bg)) g.FillRectangle(b, r);
    var ir = new Rectangle(r.X + T.S(14), r.Y, T.S(26), r.Height); string gl; Color c;
    switch (f.State) { case FState.Verified: gl = "\uE73E"; c = T.Ok; T.Text(g, gl, T.Ico, new Rectangle(ir.X - T.S(5), ir.Y, ir.Width, ir.Height), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); break;
      case FState.OK: gl = "\uE73E"; c = T.Ok; break; case FState.Failed: gl = "\uE711"; c = T.Bad; break; case FState.Mismatch: gl = "\uE7BA"; c = T.Warn; break;
      case FState.Skipped: gl = "\uE893"; c = T.Mut; break; case FState.Working: gl = "\uE768"; c = T.Acc; break; default: gl = "\uE71A"; c = T.Fg; break; }
    if (selc && c == T.Acc) c = T.Fg; if (f.State == FState.Pending) T.Text(g, "■", T.UI, ir, T.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); else T.Text(g, gl, T.Ico, ir, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    T.Text(g, f.IsDir ? "\uE8B7" : "\uE7C3", T.Ico, new Rectangle(ir.Right + T.S(4), r.Y, T.S(22), r.Height), f.IsDir ? T.Folder : T.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    int right = r.Right - T.S(14), cw = T.S(96);
    T.Text(g, f.IsDir ? "Carpeta" : Engine.Fmt(f.Size), T.Mono, new Rectangle(right - cw, r.Y, cw, r.Height), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    T.Text(g, Engine.Short(f.DstHash), T.Mono, new Rectangle(right - 2 * cw - T.S(10), r.Y, cw, r.Height), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    T.Text(g, Engine.Short(f.SrcHash), T.Mono, new Rectangle(right - 3 * cw - T.S(20), r.Y, cw, r.Height), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    var pr = new Rectangle(ir.Right + T.S(32), r.Y, right - 3 * cw - T.S(30) - ir.Right - T.S(32), r.Height);
    string extra = f.State == FState.Mismatch ? "  — Los hashes no coinciden" : f.Error != null ? "  — " + f.Error : "";
    T.Text(g, (f.IsDir ? f.Src.TrimEnd('\\') + "\\" : f.Src) + extra, T.UI, pr, T.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
  }
  DarkMenu FileMenu() {
    var m = new DarkMenu(); var f = SelFile();
    m.Add("Añadir archivos…", () => { using (var d = new OpenFileDialog { Multiselect = true, Title = "Añadir archivos" }) if (d.ShowDialog(this) == DialogResult.OK) AddTo(d.FileNames); });
    m.Add("Añadir carpeta…", () => { var p = PickFolder("Añadir carpeta"); if (p != null) AddTo(new[] { p }); });
    m.Items.Add(new ToolStripSeparator());
    m.Add("Abrir ubicación", () => { if (f != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Src}\""); }).Enabled = f != null;
    m.Add("Copiar hash", () => { if (f?.SrcHash != null) Clipboard.SetText(f.SrcHash); }).Enabled = f?.SrcHash != null;
    m.Add("Copiar ruta", () => { if (f != null) Clipboard.SetText(f.Src); }).Enabled = f != null;
    m.Items.Add(new ToolStripSeparator());
    var si = SelItems(); bool mv = si.Count > 0 && si.All(Movable);
    m.Add("Subir  (Ctrl+↑)", () => MoveSel(-1)).Enabled = mv; m.Add("Bajar  (Ctrl+↓)", () => MoveSel(1)).Enabled = mv;
    m.Add("Mover al principio  (Ctrl+Inicio)", () => MoveSel(-2)).Enabled = mv; m.Add("Mover al final  (Ctrl+Fin)", () => MoveSel(2)).Enabled = mv;
    m.Add("Ordenar por nombre", () => SortBy(x => x.Rel)).Enabled = !Busy; m.Add("Ordenar por tamaño (pequeños primero)", () => SortBy(x => x.Size.ToString("D20"))).Enabled = !Busy;
    m.Items.Add(new ToolStripSeparator());
    m.Add("Quitar de la lista  (Supr)", () => RemoveItems(SelItems())).Enabled = si.Any(Movable);
    m.Add("Quitar completados", () => RemoveItems(sel.Files.Where(x => !x.IsDir && (x.State == FState.OK || x.State == FState.Verified)).ToList(), true)).Enabled = !Busy;
    m.Add("Seleccionar fallidos para reintentar", () => { if (Busy) return; sel.Files.RemoveAll(x => !x.IsDir && x.State != FState.Failed && x.State != FState.Mismatch); sel.Total = sel.Files.Sum(x => x.Size); sel.Done = false; RebuildView(); }).Enabled = !Busy && sel.Done;
    m.Add("Vaciar lista", () => { if (Busy) return; sel.Files.Clear(); sel.Sources.Clear(); sel.Total = 0; sel.Done = false; RebuildView(); }).Enabled = !Busy;
    m.Items.Add(new ToolStripSeparator());
    m.Add("Guardar archivo de checksums", () => { if (sel.Files.Any(x => x.SrcHash != null)) MessageBox.Show(this, "Guardado en:\n" + Engine.SaveChecksum(sel)); });
    m.Add("Exportar informe HTML", () => { using (var d = new SaveFileDialog { Filter = "HTML|*.html", FileName = "informe.html" }) if (d.ShowDialog(this) == DialogResult.OK) Engine.ExportHtml(sel, d.FileName); });
    return m;
  }
  Control BuildTarget() {
    var host = new Panel { BackColor = T.Bg, Padding = new Padding(T.S(14)) };
    var bar = new Panel { Dock = DockStyle.Bottom, Height = T.S(50) };
    var browse = new FButton("Examinar…") { Bounds = new Rectangle(0, T.S(10), T.S(120), T.S(32)) }; var cp = new FButton("Copiar aquí") { Primary = true, Bounds = new Rectangle(T.S(132), T.S(10), T.S(120), T.S(32)) }; var mv = new FButton("Mover aquí") { Bounds = new Rectangle(T.S(264), T.S(10), T.S(120), T.S(32)) }; var clr = new FButton("Limpiar lista") { Bounds = new Rectangle(T.S(396), T.S(10), T.S(120), T.S(32)) };
    bar.Controls.AddRange(new Control[] { browse, cp, mv, clr });
    recent = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = T.Bg, ForeColor = T.Fg, Font = T.UI, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = T.S(30), IntegralHeight = false };
    recent.DrawItem += (s, e) => { if (e.Index < 0) return; bool sl = (e.State & DrawItemState.Selected) != 0; using (var b = new SolidBrush(sl ? T.Acc : T.Bg)) e.Graphics.FillRectangle(b, e.Bounds); T.Text(e.Graphics, "\uE8B7", T.Ico, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, T.S(24), e.Bounds.Height), T.Folder); T.Text(e.Graphics, recent.Items[e.Index].ToString(), T.UI, new Rectangle(e.Bounds.X + T.S(34), e.Bounds.Y, e.Bounds.Width - T.S(34), e.Bounds.Height), T.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis); };
    recent.HandleCreated += (s, e) => Shell.SetWindowTheme(recent.Handle, "DarkMode_Explorer", null);
    recent.DoubleClick += (s, e) => { if (recent.SelectedItem != null) Start(Mode.Copy, recent.SelectedItem.ToString()); };
    browse.Click += (s, e) => { var p = PickFolder("Elegir destino"); if (p != null) { Settings.AddRecent(p); LoadRecent(); recent.SelectedItem = p; } };
    cp.Click += (s, e) => { if (recent.SelectedItem != null) Start(Mode.Copy, recent.SelectedItem.ToString()); };
    mv.Click += (s, e) => { if (recent.SelectedItem != null) Start(Mode.Move, recent.SelectedItem.ToString()); };
    clr.Click += (s, e) => { Settings.Recent = new List<string>(); LoadRecent(); };
    var lbl = new Label { Text = "Carpetas de destino recientes (doble clic para copiar)", Dock = DockStyle.Top, Height = T.S(28), ForeColor = T.Mut };
    host.Controls.Add(recent); host.Controls.Add(lbl); host.Controls.Add(bar); LoadRecent(); return host;
  }
  void LoadRecent() { recent.Items.Clear(); foreach (var r in Settings.Recent) recent.Items.Add(r); }
  Control BuildOptions() {
    var h = new Panel { BackColor = T.Bg, AutoScroll = true }; int c1 = T.S(16), c2 = T.S(280), c3 = T.S(600), y0 = T.S(14), rh = T.S(30);
    void Add(Control c, int x, int y, int w = 0) { c.Location = new Point(x, y); if (c is FCheck fc) c.Size = new Size(w > 0 ? w : fc.PreferredW, T.S(26)); else if (w > 0) c.Width = w; h.Controls.Add(c); }
    Add(new Header("Transferir"), c1, y0); Add(new Header("Archivos y carpetas") { Font = T.UI }, c1, y0 + rh);
    oData = new FCheck("Datos", true) { Enabled = false }; oTimes = new FCheck("Marcas de tiempo", Settings.B("o.times", true)); oAttrs = new FCheck("Atributos", Settings.B("o.attrs", true));
    Add(oData, c1, y0 + rh * 2); Add(oTimes, c1, y0 + rh * 3); Add(oAttrs, c1, y0 + rh * 4);
    Add(new Header("Seguridad") { Font = T.UI }, c1, y0 + rh * 5 + T.S(6)); oAcl = new FCheck("Permisos (ACL)", Settings.B("o.acl", false)); Add(oAcl, c1, y0 + rh * 6 + T.S(6));
    oRecycle = new FCheck("Eliminar a la Papelera", Settings.B("o.recycle", true)); Add(oRecycle, c1, y0 + rh * 7 + T.S(12));
    Add(new Header("Si el archivo ya existe"), c2, y0); oConflict = new FCombo(ConflictNames); Add(oConflict, c2, y0 + rh, T.S(280)); oConflict.SelectedIndex = int.Parse(Settings.Get("o.conflict", "0"));
    oHtml = new FCheck("Exportar informe HTML al terminar", Settings.B("o.html", false)); Add(oHtml, c2, y0 + rh * 2 + T.S(6));
    oVerifyAfter = new FCheck("Verificar archivos tras transferir", Settings.B("verify", true)); oVerifyAfter.Changed += (s, e) => { cVerify.Checked = oVerifyAfter.Checked; Settings.SetB("verify", oVerifyAfter.Checked); }; Add(oVerifyAfter, c2, y0 + rh * 3 + T.S(6));
    oSaveSum = new FCheck("Guardar archivo de checksums al terminar", Settings.B("o.sum", false)); Add(oSaveSum, c2, y0 + rh * 4 + T.S(6));
    Add(new Header("Verificación y rendimiento"), c3, y0); oHash = new FCombo(Engine.Algos.Select(a => "Hash: " + a).ToArray()); Add(oHash, c3, y0 + rh, T.S(200)); oHash.SelectedIndex = Math.Max(0, Array.IndexOf(Engine.Algos, Settings.Get("o.hash", "CRC32")));
    oBuf = new FCombo(Buffers.Select(b => b + " MB").ToArray()); Add(oBuf, c3, y0 + rh * 2 + T.S(8), T.S(100)); oBuf.SelectedIndex = Math.Max(0, Array.IndexOf(Buffers, int.Parse(Settings.Get("o.buf", "4"))));
    Add(new Label { Text = "tamaño de búfer", ForeColor = T.Fg, AutoSize = true }, c3 + T.S(110), y0 + rh * 2 + T.S(12));
    Add(new Header("Al terminar"), c3, y0 + rh * 3 + T.S(14)); oFinish = new FCombo(FinishNames); Add(oFinish, c3, y0 + rh * 4 + T.S(14), T.S(200)); oFinish.SelectedIndex = Array.IndexOf(FinishKeys, onFinish); oFinish.SelectedIndexChanged += (s, e) => { onFinish = FinishKeys[oFinish.SelectedIndex]; Settings.Set("onfinish", onFinish); UpdateFinishLbl(); };
    Add(new Label { Text = "Recomendado para tu SSD: búfer 4–16 MB · CRC32 o SHA256", ForeColor = T.Mut, AutoSize = true }, c3, y0 + rh * 5 + T.S(22));
    var save = new FButton("Guardar como predeterminado"); Add(save, c2, y0 + rh * 9, T.S(240)); save.Height = T.S(34); save.Click += (s, e) => { SaveOpts(); MessageBox.Show(this, "Opciones guardadas."); };
    var reset = new FButton("Restablecer"); Add(reset, c2 + T.S(256), y0 + rh * 9, T.S(160)); reset.Height = T.S(34);
    reset.Click += (s, e) => { oTimes.Checked = oAttrs.Checked = oRecycle.Checked = oVerifyAfter.Checked = true; oAcl.Checked = oHtml.Checked = oSaveSum.Checked = false; oConflict.SelectedIndex = 0; oHash.SelectedIndex = 0; oBuf.SelectedIndex = 2; SaveOpts(); };
    return h;
  }
  void SaveOpts() { Settings.SetB("o.times", oTimes.Checked); Settings.SetB("o.attrs", oAttrs.Checked); Settings.SetB("o.acl", oAcl.Checked); Settings.SetB("o.recycle", oRecycle.Checked); Settings.SetB("o.html", oHtml.Checked); Settings.SetB("o.sum", oSaveSum.Checked); Settings.SetB("verify", oVerifyAfter.Checked); Settings.Set("o.conflict", oConflict.SelectedIndex.ToString()); Settings.Set("o.hash", Engine.Algos[oHash.SelectedIndex]); Settings.Set("o.buf", Buffers[oBuf.SelectedIndex].ToString()); }
  Opts CurrentOpts() => new Opts { Conflict = (Conflict)oConflict.SelectedIndex, Hash = Engine.Algos[oHash.SelectedIndex], Buffer = Buffers[oBuf.SelectedIndex] << 20, Verify = cVerify.Checked || Settings.B("alwaystest", false), Times = oTimes.Checked, Attrs = oAttrs.Checked, Acl = oAcl.Checked, Unattended = cUnatt.Checked, SysCache = Settings.B("syscache", true), FreeSpace = Settings.B("freespace", true), SaveChecksum = oSaveSum.Checked, ExportHtml = oHtml.Checked, Recycle = oRecycle.Checked };
  Control BuildStatus() {
    var h = new Panel { BackColor = T.Bg, Padding = new Padding(T.S(14)) };
    chart = new ChartCtl(this) { Dock = DockStyle.Top, Height = T.S(170) }; summary = new SummaryCtl(this) { Dock = DockStyle.Fill };
    h.Controls.Add(summary); h.Controls.Add(new Panel { Dock = DockStyle.Top, Height = T.S(12) }); h.Controls.Add(chart); return h;
  }
  static readonly string[] FinishKeys = { "keep", "close", "closeok", "shutdown", "sleep", "hibernate" };
  static readonly string[] FinishNames = { "mantener abierta", "cerrar la app", "cerrar si no hay errores", "apagar el PC", "suspender", "hibernar" };
  void UpdateFinishLbl() => finishLbl.Text = "Al terminar: " + FinishNames[Math.Max(0, Array.IndexOf(FinishKeys, onFinish))] + "  \uE70D".Replace("\uE70D", "⌄");
  DarkMenu FinishMenu() { var m = new DarkMenu(); for (int i = 0; i < FinishKeys.Length; i++) { int k = i; m.Add(char.ToUpper(FinishNames[i][0]) + FinishNames[i].Substring(1), () => { onFinish = FinishKeys[k]; Settings.Set("onfinish", onFinish); oFinish.SelectedIndex = k; UpdateFinishLbl(); }, onFinish == FinishKeys[i]); } return m; }

  // ---------- tareas ----------
  public IReadOnlyList<Job> Jobs => jobs; public Job Sel => sel; bool Busy => sel != null && sel.Running; bool CanResume => sel != null && !sel.Running && sel.Stopped && sel.Files.Any(f => !f.IsDir && f.State == FState.Pending);
  public void Select(Job j) { sel = j; RebuildView(); logBox.Text = j.Log.ToString(); Refresh2(true); }
  Job NewJob() { var j = new Job(); jobs.Add(j); Select(j); return j; }
  void DuplicateJob() { if (sel == null) return; var j = NewJob(); Engine.AddSources(j, jobs[jobs.Count - 2].Sources); RebuildView(); }
  public void RemoveJob(Job j) { if (j.Running) return; jobs.Remove(j); if (jobs.Count == 0) NewJob(); else Select(jobs.Last()); }
  void AddTo(IEnumerable<string> paths) { var j = sel == null || sel.Running || sel.Done ? NewJob() : sel; Cursor = Cursors.WaitCursor; Engine.AddSources(j, paths); Cursor = Cursors.Default; RebuildView(); Refresh2(true); }
  void RebuildView() {
    if (sel == null) return; int fi = filter.SelectedIndex;
    view = sel.Files.Where(f => fi == 0 || (fi == 1 && (f.State == FState.Pending || f.State == FState.Working)) || (fi == 2 && (f.State == FState.OK || f.State == FState.Verified)) || (fi == 3 && (f.State == FState.Failed || f.State == FState.Mismatch)) || (fi == 4 && f.State == FState.Skipped) || (fi == 5 && f.IsDir)).ToList();
    list.VirtualListSize = view.Count; list.Invalidate(); emptyLbl.Visible = sel.Files.Count == 0;
  }
  DarkMenu TargetMenu(Mode mode) {
    var m = new DarkMenu();
    if (sel.Files.Count == 0) { m.Add("Primero añade archivos (arrástralos aquí)", () => { }).Enabled = false; return m; }
    m.Add("Examinar…", () => { var p = PickFolder(mode == Mode.Move ? "Mover a" : "Copiar a"); if (p != null) Start(mode, p); });
    var rec = Settings.Recent; if (rec.Count > 0) m.Items.Add(new ToolStripSeparator());
    foreach (var r in rec) { var rr = r; m.Add(r, () => Start(mode, rr)); }
    return m;
  }
  string PickFolder(string title) { using (var d = new FolderPicker { Title = title }) return d.Show(Handle); }
  void Start(Mode mode, string target) {
    var j = sel; if (j == null || j.Files.Count == 0 || j.Running) return;
    if (target != null) { if (!Directory.Exists(target)) { MessageBox.Show(this, "La carpeta no existe:\n" + target); return; } Settings.AddRecent(target); LoadRecent(); }
    bool resume = j.Stopped && j.Mode == mode && string.Equals(j.Target, target, StringComparison.OrdinalIgnoreCase);
    j.Mode = mode; j.Target = target; j.O = CurrentOpts(); tabs.Selected = 0;
    new Thread(() => { Engine.Run(j, resume); BeginInvoke(new Action(() => Finished(j))); }) { IsBackground = true, Priority = ThreadPriority.AboveNormal }.Start();
    Refresh2(true);
  }
  void Pause() { if (sel.Paused) { sel.Gate.Set(); sel.L("Reanudado"); } else { sel.Gate.Reset(); sel.L("En pausa"); } Refresh2(true); }
  void Finished(Job j) {
    Refresh2(true); RebuildView(); int bad = j.Count(FState.Failed, FState.Mismatch);
    Settings.History($"{j.End:yyyy-MM-dd HH:mm} | {j.Title} | {j.Files.Count(f => !f.IsDir)} archivos | {Engine.Fmt(j.Total)} | {string.Join(", ", j.Sources)} → {j.Target ?? "-"}");
    if (Settings.B("sound", false) || bad > 0) (bad > 0 ? SystemSounds.Hand : SystemSounds.Asterisk).Play();
    if (Settings.B("notify", false) || !Visible) { tray.Visible = true; tray.ShowBalloonTip(4000, "FlashCopy Pro", $"{j.Title}: {j.Count(FState.OK, FState.Verified)} correctos, {bad} con errores", bad > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info); }
    if (bad > 0) { filter.SelectedIndex = 3; if (!Visible) ShowMain(); }
    if (j.Stopped) return;
    if (onFinish == "close" || (onFinish == "closeok" && bad == 0)) { if (!jobs.Any(x => x.Running)) { reallyClose = !Settings.B("handler", false); Close(); } }
    else if (onFinish == "shutdown") System.Diagnostics.Process.Start("shutdown", "/s /t 60 /c \"FlashCopy: transferencia terminada\"");
    else if (onFinish == "sleep") Application.SetSuspendState(PowerState.Suspend, false, false);
    else if (onFinish == "hibernate") Application.SetSuspendState(PowerState.Hibernate, false, false);
  }
  Tuple<Conflict, bool> AskConflict(Job j, FItem f) {
    Tuple<Conflict, bool> r = null;
    Invoke(new Action(() => { if (!Visible) ShowMain(); using (var d = new ConflictForm(f)) { d.ShowDialog(this); r = Tuple.Create(d.Choice, d.All); } }));
    return r;
  }
  void Refresh2(bool force = false) {
    tick++;
    foreach (var j in jobs.Where(x => x.Running)) if (tick % 2 == 0 || force) { if (!j.Paused) { j.LastSpeed = (j.DoneBytes - j.LastBytes) * 2.0; j.Speed.Add(j.LastSpeed); } j.LastBytes = j.DoneBytes; }
    header.Invalidate(); jobList.Invalidate(); chart.Invalidate(); summary.Invalidate();
    bool busy = Busy; b1.Text = busy ? (sel.Paused ? "Reanudar" : "Pausa") : CanResume ? "Reanudar" : "Copiar…"; b2.Text = busy ? "Saltar" : "Mover…"; b3.Text = busy ? "Detener" : "Probar";
    if (b4.Visible == busy) { b4.Visible = !busy; LayoutPane(); }
    if (sel != null) {
      if (sel.Running || force) { list.Invalidate(); if (sel.Running && autoScroll.Checked && sel.Cur != null && tick % 2 == 0) { int i = view.IndexOf(sel.Cur); if (i >= 0) list.EnsureVisible(i); } }
      if (logBox.Visible && logBox.TextLength != sel.Log.Length) { logBox.Text = sel.Log.ToString(); logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret(); }
      Text = sel.Running ? $"{sel.Pct:0.0}% hecho ({Engine.Fmt(sel.LastSpeed)}/s) - FlashCopy Pro" : "FlashCopy Pro";
      if (tray != null) tray.Text = Text.Length > 63 ? Text.Substring(0, 63) : Text;
    }
    var run = jobs.Where(x => x.Running).ToList(); long tot = run.Sum(x => x.Total), dn = run.Sum(x => x.DoneBytes); double pct = tot > 0 ? Math.Min(100, dn * 100.0 / tot) : 0;
    int st = run.Count == 0 ? Taskbar.None : run.All(x => x.Paused) ? Taskbar.Paused : run.Any(x => x.Count(FState.Failed, FState.Mismatch) > 0) ? Taskbar.Error : Taskbar.Normal;
    if (IsHandleCreated) Taskbar.Set(Handle, st, pct);
    int ip = run.Count == 0 ? -1 : (int)pct;
    if (ip != trayPct && tray != null) { trayPct = ip; var old = tray.Icon; tray.Icon = ip < 0 ? Icon : Icon.FromHandle(Taskbar.MakeIcon(ip, 16, Color.FromArgb(0x2E, 0x8B, 0x57))); if (old != Icon && old != null) { Shell.DestroyIcon(old.Handle); old.Dispose(); } }
  }

  // ---------- argumentos / Explorador ----------
  public void ReceiveArgs(string[] a) {
    if (a.Length == 0) { ShowMain(); return; }
    var v = a[0].ToLower(); var files = a.Skip(v.StartsWith("/") ? 1 : 0).Where(x => File.Exists(x) || Directory.Exists(x)).ToList();
    if (v == "/tray") return;
    if (v == "/paste") {
      var d = a.Skip(1).Select(x => x.Trim().Trim('"')).FirstOrDefault() ?? "";
      if (d.Length == 2 && d[1] == ':') d += "\\";
      while (d.Length > 3 && !Directory.Exists(d)) d = Path.GetDirectoryName(d) ?? "";
      if (!Directory.Exists(d)) d = Shell.ForegroundFolder();
      if (d == null) { ShowMain(); MessageBox.Show(this, "No se ha podido saber en qué carpeta pegar.", "FlashCopy"); return; }
      PasteInto(d); return;
    }
    if (!v.StartsWith("/")) v = "/add";
    if (pendingVerb != null && pendingVerb != v) Flush();
    pendingVerb = v; pending.AddRange(files); debounce.Stop(); debounce.Start();
  }
  void Flush() {
    if (pendingVerb == null) return; var v = pendingVerb; var files = pending.Distinct().ToList(); pendingVerb = null; pending.Clear();
    ShowMain(); if (files.Count == 0) return;
    var j = NewJob(); Engine.AddSources(j, files); RebuildView();
    if (v == "/copy") TargetMenu(Mode.Copy).Show(b1, 0, b1.Height);
    else if (v == "/move") TargetMenu(Mode.Move).Show(b2, 0, b2.Height);
    else if (v == "/test") Start(Mode.Test, null);
  }
  void PasteInto(string dir) {
    if (dir == null) dir = PickFolder("Pegar en");
    if (dir == null) return;
    bool has = false; for (int t = 0; t < 10 && !has; t++) { try { has = Clipboard.ContainsFileDropList(); break; } catch { Thread.Sleep(50); } }
    if (!has) { ShowMain(); MessageBox.Show(this, "No hay archivos copiados en el portapapeles.\nCopia o corta archivos en el Explorador (Ctrl+C / Ctrl+X) y vuelve a pegar.", "FlashCopy"); return; }
    var files = Clipboard.GetFileDropList().Cast<string>().ToList(); bool cut = false;
    if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms) { var b = new byte[4]; ms.Read(b, 0, 4); cut = (b[0] & 2) != 0; }
    ShowMain(); var j = NewJob(); Engine.AddSources(j, files); RebuildView(); Start(cut ? Mode.Move : Mode.Copy, dir);
    if (cut) Clipboard.Clear();
  }
  protected override void OnDragEnter(DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
  protected override void OnDragDrop(DragEventArgs e) {
    var f = (string[])e.Data.GetData(DataFormats.FileDrop); if (f == null) return; Activate();
    if (tabs.Selected == 1 && f.Length == 1 && Directory.Exists(f[0])) { Settings.AddRecent(f[0]); LoadRecent(); return; }
    if (Settings.B("confirmdrop", true) && sel != null && sel.Files.Count > 0 && !sel.Running && !sel.Done && MessageBox.Show(this, $"¿Añadir {f.Length} elemento(s) a la tarea actual?", "FlashCopy", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
    AddTo(f);
  }
  protected override void OnKeyDown(KeyEventArgs e) {
    if (e.Control && e.KeyCode == Keys.V && Clipboard.ContainsFileDropList()) { AddTo(Clipboard.GetFileDropList().Cast<string>()); e.Handled = true; }
    if (e.Control && e.KeyCode == Keys.N) NewJob();
    if (list.Focused) {
      if (e.KeyCode == Keys.Delete) { RemoveItems(SelItems()); e.Handled = true; }
      else if (e.Control && e.KeyCode == Keys.Up) { MoveSel(-1); e.Handled = true; } else if (e.Control && e.KeyCode == Keys.Down) { MoveSel(1); e.Handled = true; }
      else if (e.Control && e.KeyCode == Keys.Home) { MoveSel(-2); e.Handled = true; } else if (e.Control && e.KeyCode == Keys.End) { MoveSel(2); e.Handled = true; }
      else if (e.Control && e.KeyCode == Keys.A) { for (int i = 0; i < view.Count; i++) list.SelectedIndices.Add(i); e.Handled = true; }
    }
    base.OnKeyDown(e);
  }
  void ApplyPrefs() {
    TopMost = Settings.B("top", false); Shell.UnregisterHotKey(Handle, 1);
    if (Settings.B("handler", false)) Shell.RegisterHotKey(Handle, 1, 0x2 | 0x4 | 0x4000, (uint)Keys.V); tray.Visible = Settings.B("handler", false); HookState();
  }
  void ShowHistory() {
    using (var f = new DarkForm { Text = "Historial", Size = new Size(T.S(900), T.S(500)), StartPosition = FormStartPosition.CenterParent }) {
      var tb = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = T.Bg, ForeColor = T.Fg, Font = T.Mono, ScrollBars = ScrollBars.Both, WordWrap = false };
      try { tb.Text = string.Join(Environment.NewLine, File.ReadAllLines(Settings.HistoryFile).Reverse()); } catch { tb.Text = "Sin historial todavía."; }
      tb.HandleCreated += (s, e) => Shell.SetWindowTheme(tb.Handle, "DarkMode_Explorer", null); f.Controls.Add(tb); f.ShowDialog(this);
    }
  }
}

class HeaderCtl : Base {
  readonly MainForm m; public HeaderCtl(MainForm f) { m = f; }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics; g.Clear(T.Bg); var j = m.Sel; if (j == null) return; int w = Width, cw = T.S(86), rh = T.S(30);
    void Row(int y, string tag, string path, string[] st, double pct) {
      var tr = new Rectangle(0, y, T.S(96), rh); T.Fill(g, tr, T.Btn); T.Text(g, tag, Font, tr, T.Mut, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
      int sx = w - cw * st.Length; T.Text(g, path, Font, new Rectangle(tr.Right + T.S(16), y, sx - tr.Right - T.S(24), rh), T.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
      for (int i = 0; i < st.Length; i++) T.Text(g, st[i], Font, new Rectangle(sx + cw * i, y, cw, rh), i == st.Length - 1 ? T.Fg : T.Mut, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
      using (var b = new SolidBrush(T.Line)) g.FillRectangle(b, 0, y + rh + T.S(8), w, T.S(5));
      using (var b = new SolidBrush(j.Paused ? T.Warn : T.Acc)) g.FillRectangle(b, 0, y + rh + T.S(8), (int)(w * Math.Max(0, Math.Min(1, pct / 100))), T.S(5));
    }
    var f = j.Cur; int nfiles = j.Files.Count(x => !x.IsDir), idx = j.Files.Where(x => !x.IsDir).TakeWhile(x => x != f).Count() + (f != null ? 1 : 0);
    double fp = f != null && f.Size > 0 ? j.CurDone * 100.0 / f.Size : j.Done ? 100 : 0;
    string tag1 = j.Running ? (j.Phase == "Verificando" ? "Verificando" : j.ModeName) : "Origen";
    string src = f?.Src ?? (j.Files.Count == 0 ? "Arrastra archivos o carpetas aquí, o usa clic derecho → Copiar con FlashCopy" : string.Join("; ", j.Sources));
    Row(0, tag1, src, new[] { $"{(j.Running ? idx : j.Processed)} / {nfiles}", f != null ? Engine.Fmt(j.CurDone) : "", f != null ? Engine.Fmt(f.Size) : "", $"{fp:0.0} %" }, fp);
    double eta = j.LastSpeed > 0 ? (j.Total - j.DoneBytes) / j.LastSpeed : 0;
    string et = eta > 3600 ? $"{(int)(eta / 3600)} h {(int)(eta % 3600 / 60)} m" : eta > 60 ? $"{(int)(eta / 60)} m {(int)(eta % 60)} s" : $"{(int)eta} s";
    Row(T.S(52), "Destino", f?.Dst ?? j.Target ?? (j.Mode == Mode.Test && j.Started ? "Sin destino (prueba)" : "Sin destino"), new[] { j.Running ? $"{Engine.Fmt(j.LastSpeed)}/s - {et}" : j.Done ? $"{(j.End - j.Start):hh\\:mm\\:ss}" : "", Engine.Fmt(j.DoneBytes), Engine.Fmt(j.Total), $"{j.Pct:0.0} %" }, j.Pct);
  }
}
class JobListCtl : Base {
  readonly MainForm m; int H => T.S(94); public JobListCtl(MainForm f) { m = f; BackColor = T.Side; }
  protected override void OnMouseClick(MouseEventArgs e) {
    int i = e.Y / H; if (i >= m.Jobs.Count) return; var j = m.Jobs[i]; m.Select(j);
    if (e.Button == MouseButtons.Right) { var mm = new DarkMenu(); mm.Add("Quitar tarea", () => m.RemoveJob(j)).Enabled = !j.Running; mm.Add("Quitar tareas terminadas", () => { foreach (var x in m.Jobs.Where(k => k.Done).ToList()) m.RemoveJob(x); }); mm.Show(this, e.Location); }
  }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics; g.Clear(T.Side); int y = 0;
    foreach (var j in m.Jobs) {
      var r = new Rectangle(0, y, Width, H); if (j == m.Sel) using (var b = new SolidBrush(T.Card)) g.FillRectangle(b, r);
      int p = T.S(14), w = Width - 2 * p;
      T.Text(g, "\uE8C8", T.Ico, new Rectangle(p, y + T.S(8), T.S(22), T.S(24)), T.Fg); T.Text(g, j.Title, T.UI, new Rectangle(p + T.S(26), y + T.S(8), w - T.S(50), T.S(24)), T.Fg);
      string st = j.Running ? (j.Paused ? "\uE769" : "\uE768") : j.Done ? (j.Count(FState.Failed, FState.Mismatch) > 0 ? "\uE7BA" : "\uE73E") : "";
      T.Text(g, st, T.Ico, new Rectangle(Width - p - T.S(24), y + T.S(8), T.S(24), T.S(24)), j.Running ? (j.Paused ? T.Warn : T.Ok) : j.Count(FState.Failed, FState.Mismatch) > 0 ? T.Warn : T.Ok, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
      using (var b = new SolidBrush(T.Btn)) g.FillRectangle(b, p, y + T.S(36), w, T.S(5)); using (var b = new SolidBrush(j.Done ? T.Mut : T.Acc)) g.FillRectangle(b, p, y + T.S(36), (int)(w * j.Pct / 100), T.S(5));
      var src = j.Sources.Count == 0 ? "Sin archivos añadidos" : j.Sources.Count == 1 ? (Directory.Exists(j.Sources[0]) ? j.Sources[0].TrimEnd('\\') + "\\" : Path.GetDirectoryName(j.Sources[0]) + "\\") : $"{j.Sources.Count} elementos";
      T.Text(g, src, T.UI, new Rectangle(p, y + T.S(48), w - T.S(70), T.S(20)), T.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis); T.Text(g, j.Files.Count(x => !x.IsDir).ToString("N0"), T.UI, new Rectangle(p + w - T.S(70), y + T.S(48), T.S(70), T.S(20)), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
      T.Text(g, j.Target ?? "Sin destino", T.UI, new Rectangle(p, y + T.S(68), w - T.S(70), T.S(20)), T.Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis); T.Text(g, Engine.Fmt(j.Total), T.UI, new Rectangle(p + w - T.S(70), y + T.S(68), T.S(70), T.S(20)), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
      using (var pen = new Pen(T.Line)) g.DrawLine(pen, 0, y + H - 1, Width, y + H - 1); y += H;
    }
  }
}
class ChartCtl : Base {
  readonly MainForm m; public ChartCtl(MainForm f) { m = f; }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics; g.Clear(T.Bg); var j = m.Sel; using (var b = new SolidBrush(T.Band)) g.FillRectangle(b, ClientRectangle); if (j == null || j.Speed.Count < 2) { T.Text(g, "La gráfica de velocidad aparecerá al transferir", Font, ClientRectangle, T.Mut, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
    var s = j.Speed; double mx = Math.Max(1, s.Max()); int n = Math.Max(120, s.Count); float dx = (float)Width / n; int h = Height - T.S(6);
    var pts = new List<PointF> { new PointF(0, Height) }; for (int i = 0; i < s.Count; i++) pts.Add(new PointF(i * dx, Height - (float)(s[i] / mx * h))); pts.Add(new PointF((s.Count - 1) * dx, Height));
    using (var lb = new SolidBrush(Color.FromArgb(0x38, 0x38, 0x38))) g.FillRectangle(lb, 0, 0, (s.Count - 1) * dx, Height);
    g.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(T.Acc)) g.FillPolygon(b, pts.ToArray());
    T.Text(g, "máx " + Engine.Fmt(mx) + "/s", Font, new Rectangle(0, T.S(4), Width - T.S(8), T.S(20)), T.Fg, TextFormatFlags.Right);
  }
}
class SummaryCtl : Base {
  readonly MainForm m; public SummaryCtl(MainForm f) { m = f; }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics; g.Clear(T.Bg); var j = m.Sel; if (j == null) return; using (var b = new SolidBrush(T.Band)) g.FillRectangle(b, ClientRectangle);
    int rh = T.S(30), p = T.S(14), w = Width - 2 * p; int[] cx = { p, p + (int)(w * .35), p + (int)(w * .7), p + w };
    void Row(int y, string a, string b, string c, string d, Font f) { T.Text(g, a, f, new Rectangle(cx[0], y, T.S(200), rh), T.Fg); T.Text(g, b, f, new Rectangle(cx[1] - T.S(120), y, T.S(120), rh), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter); T.Text(g, c, f, new Rectangle(cx[2] - T.S(220), y, T.S(220), rh), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter); T.Text(g, d, f, new Rectangle(cx[3] - T.S(120), y, T.S(120), rh), T.Fg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter); }
    Row(T.S(8), "Categoría", "Archivos", "Bytes", "Tiempo", T.UIb); int y = T.S(8) + rh;
    var files = j.Files.Where(x => !x.IsDir).ToList();
    foreach (var c in new[] { ("Correctos", new[] { FState.OK, FState.Verified }), ("Verificados", new[] { FState.Verified }), ("Fallidos", new[] { FState.Failed, FState.Mismatch }), ("Saltados", new[] { FState.Skipped }), ("Pendientes", new[] { FState.Pending, FState.Working }) }) {
      var l = files.Where(x => c.Item2.Contains(x.State)).ToList(); if (l.Count == 0) continue; Row(y, c.Item1, l.Count.ToString("N0"), l.Sum(x => x.Size).ToString("N0"), "", T.Mono); y += rh;
    }
    Row(y, "---", "", "", "", T.Mono); y += rh;
    var t = j.Started ? ((j.Done ? j.End : DateTime.Now) - j.Start) : TimeSpan.Zero;
    Row(y, "Total", files.Count.ToString("N0"), j.Total.ToString("N0"), t.ToString(@"hh\:mm\:ss"), T.Mono);
    if (j.Started && t.TotalSeconds > 0) T.Text(g, $"Velocidad media: {Engine.Fmt(j.DoneBytes / t.TotalSeconds)}/s · Hash: {j.O.Hash} · Búfer: {Engine.Fmt(j.O.Buffer)}", T.UI, new Rectangle(p, y + rh + T.S(6), w, rh), T.Mut);
  }
}
}

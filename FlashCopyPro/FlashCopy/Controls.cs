using System; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Text; using System.Linq; using System.Windows.Forms;
namespace FlashCopy {
public static class T {
  public static Color Bg = C(0x202020), Side = C(0x1A1A1A), Band = C(0x2B2B2B), Card = C(0x2D2D2D), Btn = C(0x3A3A3A), BtnHot = C(0x454545), Line = C(0x333333),
    Fg = C(0xE6E6E6), Mut = C(0x9A9A9A), Acc = C(0x0078D4), Ok = C(0x6CCB5F), Bad = C(0xFF6B6B), Warn = C(0xFFC83D), Folder = C(0xF4C542);
  static Color C(int v) => Color.FromArgb(v >> 16 & 255, v >> 8 & 255, v & 255);
  public static Font UI = new Font("Segoe UI", 9.75f), UIb = new Font("Segoe UI Semibold", 9.75f), Mono = new Font("Consolas", 9.75f);
  public static Font Icon(float s) { var n = new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets"; return new Font(n, s); }
  public static Font Ico = Icon(11.5f), IcoS = Icon(9f);
  public static float Scale = 1;
  public static int S(float v) => (int)Math.Round(v * Scale);
  public static GraphicsPath Round(Rectangle r, int rad) { var p = new GraphicsPath(); int d = rad * 2; p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90); p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90); p.CloseFigure(); return p; }
  public static void Fill(Graphics g, Rectangle r, Color c, int rad = 4) { g.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(c)) using (var p = Round(r, S(rad))) g.FillPath(b, p); g.SmoothingMode = SmoothingMode.None; }
  public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags fl = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix) => TextRenderer.DrawText(g, s, f, r, c, fl);
}
public class Base : Control { public Base() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); ForeColor = T.Fg; Font = T.UI; } protected bool Hot; protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); } protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); } protected override void OnEnabledChanged(EventArgs e) { Invalidate(); } }
public class FButton : Base {
  public bool Primary; public FButton(string t) { Text = t; Cursor = Cursors.Hand; }
  protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent.BackColor); var r = new Rectangle(0, 0, Width, Height); T.Fill(e.Graphics, r, !Enabled ? T.Card : Primary ? (Hot ? ControlPaint.Light(T.Acc) : T.Acc) : Hot ? T.BtnHot : T.Btn); T.Text(e.Graphics, Text, Font, r, Enabled ? T.Fg : T.Mut, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); }
}
public class IconBtn : Base {
  public string Glyph; public IconBtn(string g, string tip, ToolTip tt) { Glyph = g; tt.SetToolTip(this, tip); Cursor = Cursors.Hand; }
  protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent.BackColor); var r = new Rectangle(0, 0, Width, Height); if (Hot) T.Fill(e.Graphics, r, T.Btn); T.Text(e.Graphics, Glyph, T.Ico, r, T.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
}
public class FCheck : Base {
  bool c; public bool Checked { get => c; set { c = value; Invalidate(); } } public event EventHandler Changed;
  public FCheck(string t, bool v = false) { Text = t; c = v; Cursor = Cursors.Hand; }
  protected override void OnClick(EventArgs e) { if (!Enabled) return; Checked = !Checked; Changed?.Invoke(this, e); }
  protected override void OnPaint(PaintEventArgs e) {
    var g = e.Graphics; g.Clear(Parent.BackColor); int s = T.S(18); var b = new Rectangle(0, (Height - s) / 2, s, s);
    if (c) { T.Fill(g, b, Enabled ? T.Acc : T.Btn); T.Text(g, "\uE73E", T.IcoS, b, T.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
    else { T.Fill(g, b, Hot ? C2 : T.Mut); T.Fill(g, Rectangle.Inflate(b, -1, -1), T.Bg); }
    if (Text.Length > 0) T.Text(g, Text, Font, new Rectangle(s + T.S(8), 0, Width - s - T.S(8), Height), Enabled ? T.Fg : T.Mut);
  }
  static Color C2 = Color.FromArgb(200, 200, 200);
  public int PreferredW => s() + TextRenderer.MeasureText(Text, Font).Width + T.S(14); int s() => T.S(18);
}
public class FTabs : Base {
  public string[] Items; int sel; public int Selected { get => sel; set { sel = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); } } public event EventHandler Changed;
  public FTabs(params string[] it) { Items = it; BackColor = T.Band; }
  Rectangle R(int i) { int x = T.S(12); for (int k = 0; k < i; k++) x += TextRenderer.MeasureText(Items[k], Font).Width + T.S(26); return new Rectangle(x, 0, TextRenderer.MeasureText(Items[i], Font).Width, Height); }
  protected override void OnMouseClick(MouseEventArgs e) { for (int i = 0; i < Items.Length; i++) if (R(i).Contains(e.Location) || Rectangle.Inflate(R(i), T.S(10), 0).Contains(e.Location)) Selected = i; }
  protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(BackColor); for (int i = 0; i < Items.Length; i++) { var r = R(i); T.Text(e.Graphics, Items[i], Font, r, i == sel ? T.Fg : T.Mut, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding); if (i == sel) using (var b = new SolidBrush(T.Acc)) e.Graphics.FillRectangle(b, r.X, r.Bottom - T.S(10), r.Width, T.S(2)); } }
}
public class FCombo : ComboBox {
  public FCombo(params string[] items) { DropDownStyle = ComboBoxStyle.DropDownList; FlatStyle = FlatStyle.Flat; DrawMode = DrawMode.OwnerDrawFixed; BackColor = T.Btn; ForeColor = T.Fg; Font = T.UI; ItemHeight = T.S(22); Items.AddRange(items); if (items.Length > 0) SelectedIndex = 0; }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Shell.SetWindowTheme(Handle, "DarkMode_CFD", null); }
  protected override void OnDrawItem(DrawItemEventArgs e) { if (e.Index < 0) return; bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0; using (var b = new SolidBrush(sel ? T.Acc : T.Btn)) e.Graphics.FillRectangle(b, e.Bounds); T.Text(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), T.Fg); }
}
public class Header : Label { public Header(string t) { Text = t; Font = T.UIb; ForeColor = T.Fg; AutoSize = true; BackColor = Color.Transparent; } }
public class DarkColors : ProfessionalColorTable {
  public override Color ToolStripDropDownBackground => T.Card; public override Color MenuItemSelected => T.Btn; public override Color MenuItemBorder => T.Btn; public override Color MenuBorder => T.Line;
  public override Color ImageMarginGradientBegin => T.Card; public override Color ImageMarginGradientMiddle => T.Card; public override Color ImageMarginGradientEnd => T.Card; public override Color SeparatorDark => T.Line; public override Color SeparatorLight => T.Line;
  public override Color MenuItemSelectedGradientBegin => T.Btn; public override Color MenuItemSelectedGradientEnd => T.Btn; public override Color CheckBackground => T.Acc; public override Color CheckSelectedBackground => T.Acc;
}
public class DarkRenderer : ToolStripProfessionalRenderer { public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; } protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? T.Fg : T.Mut; base.OnRenderItemText(e); } protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = T.Fg; base.OnRenderArrow(e); } }
public class DarkMenu : ContextMenuStrip { public DarkMenu() { Renderer = new DarkRenderer(); Font = T.UI; ShowImageMargin = true; } public ToolStripMenuItem Add(string t, Action a, bool chk = false) { var i = new ToolStripMenuItem(t) { Checked = chk }; i.Click += (s, e) => a(); Items.Add(i); return i; } }
public class DarkForm : Form {
  public DarkForm() { BackColor = T.Bg; ForeColor = T.Fg; Font = T.UI; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Shell.Dark(Handle); }
}
}

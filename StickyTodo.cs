// StickyTodo - a minimalist always-on-top-able to-do sticky note.
// Build: build.cmd (compiles, writes StickyTodo.ico from the app's own icon drawing, then embeds it)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Shown by Task Manager (Startup apps / Processes) and Settings > Apps
[assembly: System.Reflection.AssemblyTitle("Sticky Todo")]
[assembly: System.Reflection.AssemblyProduct("Sticky Todo")]
[assembly: System.Reflection.AssemblyCompany("Sticky Todo")]
[assembly: System.Reflection.AssemblyVersion("1.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.1.0")]

// Screen edge the note docks to with auto-hide on (the order matches SnapToEdge's gap array)
enum DockEdge { Left, Right, Top, Bottom }

class TodoItem
{
    public long Created;   // DateTime ticks
    public bool Done;
    public bool Urgent;
    public long Remind;    // DateTime ticks, 0 = no reminder
    public bool Notified;
    public string Text;
    public string Notes = "";
}

// Time-based ease-out animation of a single value, independent of frame rate
class Tween
{
    float from, to;
    double duration;
    readonly Stopwatch clock = new Stopwatch();

    public bool Running { get { return clock.IsRunning; } }

    public void Start(float from, float to, double ms)
    {
        this.from = from;
        this.to = to;
        duration = ms;
        clock.Restart();
    }

    public void Stop() { clock.Reset(); }

    public float Value()
    {
        double t = clock.Elapsed.TotalMilliseconds / duration;
        if (t >= 1) { clock.Reset(); return to; }
        double eased = 1 - Math.Pow(1 - t, 3);
        return (float)(from + (to - from) * eased);
    }
}

class Theme
{
    public string Name, Label;
    public bool IsDark;
    public Color Bg, Surface, Hover, Menu, Text, Muted, Border, Accent, OnAccent, Alert;

    static Color Hex(string h) { return ColorTranslator.FromHtml(h); }

    public static readonly Theme Light = new Theme
    {
        Name = "light", Label = "Light",
        Bg = Hex("#FAFAF9"), Surface = Hex("#EEEEEC"), Hover = Hex("#F0F0EE"), Menu = Hex("#FFFFFF"),
        Text = Hex("#1C1C1E"), Muted = Hex("#8C8C90"), Border = Hex("#DEDEDB"),
        Accent = Hex("#3B6FE0"), OnAccent = Color.White, Alert = Hex("#E5484D")
    };
    public static readonly Theme Dark = new Theme
    {
        Name = "dark", Label = "Dark", IsDark = true,
        Bg = Hex("#1F2023"), Surface = Hex("#2C2D32"), Hover = Hex("#27282C"), Menu = Hex("#2C2D32"),
        Text = Hex("#EDEDEF"), Muted = Hex("#8B8D94"), Border = Hex("#3A3B41"),
        Accent = Hex("#6E9BFF"), OnAccent = Hex("#0E1424"), Alert = Hex("#FF6369")
    };
    public static readonly Theme Sticky = new Theme
    {
        Name = "sticky", Label = "Sticky",
        Bg = Hex("#FFF6CC"), Surface = Hex("#FBEBA6"), Hover = Hex("#FDF0B6"), Menu = Hex("#FFFAE3"),
        Text = Hex("#3A3320"), Muted = Hex("#A3955E"), Border = Hex("#EEDC92"),
        Accent = Hex("#D99A00"), OnAccent = Color.White, Alert = Hex("#D9473B")
    };
    public static readonly Theme[] All = { Light, Dark, Sticky };
}

static class UI
{
    public static float S = 1f;
    public static Theme T = Theme.Light;
    public static Font Body, BodyStrike, Title, Small, Button, Icon, IconSmall;

    public static int D(float v) { return (int)Math.Round(v * S); }

    static string[] installed;

    // Fonts are sized in pixels so they follow the monitor the note is on, not the system DPI
    public static void Init(float scale)
    {
        S = scale;
        if (installed == null) installed = new InstalledFontCollection().Families.Select(f => f.Name).ToArray();
        Func<string, string, string> pick = (want, fallback) => installed.Contains(want) ? want : fallback;
        Func<string, float, FontStyle, Font> make = (family, pt, style) => new Font(family, pt * 96f / 72f * scale, style, GraphicsUnit.Pixel);
        string text = pick("Segoe UI Variable Text", "Segoe UI");
        string display = pick("Segoe UI Variable Display Semib", "Segoe UI Semibold");
        string small = pick("Segoe UI Variable Small", "Segoe UI");
        string icons = pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
        Body = make(text, 10.5f, FontStyle.Regular);
        BodyStrike = make(text, 10.5f, FontStyle.Strikeout);
        Title = make(display, 12f, FontStyle.Regular);
        Small = make(small, 8.5f, FontStyle.Regular);
        Button = make(text, 9.5f, FontStyle.Regular);
        Icon = make(icons, 10f, FontStyle.Regular);
        IconSmall = make(icons, 8f, FontStyle.Regular);
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using (GraphicsPath p = Round(r, rad))
        using (SolidBrush b = new SolidBrush(c))
            g.FillPath(b, p);
    }

    public static Color Mix(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static void Glyph(Graphics g, string glyph, Font f, Rectangle r, Color c)
    {
        TextRenderer.DrawText(g, glyph, f, r, c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    public static Bitmap GlyphImage(string glyph, Color c)
    {
        int s = D(16);
        Bitmap bmp = new Bitmap(s, s);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(glyph, IconSmall, b, new RectangleF(0, 0, s, s), sf);
        }
        return bmp;
    }

    public static ContextMenuStrip Menu()
    {
        ContextMenuStrip m = new ContextMenuStrip { Renderer = new MenuRenderer(), Font = Button, ShowImageMargin = true };
        m.Padding = new Padding(0, D(4), 0, D(4));
        m.HandleCreated += delegate { Dwm.Apply(m.Handle, 3); };
        m.Opening += delegate { m.BackColor = T.Menu; if (m.IsHandleCreated) Dwm.Apply(m.Handle, 3); };
        m.Opened += delegate { OpenMenus++; };
        m.Closed += delegate { OpenMenus = Math.Max(0, OpenMenus - 1); };
        return m;
    }

    public static int OpenMenus;   // auto-hide stays out while a menu is up

    public static ToolStripMenuItem MenuItem(ContextMenuStrip m, string text, string glyph, bool danger, EventHandler click)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(text, GlyphImage(glyph, danger ? T.Alert : T.Muted), click);
        item.Padding = new Padding(0, D(3), D(8), D(3));
        if (danger) item.Tag = "danger";
        m.Items.Add(item);
        return item;
    }
}

static class Dwm
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static void Set(IntPtr h, int attr, int value) { try { DwmSetWindowAttribute(h, attr, ref value, 4); } catch { } }
    static int Ref(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

    // corner: 0 = leave default, 2 = round, 3 = small round (Windows 11)
    public static void Apply(IntPtr h, int corner)
    {
        Theme t = UI.T;
        Set(h, 20, t.IsDark ? 1 : 0);      // immersive dark mode
        if (corner > 0) Set(h, 33, corner);
        Set(h, 34, Ref(t.Border));       // border color
        Set(h, 35, Ref(t.Bg));           // caption color
        Set(h, 36, Ref(t.Text));         // caption text color
    }
}

class MenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(UI.T.Menu); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        UI.FillRound(e.Graphics, UI.Mix(UI.T.Menu, UI.T.Text, 0.08f),
            new RectangleF(UI.D(5), 1, e.Item.Width - UI.D(10), e.Item.Height - 2), UI.D(5));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? UI.T.Muted : (e.Item.Tag as string) == "danger" ? UI.T.Alert : UI.T.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using (Pen p = new Pen(UI.T.Border))
            e.Graphics.DrawLine(p, UI.D(10), y, e.Item.Width - UI.D(10), y);
    }
}

// Flat icon button with a soft rounded hover background
class IconButton : Control
{
    public string Glyph;
    public bool Active, AccentWhenActive;
    public bool Dots;   // draw three small hollow circles instead of a font glyph
    public string Label = "";   // shown in the springy hint pill on hover
    bool hover;

    public IconButton(string glyph)
    {
        Glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(UI.D(30), UI.D(30));
        Cursor = Cursors.Hand;
        // Click is raised on press-then-release ourselves: WinForms' own Click is dropped when
        // the button's tooltip happens to be under the pointer
        SetStyle(ControlStyles.StandardClick, false);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    bool pressed;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) pressed = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool click = pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        pressed = false;
        if (click) OnClick(EventArgs.Empty);
    }

    // Lets OptionsColumn, which draws these buttons itself, trigger their Click handlers
    public void PerformClick() { OnClick(EventArgs.Empty); }

    public Color GlyphColor(bool hovered)
    {
        return Active ? (AccentWhenActive ? UI.T.Accent : UI.T.Text) : (hovered ? UI.T.Text : UI.T.Muted);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(UI.T.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (hover) UI.FillRound(g, UI.T.Surface, new RectangleF(UI.D(2), UI.D(2), Width - UI.D(4), Height - UI.D(4)), UI.D(6));
        Color c = GlyphColor(hover);
        if (Dots)
        {
            float d = 4.6f * UI.S, gap = 2.6f * UI.S;
            float x = (Width - (3 * d + 2 * gap)) / 2f, y = (Height - d) / 2f;
            using (Pen p = new Pen(c, 1.3f * UI.S))
                for (int i = 0; i < 3; i++)
                    g.DrawEllipse(p, x + i * (d + gap), y, d, d);
        }
        else UI.Glyph(g, Glyph, UI.Icon, ClientRectangle, c);
    }
}

// Rounded pill button for the reminder dialog
class PillButton : Control
{
    readonly bool primary;
    readonly Font font;
    bool hover;

    public PillButton(string text, bool primary, bool small)
    {
        this.primary = primary;
        font = small ? UI.Small : UI.Button;
        Text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(TextRenderer.MeasureText(text, font).Width + UI.D(small ? 18 : 28), UI.D(small ? 28 : 32));
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(UI.T.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool accent = primary && Enabled;
        Color fill = accent
            ? (hover ? UI.Mix(UI.T.Accent, UI.T.Text, 0.12f) : UI.T.Accent)
            : (hover && Enabled ? UI.Mix(UI.T.Surface, UI.T.Text, 0.07f) : UI.T.Surface);
        UI.FillRound(g, fill, new RectangleF(0, 0, Width - 1, Height - 1), UI.D(7));
        TextRenderer.DrawText(g, Text, font, ClientRectangle, accent ? UI.T.OnAccent : Enabled ? UI.T.Text : UI.T.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

// Rounded field that hosts a borderless TextBox
class RoundBox : Panel
{
    public readonly TextBox Box;
    public bool ShowPlus = true, Error;
    Label placeholder;   // multi-line TextBoxes don't support cue banners, so draw our own

    public RoundBox() : this(false) { }

    public RoundBox(bool multiline)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = new TextBox { BorderStyle = BorderStyle.None, Font = UI.Body };
        if (multiline)
        {
            Box.Multiline = true;
            Box.AcceptsReturn = true;
            Box.WordWrap = true;
            Box.ScrollBars = ScrollBars.None;
        }
        Box.GotFocus += delegate { Invalidate(); };
        Box.LostFocus += delegate { Invalidate(); };
        Box.TextChanged += delegate
        {
            if (Error) { Error = false; Invalidate(); }
            if (placeholder != null) placeholder.Visible = Box.TextLength == 0;
        };
        Controls.Add(Box);
        Click += delegate { Box.Focus(); };
        Cursor = Cursors.IBeam;
    }

    public string Placeholder
    {
        set
        {
            if (placeholder == null)
            {
                placeholder = new Label { AutoSize = false, UseMnemonic = false, Cursor = Cursors.IBeam };
                placeholder.Click += delegate { Box.Focus(); };
                Controls.Add(placeholder);
                placeholder.BringToFront();
            }
            placeholder.Text = value;
            ApplyTheme();
            PerformLayout();
        }
    }

    public void ApplyTheme()
    {
        Box.BackColor = UI.T.Surface;
        Box.ForeColor = UI.T.Text;
        if (placeholder != null)
        {
            placeholder.BackColor = UI.T.Surface;
            placeholder.ForeColor = UI.T.Muted;
            placeholder.Font = Box.Font;
            placeholder.Visible = Box.TextLength == 0;
        }
        Invalidate();
    }

    // Height needed to show all of the text if the field is the given width
    public int PreferredBoxHeight(int width)
    {
        int w = Math.Max(1, width - (ShowPlus ? UI.D(36) : UI.D(10)) - UI.D(10));
        int th = TextRenderer.MeasureText(Box.Text.Length == 0 ? "Ag" : Box.Text + "​", Box.Font,
            new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
        return Math.Max(UI.D(36), th + UI.D(16));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int left = ShowPlus ? UI.D(36) : UI.D(10);
        if (Box.Multiline) Box.SetBounds(left, UI.D(8), Width - left - UI.D(10), Height - UI.D(14));
        else
        {
            Box.Location = new Point(left, (Height - Box.Height) / 2);
            Box.Width = Width - left - UI.D(10);
        }
        if (placeholder != null)
            placeholder.SetBounds(Box.Left, Box.Top, Box.Width, TextRenderer.MeasureText("Ag", Box.Font).Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(UI.T.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        UI.FillRound(g, UI.T.Surface, r, UI.D(8));
        if (Box.Focused || Error)
            using (GraphicsPath p = UI.Round(r, UI.D(8)))
            using (Pen pen = new Pen(Error ? UI.T.Alert : UI.T.Accent, 1.5f * UI.S))
                g.DrawPath(pen, p);
        if (ShowPlus)
            UI.Glyph(g, "\uE710", UI.IconSmall, new Rectangle(UI.D(8), 0, UI.D(24), Height), Box.Focused ? UI.T.Accent : UI.T.Muted);
    }
}

// Inline editor for a new or existing task: the task text, with a smaller "Notes" line under it
// (like iOS Reminders). Enter saves, Shift+Enter adds a line break, Esc cancels, clicking away saves.
class TaskEditor : Control
{
    const TextFormatFlags Measure = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
    public readonly TextBox Title, Notes;
    readonly Label titleHint, notesHint;
    public readonly bool IsNew;
    public bool Urgent;
    public Action<bool, bool> Finished;   // (save, open another new task)
    public event EventHandler ContentChanged;
    bool finished;

    public TaskEditor(bool isNew)
    {
        IsNew = isNew;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Margin = Padding.Empty;
        Cursor = Cursors.IBeam;
        Title = MakeBox();
        Notes = MakeBox();
        titleHint = MakeHint(Title, isNew ? "New task" : "Task");
        notesHint = MakeHint(Notes, "Notes");

        Title.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Finish(true, IsNew); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false, false); }
            else if (e.KeyCode == Keys.Down && Title.SelectionStart == Title.TextLength)
            {
                e.SuppressKeyPress = true;
                Notes.Focus();
                Notes.SelectionStart = Notes.TextLength;
            }
        };
        Notes.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Finish(true, IsNew); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false, false); }
            else if ((e.KeyCode == Keys.Up && Notes.SelectionStart == 0) || (e.KeyCode == Keys.Back && Notes.TextLength == 0))
            {
                e.SuppressKeyPress = true;
                Title.Focus();
                Title.SelectionStart = Title.TextLength;
            }
        };
        // Clicking anywhere outside both fields saves
        EventHandler lost = delegate
        {
            Invalidate();
            BeginInvoke((Action)delegate { if (!IsDisposed && !Title.Focused && !Notes.Focused) Finish(true, false); });
        };
        Title.LostFocus += lost;
        Notes.LostFocus += lost;
        Title.GotFocus += delegate { UpdateHints(); Invalidate(); };
        Notes.GotFocus += delegate { UpdateHints(); Invalidate(); };
        Title.LostFocus += delegate { UpdateHints(); };
        Notes.LostFocus += delegate { UpdateHints(); };
        // Pressing anywhere in the editor focuses the nearer line: the task text, or the notes below it
        MouseDown += delegate(object s, MouseEventArgs e)
        {
            FocusBox(e.Y >= Title.Bottom ? Notes : Title);
        };
        ApplyTheme();
    }

    // Placeholders sit on top of their (empty) text box, so hide them while it has focus,
    // otherwise they cover the text cursor
    void UpdateHints()
    {
        if (titleHint == null || notesHint == null) return;
        titleHint.Visible = Title.TextLength == 0 && !Title.Focused;
        notesHint.Visible = Notes.TextLength == 0 && !Notes.Focused;
    }

    void FocusBox(TextBox box)
    {
        box.Focus();
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
    }

    TextBox MakeBox()
    {
        TextBox b = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = true,
            ScrollBars = ScrollBars.None
        };
        b.TextChanged += delegate
        {
            UpdateHints();
            PerformLayout();
            if (ContentChanged != null) ContentChanged(this, EventArgs.Empty);
        };
        Controls.Add(b);
        return b;
    }

    Label MakeHint(TextBox box, string text)
    {
        Label l = new Label { Text = text, AutoSize = false, UseMnemonic = false, Cursor = Cursors.IBeam };
        l.MouseDown += delegate { FocusBox(box); };   // on press, so it can't be lost like a Click
        Controls.Add(l);
        l.BringToFront();
        return l;
    }

    void Finish(bool save, bool another)
    {
        if (finished) return;
        finished = true;
        if (Finished != null) Finished(save, another);
    }

    public void ApplyTheme()
    {
        Theme t = UI.T;
        Title.Font = UI.Body;
        Notes.Font = UI.Small;
        Title.BackColor = Notes.BackColor = t.Surface;
        Title.ForeColor = Urgent ? t.Alert : t.Text;
        Notes.ForeColor = t.Muted;
        foreach (Label l in new[] { titleHint, notesHint })
        {
            l.BackColor = t.Surface;
            l.ForeColor = UI.Mix(t.Muted, t.Surface, 0.25f);
        }
        titleHint.Font = UI.Body;
        notesHint.Font = UI.Small;
        UpdateHints();
        Invalidate();
    }

    static int TextWidth(int width) { return Math.Max(UI.D(40), width - UI.D(44) - UI.D(16)); }

    static int BoxHeight(TextBox b, Font f, int w)
    {
        int line = TextRenderer.MeasureText("Ag", f, new Size(int.MaxValue, int.MaxValue), Measure).Height;
        if (b.TextLength == 0) return line + UI.D(2);
        // A trailing line break still occupies a line in the TextBox
        int h = TextRenderer.MeasureText(b.Text + "​", f, new Size(w, int.MaxValue), Measure).Height;
        return Math.Max(line, h) + UI.D(2);
    }

    public int PreferredHeight(int width)
    {
        int w = TextWidth(width);
        return UI.D(9) + BoxHeight(Title, UI.Body, w) + UI.D(2) + BoxHeight(Notes, UI.Small, w) + UI.D(9);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Title == null || Notes == null || titleHint == null || notesHint == null) return;   // still being built
        int w = TextWidth(Width), x = UI.D(44);
        Title.SetBounds(x, UI.D(9), w, BoxHeight(Title, UI.Body, w));
        Notes.SetBounds(x, Title.Bottom + UI.D(2), w, BoxHeight(Notes, UI.Small, w));
        titleHint.SetBounds(x, Title.Top, w, Title.Height);
        // The notes hint spans the whole notes line, down to the editor's bottom edge
        notesHint.SetBounds(x, Notes.Top, w, Math.Max(Notes.Height, Height - Notes.Top - UI.D(4)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme t = UI.T;
        Graphics g = e.Graphics;
        g.Clear(t.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(UI.D(4), UI.D(2), Width - UI.D(8), Height - UI.D(4));
        UI.FillRound(g, t.Surface, r, UI.D(8));
        if (Title.Focused || Notes.Focused)
            using (GraphicsPath p = UI.Round(r, UI.D(8)))
            using (Pen pen = new Pen(t.Accent, 1.5f * UI.S))
                g.DrawPath(pen, p);

        // Plus for a new task, the task's (inactive) circle when editing one
        int lineH = TextRenderer.MeasureText("Ag", UI.Body).Height, s = UI.D(18);
        Rectangle cr = new Rectangle(UI.D(14), UI.D(9) + (lineH - s) / 2, s, s);
        if (IsNew) UI.Glyph(g, "", UI.IconSmall, cr, Title.Focused || Notes.Focused ? t.Accent : t.Muted);
        else
        {
            float inset = 0.75f * UI.S;
            using (Pen p = new Pen(Urgent ? t.Alert : t.Muted, 1.5f * UI.S))
                g.DrawEllipse(p, cr.X + inset, cr.Y + inset, cr.Width - 2 * inset, cr.Height - 2 * inset);
        }
    }
}

// One to-do row, fully custom painted
class TaskRow : Control
{
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
    public readonly TodoItem Item;
    public event EventHandler ToggleClicked, DeleteClicked, EditClicked;
    bool hover, hoverDel, hoverCheck, selected;

    // Keyboard selection (arrow keys): drawn as a tinted row with an accent outline
    public bool Selected
    {
        get { return selected; }
        set { if (selected != value) { selected = value; Invalidate(); } }
    }
    int textW, textH, lineH, notesH;

    public TaskRow(TodoItem it, int width)
    {
        Item = it;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);   // clicking a row must not pull focus from an open editor
        Margin = Padding.Empty;
        Cursor = Cursors.IBeam;
        Width = width;
        lineH = TextRenderer.MeasureText("Ag", TextFont, new Size(int.MaxValue, int.MaxValue), Flags).Height;
        textW = Width - UI.D(44) - UI.D(38);
        textH = Math.Max(lineH, TextRenderer.MeasureText(Item.Text, TextFont, new Size(textW, int.MaxValue), Flags).Height);
        notesH = HasNotes ? TextRenderer.MeasureText(Item.Notes, UI.Small, new Size(textW, int.MaxValue), Flags).Height + UI.D(2) : 0;
        Height = textH + notesH + (HasReminder ? UI.D(19) : 0) + UI.D(18);
    }

    Font TextFont { get { return Item.Done ? UI.BodyStrike : UI.Body; } }
    bool Urgent { get { return Item.Urgent && !Item.Done; } }
    bool HasReminder { get { return Item.Remind > 0 && !Item.Done; } }
    bool HasNotes { get { return !string.IsNullOrEmpty(Item.Notes); } }
    int Top0 { get { return UI.D(9); } }

    Rectangle CheckRect
    {
        get { int s = UI.D(18); return new Rectangle(UI.D(14), Top0 + (lineH - s) / 2, s, s); }
    }

    Rectangle DelRect
    {
        get { int s = UI.D(28); return new Rectangle(Width - s - UI.D(8), Math.Max(UI.D(3), Top0 + (lineH - s) / 2), s, s); }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = hoverDel = hoverCheck = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    Rectangle CheckHitRect
    {
        get { Rectangle r = CheckRect; r.Inflate(UI.D(7), UI.D(7)); return r; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool d = DelRect.Contains(e.Location), c = CheckHitRect.Contains(e.Location);
        if (d != hoverDel || c != hoverCheck) { hoverDel = d; hoverCheck = c; Invalidate(); }
        Cursor = d || c ? Cursors.Hand : Cursors.IBeam;
        base.OnMouseMove(e);
    }

    // Circle completes, bin deletes, anywhere else edits the task
    // Our own press-then-release click: WinForms' Click also requires WindowFromPoint to be this row,
    // which fails when the tooltip happens to sit under the pointer
    bool pressed;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) pressed = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool click = pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        pressed = false;
        if (click) HandleClick(e.Location);
    }

    void HandleClick(Point p)
    {
        MouseEventArgs e = new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0);
        EventHandler h = DelRect.Contains(e.Location) ? DeleteClicked
            : CheckHitRect.Contains(e.Location) ? ToggleClicked
            : EditClicked;
        if (h != null) h(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme t = UI.T;
        Graphics g = e.Graphics;
        g.Clear(t.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        RectangleF rowRect = new RectangleF(UI.D(4), UI.D(1), Width - UI.D(8), Height - UI.D(2));
        if (selected)
        {
            UI.FillRound(g, UI.Mix(t.Hover, t.Accent, 0.12f), rowRect, UI.D(8));
            RectangleF edge = new RectangleF(rowRect.X + 0.75f * UI.S, rowRect.Y + 0.75f * UI.S, rowRect.Width - 1.5f * UI.S, rowRect.Height - 1.5f * UI.S);
            using (GraphicsPath p = UI.Round(edge, UI.D(8)))
            using (Pen pen = new Pen(Color.FromArgb(170, t.Accent), 1.3f * UI.S))
                g.DrawPath(pen, p);
        }
        else if (hover) UI.FillRound(g, t.Hover, rowRect, UI.D(8));

        // Round checkbox
        Rectangle cr = CheckRect;
        float inset = 0.75f * UI.S;
        RectangleF circle = new RectangleF(cr.X + inset, cr.Y + inset, cr.Width - 2 * inset, cr.Height - 2 * inset);
        if (Item.Done)
        {
            using (SolidBrush b = new SolidBrush(t.Accent)) g.FillEllipse(b, circle);
            DrawCheck(g, cr, t.OnAccent);
        }
        else
        {
            Color ring = hoverCheck ? t.Accent : Urgent ? t.Alert : t.Muted;
            using (Pen p = new Pen(ring, 1.5f * UI.S)) g.DrawEllipse(p, circle);
            if (hoverCheck) DrawCheck(g, cr, Color.FromArgb(150, t.Accent));
        }

        // Urgent tasks are marked by red text (and ring) alone
        int x = UI.D(44);
        TextRenderer.DrawText(g, Item.Text, TextFont, new Rectangle(x, Top0, textW, textH), Item.Done ? t.Muted : Urgent ? t.Alert : t.Text, Flags);

        // Notes, small and grey under the task
        if (HasNotes)
            TextRenderer.DrawText(g, Item.Notes, UI.Small, new Rectangle(x, Top0 + textH + UI.D(2), textW, notesH), t.Muted, Flags);

        // Reminder time on its own line
        int y = Top0 + textH + notesH + UI.D(2), h = UI.D(16);
        if (HasReminder)
        {
            DateTime at = new DateTime(Item.Remind);
            bool due = at <= DateTime.Now;
            DrawMeta(g, x, y, h, "\uE823", StickyForm.FormatWhen(at) + (due ? " \u00B7 due" : ""), due ? t.Alert : t.Muted);
        }

        if (hover)
        {
            Rectangle dr = DelRect;
            if (hoverDel) UI.FillRound(g, t.Surface, dr, UI.D(6));
            UI.Glyph(g, "\uE74D", UI.IconSmall, dr, hoverDel ? t.Alert : t.Muted);
        }
    }

    static void DrawCheck(Graphics g, Rectangle r, Color c)
    {
        float s = r.Width / 18f;
        using (Pen p = new Pen(c, 1.7f * UI.S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(p, new[] {
                new PointF(r.X + 5.2f * s, r.Y + 9.3f * s),
                new PointF(r.X + 7.9f * s, r.Y + 12f * s),
                new PointF(r.X + 13f * s, r.Y + 6.4f * s) });
    }

    static int DrawMeta(Graphics g, int x, int y, int h, string glyph, string text, Color c)
    {
        UI.Glyph(g, glyph, UI.IconSmall, new Rectangle(x, y, UI.D(14), h), c);
        x += UI.D(17);
        Size sz = TextRenderer.MeasureText(text, UI.Small, new Size(int.MaxValue, h), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, text, UI.Small, new Rectangle(x, y, sz.Width + 2, h), c,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return x + sz.Width + UI.D(12);
    }
}

// The gear's options as one self-drawn window: a rounded column whose icons sit from the bottom up.
// Growing it is a single window resize, which keeps the animation at the display's refresh rate.
class OptionsColumn : Control
{
    public IconButton[] Items = new IconButton[0];
    public Action<Graphics, Rectangle> PaintBackdrop;
    public Action<int> HoverChanged;   // index of the hovered option, -1 for none (or its state changed)
    int hoverIndex = -1;

    public OptionsColumn()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    int Pad { get { return UI.D(3); } }
    int Cell { get { return UI.D(30); } }
    public Rectangle ItemRect(int i) { return new Rectangle(Pad, Height - Pad - (i + 1) * Cell, Cell, Cell); }

    int HitTest(Point p)
    {
        for (int i = 0; i < Items.Length; i++)
            if (ItemRect(i).Contains(p) && ItemRect(i).Top >= 0) return i;
        return -1;
    }

    void SetHover(int i)
    {
        if (i == hoverIndex) return;
        hoverIndex = i;
        if (HoverChanged != null) HoverChanged(i);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) { SetHover(HitTest(e.Location)); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { SetHover(-1); base.OnMouseLeave(e); }

    // Press-then-release click (see TaskRow: WinForms' Click can be swallowed when a tooltip is under the pointer)
    int pressedIndex = -1;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        pressedIndex = e.Button == MouseButtons.Left ? HitTest(e.Location) : -1;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i = HitTest(e.Location);
        bool click = e.Button == MouseButtons.Left && i >= 0 && i == pressedIndex;
        pressedIndex = -1;
        if (!click) return;
        Items[i].PerformClick();
        if (!IsDisposed && i == hoverIndex && HoverChanged != null) HoverChanged(i);   // its label may have changed
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (PaintBackdrop != null) PaintBackdrop(g, Bounds); else g.Clear(UI.T.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath p = UI.Round(r, Math.Min(UI.D(9), Height / 2f)))
        {
            using (SolidBrush b = new SolidBrush(UI.T.Bg)) g.FillPath(b, p);
            using (Pen pen = new Pen(UI.T.Border, UI.S)) g.DrawPath(pen, p);
            g.SetClip(p);
        }
        for (int i = 0; i < Items.Length; i++)
        {
            Rectangle ir = ItemRect(i);
            if (ir.Bottom < 0) break;
            bool hot = i == hoverIndex;
            if (hot) UI.FillRound(g, UI.T.Surface, new RectangleF(ir.X + UI.D(2), ir.Y + UI.D(2), ir.Width - UI.D(4), ir.Height - UI.D(4)), UI.D(6));
            UI.Glyph(g, Items[i].Glyph, UI.Icon, ir, Items[i].GlyphColor(hot));
        }
    }
}

// Reports every left/right mouse-button release in the app, before the target window handles it
class MouseUpFilter : IMessageFilter
{
    public Action<IntPtr> OnRelease;

    public bool PreFilterMessage(ref Message m)
    {
        if ((m.Msg == 0x0202 || m.Msg == 0x0205) && OnRelease != null) OnRelease(m.HWnd);   // WM_LBUTTONUP, WM_RBUTTONUP
        return false;
    }
}

// One entry in the guide: an illustration on the left, a title and explanation on the right
class GuideSection : Control
{
    const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
    readonly string title, body;
    readonly Action<Graphics, Rectangle> visual;
    readonly int textX, titleH, bodyH;
    static Font titleFont;

    public static Font TitleFont
    {
        get
        {
            if (titleFont == null || Math.Abs(titleFont.Size - UI.Body.Size) > 0.01f)
                titleFont = new Font(UI.Body.FontFamily, UI.Body.Size, FontStyle.Bold, GraphicsUnit.Pixel);
            return titleFont;
        }
    }

    public GuideSection(string title, string body, Action<Graphics, Rectangle> visual, int width)
    {
        this.title = title;
        this.body = body;
        this.visual = visual;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Margin = Padding.Empty;
        Width = width;
        textX = UI.D(160);
        int tw = Math.Max(UI.D(80), width - textX - UI.D(22));
        titleH = TextRenderer.MeasureText(title, TitleFont, new Size(tw, int.MaxValue), Wrap).Height;
        bodyH = TextRenderer.MeasureText(body, UI.Button, new Size(tw, int.MaxValue), Wrap).Height;
        Height = Math.Max(UI.D(88), titleH + UI.D(5) + bodyH) + UI.D(26);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme t = UI.T;
        Graphics g = e.Graphics;
        g.Clear(t.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle art = new Rectangle(UI.D(18), UI.D(13), UI.D(128), Height - UI.D(26));
        UI.FillRound(g, t.Surface, art, UI.D(10));
        if (visual != null)
        {
            GraphicsState state = g.Save();
            g.SetClip(art);
            visual(g, art);
            g.Restore(state);
        }
        int tw = Width - textX - UI.D(22), y = UI.D(13);
        TextRenderer.DrawText(g, title, TitleFont, new Rectangle(textX, y, tw, titleH), t.Text, Wrap);
        TextRenderer.DrawText(g, body, UI.Button, new Rectangle(textX, y + titleH + UI.D(5), tw, bodyH), UI.Mix(t.Text, t.Bg, 0.25f), Wrap);
        using (Pen p = new Pen(t.Border)) g.DrawLine(p, UI.D(18), Height - 1, Width - UI.D(18), Height - 1);
    }
}

// Small drawing helpers for the guide's illustrations, all in the current theme
static class GuideArt
{
    // A keyboard key; returns the x just after it
    public static int Key(Graphics g, int x, int y, string label)
    {
        Theme t = UI.T;
        int h = UI.D(24);
        int w = Math.Max(h, TextRenderer.MeasureText(label, UI.Small).Width + UI.D(10));
        UI.FillRound(g, UI.Mix(t.Border, t.Bg, 0.3f), new RectangleF(x, y + UI.D(2), w, h), UI.D(5));   // key side
        UI.FillRound(g, t.Menu, new RectangleF(x, y, w, h), UI.D(5));
        using (GraphicsPath p = UI.Round(new RectangleF(x + 0.5f, y + 0.5f, w - 1, h - 1), UI.D(5)))
        using (Pen pen = new Pen(t.Border)) g.DrawPath(pen, p);
        TextRenderer.DrawText(g, label, UI.Small, new Rectangle(x, y, w, h), t.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        return x + w;
    }

    // Keys side by side (no "+"), centred in the art box at the given row
    public static void KeyRow(Graphics g, Rectangle art, int y, params string[] keys)
    {
        int gap = UI.D(6), total = -gap;
        foreach (string k in keys) total += Math.Max(UI.D(24), TextRenderer.MeasureText(k, UI.Small).Width + UI.D(10)) + gap;
        int x = art.X + (art.Width - total) / 2;
        foreach (string k in keys) x = Key(g, x, y, k) + gap;
    }

    public static int Plus(Graphics g, int x, int y)
    {
        int w = UI.D(14);
        TextRenderer.DrawText(g, "+", UI.Small, new Rectangle(x, y, w, UI.D(24)), UI.T.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return x + w;
    }

    // Keys separated by "+", centred in the art box at the given row
    public static void Combo(Graphics g, Rectangle art, int y, params string[] keys)
    {
        int total = 0;
        foreach (string k in keys) total += Math.Max(UI.D(24), TextRenderer.MeasureText(k, UI.Small).Width + UI.D(10));
        total += (keys.Length - 1) * UI.D(14);
        int x = art.X + (art.Width - total) / 2;
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) x = Plus(g, x, y);
            x = Key(g, x, y, keys[i]);
        }
    }

    public static void Circle(Graphics g, Rectangle r, bool done, Color ring)
    {
        Theme t = UI.T;
        float inset = 0.75f * UI.S;
        RectangleF c = new RectangleF(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
        if (done)
        {
            using (SolidBrush b = new SolidBrush(t.Accent)) g.FillEllipse(b, c);
            float s = r.Width / 18f;
            using (Pen p = new Pen(t.OnAccent, 1.7f * UI.S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(r.X + 5.2f * s, r.Y + 9.3f * s), new PointF(r.X + 7.9f * s, r.Y + 12f * s), new PointF(r.X + 13f * s, r.Y + 6.4f * s) });
        }
        else using (Pen p = new Pen(ring, 1.5f * UI.S)) g.DrawEllipse(p, c);
    }

    // A mini task row: circle + text
    public static void Row(Graphics g, int x, int y, int w, string text, bool done, bool urgent)
    {
        Theme t = UI.T;
        int s = UI.D(14);
        Circle(g, new Rectangle(x, y + UI.D(2), s, s), done, urgent ? t.Alert : t.Muted);
        Font f = done ? UI.Small : UI.Small;
        Color c = done ? t.Muted : urgent ? t.Alert : t.Text;
        TextRenderer.DrawText(g, text, f, new Rectangle(x + s + UI.D(7), y, w - s - UI.D(7), UI.D(18)), c,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (done)
        {
            int tw = Math.Min(w - s - UI.D(7), TextRenderer.MeasureText(text, f).Width - UI.D(4));
            using (Pen p = new Pen(t.Muted, UI.S)) g.DrawLine(p, x + s + UI.D(9), y + UI.D(9), x + s + UI.D(7) + tw, y + UI.D(9));
        }
    }

    // A mini editor box with focus outline
    public static void Editor(Graphics g, Rectangle r, string text, string notes, bool caretInNotes)
    {
        Theme t = UI.T;
        UI.FillRound(g, t.Bg, r, UI.D(7));
        using (GraphicsPath p = UI.Round(new RectangleF(r.X + 0.75f, r.Y + 0.75f, r.Width - 1.5f, r.Height - 1.5f), UI.D(7)))
        using (Pen pen = new Pen(t.Accent, 1.3f * UI.S)) g.DrawPath(pen, p);
        int x = r.X + UI.D(10), y = r.Y + UI.D(7);
        TextRenderer.DrawText(g, text, UI.Small, new Point(x, y), t.Text, TextFormatFlags.NoPrefix);
        int lineH = TextRenderer.MeasureText("Ag", UI.Small).Height;
        if (notes != null)
        {
            Font tiny = new Font(UI.Small.FontFamily, UI.Small.Size * 0.88f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, notes, tiny, new Point(x, y + lineH + UI.D(1)), t.Muted, TextFormatFlags.NoPrefix);
            int cx = caretInNotes ? x + TextRenderer.MeasureText(notes, tiny, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + UI.D(3)
                                  : x + TextRenderer.MeasureText(text, UI.Small, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + UI.D(3);
            int cy = caretInNotes ? y + lineH + UI.D(2) : y + UI.D(1);
            using (Pen p = new Pen(t.Text, UI.S)) g.DrawLine(p, cx, cy, cx, cy + lineH - UI.D(3));
            tiny.Dispose();
        }
        else
        {
            int cx = x + TextRenderer.MeasureText(text, UI.Small, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + UI.D(3);
            using (Pen p = new Pen(t.Text, UI.S)) g.DrawLine(p, cx, y + UI.D(1), cx, y + lineH - UI.D(2));
        }
    }
}

// Double-buffered panel for custom-painted overlays
class PaintPanel : Panel
{
    public PaintPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

// Dialog that sees every key press first (including Alt and Tab combinations) for shortcut capture
class KeyCaptureForm : Form
{
    public Func<Keys, bool> OnKey;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (OnKey != null && OnKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

class StickyForm : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);
    const int WM_SETREDRAW = 0x000B;
    const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1, WM_DPICHANGED = 0x02E0, HTCAPTION = 2, EM_SETCUEBANNER = 0x1501;

    static float systemScale = 1f;
    readonly string dataFile, settingsFile, titleFile;
    List<TodoItem> items = new List<TodoItem>();
    bool newestFirst, hideDone, pinned;

    // Auto-hide: dock to a screen edge, slide away leaving a tab, slide back on hover
    bool autoHide, hidden, dialogOpen, keyOpened;
    DockEdge dockEdge = DockEdge.Right;
    // Saved spot on a monitor that wasn't there at start-up (e.g. still waking at sign-in):
    // kept, and moved back to once that monitor appears, unless the user moves the note first
    Point? wantedLocation;

    // Global toggle shortcut (RegisterHotKey modifier bits + virtual key; 0 = none)
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    const int WM_HOTKEY = 0x0312, HOTKEY_ID = 1, TEST_ID = 2;

    // Single instance: a second launch asks the running copy to show itself, then exits
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    static readonly int ShowNoteMessage = RegisterWindowMessage("StickyTodo.ShowNote");
    static readonly int ShowGuideMessage = RegisterWindowMessage("StickyTodo.ShowGuide");
    static bool openGuideOnStart;   // started with --guide
    static System.Threading.Mutex instanceMutex;

    // The note's window is owned (that's what keeps it off the taskbar), and broadcasts skip owned
    // windows, so the running copy marks its window and a second launch finds it by that marker
    const string InstanceProp = "StickyTodo.Note";
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr hWnd, string name, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hWnd, string name);

    static void ShowRunningInstance(bool guide)
    {
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (GetProp(hWnd, InstanceProp) == IntPtr.Zero) return true;
            PostMessage(hWnd, guide ? ShowGuideMessage : ShowNoteMessage, IntPtr.Zero, IntPtr.Zero);
            return false;
        }, IntPtr.Zero);
    }
    const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    int hotMods, hotKey;
    IconButton keyBtn;

    // Last window the user was in, in any other program, so the shortcut can hand focus back to it
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_SKIPOWNPROCESS = 2;
    WinEventProc foregroundProc;   // kept in a field so the GC doesn't collect the callback
    IntPtr foregroundHook, lastOtherWindow;
    Rectangle dockBounds;
    int targetPos;
    Panel grip;
    IconButton dockBtn;
    Timer hoverTimer;
    Tween slideTween = new Tween();

    // Animation frames are paced by the compositor (DwmFlush), i.e. the monitor refresh rate
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    readonly object frameLock = new object();
    System.Threading.Thread frameThread;
    bool framesWanted;
    DateTime leftAt = DateTime.Now, enterAt = DateTime.MaxValue, holdUntil = DateTime.MinValue;

    Panel header, footer, veil, clip, thumb;
    OptionsColumn column;

    // Hint pill: springs out to the right of a hovered option and shows its label
    PaintPanel hint;
    string hintText = "";
    int hintX, hintTextW;
    float hintW, hintWVel, hintTargetW, hintY, hintYVel, hintTargetY;
    bool hintWanted;
    Bitmap veilSharp, veilBlur;   // snapshot of the list and its blurred copy
    float veilAlpha;
    readonly Stopwatch frameClock = Stopwatch.StartNew();
    double lastFrameMs;
    IconButton gearBtn;
    Tween optionsTween = new Tween();
    Timer optionsWatch;
    DateTime optionsLeftAt;

    string noteTitle = "Tasks";
    RoundBox titleEditor;   // inline field while renaming the note
    bool optionsOpen;
    float optionsSize;
    IconButton pinBtn, sortBtn, doneBtn, themeBtn;
    MouseUpFilter clickWatcher;
    TaskEditor editor;   // inline editor while adding or editing a task
    TodoItem editing;    // the task being edited, or null when adding a new one
    FlowLayoutPanel list;
    ToolTip tip = new ToolTip();
    NotifyIcon tray;
    ContextMenuStrip trayMenu;
    Timer reminderTimer;


    public StickyForm()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickyTodo");
        Directory.CreateDirectory(dir);
        dataFile = Path.Combine(dir, "todos.txt");
        settingsFile = Path.Combine(dir, "settings.txt");
        titleFile = Path.Combine(dir, "title.txt");
        if (File.Exists(titleFile))
        {
            string saved = File.ReadAllText(titleFile).Trim();
            if (saved.Length > 0) noteTitle = saved;
        }

        UI.T = SystemPrefersDark() ? Theme.Dark : Theme.Light;

        Text = "Sticky To-Do";
        Icon = MakeIcon(Math.Max(32, SystemInformation.IconSize.Width));
        ShowInTaskbar = false;   // lives in the system tray only
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        // Header: title + task count; drag to move
        header = new Panel { Dock = DockStyle.Top };
        header.MouseDown += DragWindow;
        header.Paint += PaintHeader;

        // Footer: gear in the bottom-left; clicking it grows a column of options upward over the list,
        // blurring the tasks behind it once the column reaches them
        footer = new Panel { Dock = DockStyle.Bottom };
        footer.MouseDown += DragWindow;
        gearBtn = new IconButton("") { AccentWhenActive = true, Dots = true };
        gearBtn.Label = "Options";
        gearBtn.Click += delegate { ToggleOptions(); };
        footer.Controls.Add(gearBtn);
        column = new OptionsColumn { Visible = false };
        column.PaintBackdrop = PaintColumnBackdrop;
        column.HoverChanged = delegate(int i)
        {
            if (i < 0) { HideHint(); return; }
            Rectangle r = column.ItemRect(i);
            r.Offset(column.Location);
            ShowHint(column.Items[i].Label, r);
        };
        hint = new PaintPanel { Visible = false };
        hint.Paint += PaintHint;
        gearBtn.MouseEnter += delegate
        {
            ShowHint(gearBtn.Label, new Rectangle(footer.Left + gearBtn.Left, footer.Top + gearBtn.Top, gearBtn.Width, gearBtn.Height));
        };
        gearBtn.MouseLeave += delegate { HideHint(); };
        veil = new PaintPanel { Visible = false };
        veil.Paint += PaintVeil;
        veil.MouseDown += delegate { if (optionsOpen) ToggleOptions(); };
        optionsWatch = new Timer { Interval = 25 };
        optionsWatch.Tick += delegate { WatchOptions(); };

        keyBtn = HeaderButton("\uE765", "");
        keyBtn.Click += delegate { EditShortcut(); };
        pinBtn = HeaderButton("\uE718", "");
        pinBtn.AccentWhenActive = true;
        pinBtn.Click += delegate { pinned = !pinned; ApplyTopMost(); UpdateButtons(); SaveSettings(); };
        dockBtn = HeaderButton("\uE90D", "");
        dockBtn.AccentWhenActive = true;
        dockBtn.Click += delegate { SetAutoHide(!autoHide); };
        themeBtn = HeaderButton("\uE790", "");
        themeBtn.Click += delegate { CycleTheme(); };
        sortBtn = HeaderButton("\uE74A", "");
        sortBtn.Click += delegate { newestFirst = !newestFirst; UpdateButtons(); Rebuild(); SaveSettings(); };
        doneBtn = HeaderButton("\uE890", "");
        doneBtn.Click += delegate { hideDone = !hideDone; justCompleted.Clear(); UpdateButtons(); Rebuild(); SaveSettings(); };

        // Task list; the native scrollbar is clipped off and replaced by a slim thumb
        clip = new Panel { Dock = DockStyle.Fill };
        list = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Location = Point.Empty
        };
        thumb = new Panel { Visible = false };
        clip.Controls.Add(thumb);
        clip.Controls.Add(list);
        clip.Resize += delegate
        {
            list.Size = new Size(clip.Width + SystemInformation.VerticalScrollBarWidth, clip.Height);
            UpdateThumb();
        };
        list.Scroll += delegate { UpdateThumb(); };
        list.MouseWheel += delegate { BeginInvoke((Action)UpdateThumb); };

        // Double-click empty space (or the header) to add a task; Ctrl+N also works
        list.DoubleClick += delegate { StartAdding(); };
        clip.DoubleClick += delegate { StartAdding(); };
        tip.SetToolTip(header, "Double-click to rename");

        // Clicking anywhere outside an open editor closes (and saves) it, even on parts of the
        // note that can't take keyboard focus, like the list background or the header
        clickWatcher = new MouseUpFilter { OnRelease = ClickedSomewhere };
        Application.AddMessageFilter(clickWatcher);
        FormClosed += delegate { Application.RemoveMessageFilter(clickWatcher); };
        KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; StartAdding(); }
        };
        // Just start typing: with the note focused and no text field active, characters (including
        // ones committed by an input method) open a new task and go straight into it
        KeyPress += delegate(object s, KeyPressEventArgs e)
        {
            if (ActiveControl is TextBox || dialogOpen) return;   // a field is already taking the text
            if (e.KeyChar == '\r') { e.Handled = true; StartAdding(); return; }
            if (char.IsControl(e.KeyChar)) return;
            e.Handled = true;
            if (editor == null || editing != null) StartAdding();
            if (editor == null) return;
            editor.Title.Focus();
            editor.Title.SelectionStart = editor.Title.TextLength;
            editor.Title.SelectedText = e.KeyChar.ToString();
        };

        // Docked controls lay out in reverse add order
        Controls.Add(clip);
        Controls.Add(footer);
        Controls.Add(header);
        Controls.Add(veil);
        Controls.Add(column);
        Controls.Add(hint);

        // Tab that stays visible at the screen edge while the note is tucked away
        grip = new Panel { Visible = false };
        grip.Paint += PaintGrip;
        Controls.Add(grip);
        hoverTimer = new Timer { Interval = 80 };
        hoverTimer.Tick += delegate { CheckHover(); };

        // Tray icon carries the reminder notifications
        trayMenu = UI.Menu();
        tray = new NotifyIcon { Icon = MakeIcon(SystemInformation.SmallIconSize.Width), Text = "Sticky To-Do", Visible = true, ContextMenuStrip = trayMenu };
        tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowNote(); };
        tray.BalloonTipClicked += delegate { ShowNote(); };

        reminderTimer = new Timer { Interval = 10000 };
        reminderTimer.Tick += delegate { CheckReminders(); if (hidden) grip.Invalidate(); };

        Resize += delegate { LayoutHeader(); LayoutGrip(); };
        ResizeEnd += delegate { wantedLocation = null; if (autoHide) SnapToEdge(); Rebuild(); SaveSettings(); };
        // A monitor was plugged in / woke up / changed layout: go back to a saved spot that was on it
        EventHandler displaysChanged = delegate { if (IsHandleCreated) BeginInvoke((Action)RestoreWantedLocation); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displaysChanged;
        FormClosing += delegate
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displaysChanged;
            UnregisterHotKey(Handle, HOTKEY_ID);
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            SaveSettings();
            reminderTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
        };
        // Hidden (auto-hide off): drop the finished tasks that were kept on show
        VisibleChanged += delegate { if (!Visible) FlushCompleted(); };
        // Opened with the shortcut, then the user went to another window: tuck away (or hide)
        Deactivate += delegate
        {
            if (selectedItem != null && !dialogOpen) SetSelected(null);
            if (!keyOpened || dialogOpen) return;
            BeginInvoke((Action)delegate
            {
                if (!keyOpened || dialogOpen || GetForegroundWindow() == Handle) return;
                keyOpened = false;
                if (optionsOpen) ToggleOptions();
                if (autoHide) { if (!hidden) SlideTo(true); }
                else Hide();
            });
        };
        Shown += delegate
        {
            lastOtherWindow = GetForegroundWindow() == Handle ? IntPtr.Zero : GetForegroundWindow();
            foregroundProc = delegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
            {
                if (hwnd != IntPtr.Zero) lastOtherWindow = hwnd;
            };
            foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundProc, 0, 0, WINEVENT_SKIPOWNPROCESS);
            CheckReminders();
            if (openGuideOnStart) BeginInvoke((Action)ShowGuide);
            reminderTimer.Start();
            ApplyTopMost();
            if (autoHide) { SnapToEdge(); holdUntil = DateTime.Now.AddSeconds(1.5); hoverTimer.Start(); }
            // The shortcut can be briefly busy at sign-in (e.g. a previous instance still exiting),
            // so keep retrying for ~30 s before telling the user
            if (!RegisterToggle())
            {
                int attempts = 0;
                Timer retry = new Timer { Interval = 2000 };
                retry.Tick += delegate
                {
                    attempts++;
                    if (RegisterToggle()) { retry.Dispose(); return; }
                    if (attempts < 15) return;
                    retry.Dispose();
                    tray.ShowBalloonTip(10000, "Shortcut unavailable",
                        FormatHotkey(hotMods, hotKey) + " is already used by another app. Pick a new one from the … menu.",
                        ToolTipIcon.Warning);
                };
                retry.Start();
            }
        };

        LoadItems();
        LoadSettings();
        ApplyTheme();
        ApplyTitle();
    }

    // DPI of the monitor nearest to a screen point (per-monitor aware)
    static float ScaleAt(Point p)
    {
        try
        {
            uint dx, dy;
            if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out dx, out dy) == 0) return dx / 96f;
        }
        catch { }
        return systemScale;
    }

    // Re-create fonts and re-apply every size for a new DPI scale
    void ScaleTo(float scale)
    {
        UI.Init(scale);
        Padding = new Padding(UI.D(4));
        // Tall enough for the options column (6 options) to rise from the dots up over the title bar
        MinimumSize = new Size(UI.D(190), UI.D(236));
        header.Height = UI.D(44);
        footer.Height = UI.D(42);
        foreach (IconButton b in new[] { gearBtn, pinBtn, keyBtn, dockBtn, themeBtn, sortBtn, doneBtn }) b.Size = new Size(UI.D(30), UI.D(30));
        if (optionsOpen) optionsSize = OptionsFullHeight;
        list.Padding = new Padding(UI.D(6), UI.D(2), 0, UI.D(8));
        trayMenu.Font = UI.Button;
        trayMenu.Padding = new Padding(0, UI.D(4), 0, UI.D(4));
        LayoutHeader();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Dwm.Apply(Handle, 2);
        SetProp(Handle, InstanceProp, (IntPtr)1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    // Borderless resize: report edges/corners as resize handles
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
        {
            ToggleNote();
            return;
        }
        if (ShowNoteMessage != 0 && m.Msg == ShowNoteMessage)
        {
            ExpandByKey();   // someone started the app again: bring this copy out instead
            return;
        }
        if (ShowGuideMessage != 0 && m.Msg == ShowGuideMessage)
        {
            ShowGuide();     // started again with --guide
            return;
        }
        if (m.Msg == WM_DPICHANGED && autoHide && (hidden || slideTween.Running))
        {
            // Sliding past the edge onto a neighbouring monitor: keep the current scale
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_DPICHANGED)
        {
            // Moved to a monitor with different scaling: redraw natively at its DPI
            RECT rc = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
            SuspendLayout();
            ScaleTo((m.WParam.ToInt64() & 0xFFFF) / 96f);
            SetBounds(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            ResumeLayout();
            ApplyTheme();
            m.Result = IntPtr.Zero;
            return;
        }
        // No edge resizing: the note sizes itself to its tasks (see AutoSizeNote)
        base.WndProc(ref m);
    }

    static bool SystemPrefersDark()
    {
        try
        {
            object v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return v is int && (int)v == 0;
        }
        catch { return false; }
    }

    // The app icon (blue rounded square, white tick), drawn on a 32-unit grid at any pixel size
    static Bitmap DrawAppIcon(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 32f, size / 32f);
            UI.FillRound(g, ColorTranslator.FromHtml("#3B6FE0"), new RectangleF(2, 2, 28, 28), 7);
            using (Pen pen = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(9.5f, 16.5f), new PointF(14f, 21f), new PointF(22.5f, 11.5f) });
        }
        return bmp;
    }

    static Icon MakeIcon(int size)
    {
        using (Bitmap bmp = DrawAppIcon(size)) return Icon.FromHandle(bmp.GetHicon());
    }

    // Writes the icon at every common size as a .ico (PNG entries); used to embed it in the exe:
    // StickyTodo.exe --write-icon StickyTodo.ico, then compile with /win32icon:StickyTodo.ico
    static void WriteIconFile(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        List<byte[]> images = new List<byte[]>();
        foreach (int s in sizes)
            using (Bitmap b = DrawAppIcon(s))
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                images.Add(ms.ToArray());
            }
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0);               // reserved
            w.Write((short)1);               // type: icon
            w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // 0 means 256
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);            // palette colours
                w.Write((byte)0);            // reserved
                w.Write((short)1);           // colour planes
                w.Write((short)32);          // bits per pixel
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images) w.Write(img);
        }
    }

    void ShowNote()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        if (autoHide)
        {
            holdUntil = DateTime.Now.AddSeconds(4);
            if (hidden) SlideTo(false);
        }
        Activate();
    }

    void ApplyTopMost() { TopMost = pinned || autoHide; }

    // Global shortcut: slide out / tuck away when auto-hide is on, otherwise show / hide the window
    // Shortcut: if the note is tucked away (or out but not focused), bring it out and focus it;
    // if it's out and focused, tuck it away and give focus back to the window that had it
    void ToggleNote()
    {
        bool shown = autoHide ? !hidden : Visible && WindowState != FormWindowState.Minimized;
        if (shown && GetForegroundWindow() == Handle) TuckByKey();
        else ExpandByKey();
    }

    void ExpandByKey()
    {
        keyOpened = true;
        if (autoHide)
        {
            if (hidden) SlideTo(false);
        }
        else
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
        }
        // The hotkey message grants this process the right to take the foreground
        SetForegroundWindow(Handle);
        Activate();
    }

    void TuckByKey()
    {
        keyOpened = false;
        if (editor != null) FinishEditor(true, false);
        if (titleEditor != null) FinishRename(true);
        if (optionsOpen) ToggleOptions();
        if (autoHide) SlideTo(true);
        else Hide();
        IntPtr back = lastOtherWindow;
        if (back != IntPtr.Zero && IsWindow(back) && IsWindowVisible(back)) SetForegroundWindow(back);
    }

    bool RegisterToggle()
    {
        if (!IsHandleCreated) return true;
        UnregisterHotKey(Handle, HOTKEY_ID);
        if (hotKey == 0) return true;
        return RegisterHotKey(Handle, HOTKEY_ID, (uint)(hotMods | MOD_NOREPEAT), (uint)hotKey);
    }

    // True if no other app (or Windows itself) has claimed the combination
    bool HotkeyAvailable(int mods, int key)
    {
        if (!RegisterHotKey(Handle, TEST_ID, (uint)(mods | MOD_NOREPEAT), (uint)key)) return false;
        UnregisterHotKey(Handle, TEST_ID);
        return true;
    }

    static string FormatMods(int mods)
    {
        List<string> parts = new List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    static string FormatHotkey(int mods, int key)
    {
        string m = FormatMods(mods);
        return (m.Length > 0 ? m + " + " : "") + KeyName((Keys)key);
    }

    static string KeyName(Keys k)
    {
        if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
        switch (k)
        {
            case Keys.Space: return "Space";
            case Keys.Return: return "Enter";
            case Keys.Back: return "Backspace";
            case Keys.Prior: return "Page Up";
            case Keys.Next: return "Page Down";
            case Keys.Oemcomma: return ",";
            case Keys.OemPeriod: return ".";
            case Keys.OemMinus: return "-";
            case Keys.Oemplus: return "=";
            case Keys.OemQuestion: return "/";
            case Keys.OemSemicolon: return ";";
            case Keys.OemQuotes: return "'";
            case Keys.OemOpenBrackets: return "[";
            case Keys.OemCloseBrackets: return "]";
            case Keys.OemPipe: return "\\";
            case Keys.Oemtilde: return "`";
        }
        return k.ToString();
    }

    // Shortcuts most apps use; allowed, but a global hotkey would take them over everywhere
    static readonly Dictionary<string, string> CommonShortcuts = new Dictionary<string, string>
    {
        { "Ctrl + A", "Select all" }, { "Ctrl + C", "Copy" }, { "Ctrl + V", "Paste" }, { "Ctrl + X", "Cut" },
        { "Ctrl + Z", "Undo" }, { "Ctrl + Y", "Redo" }, { "Ctrl + S", "Save" }, { "Ctrl + F", "Find" },
        { "Ctrl + N", "New" }, { "Ctrl + O", "Open" }, { "Ctrl + P", "Print" }, { "Ctrl + W", "Close tab" },
        { "Ctrl + T", "New tab" }, { "Ctrl + R", "Reload" }, { "Ctrl + H", "History / Replace" },
        { "Ctrl + B", "Bold" }, { "Ctrl + I", "Italic" }, { "Ctrl + U", "Underline" }, { "Ctrl + L", "Address bar" },
        { "Ctrl + D", "Bookmark" }, { "Ctrl + E", "Search" }, { "Ctrl + K", "Insert link" }, { "Ctrl + J", "Downloads" },
        { "Ctrl + Tab", "Next tab" }, { "Ctrl + Space", "Autocomplete / input switch" },
        { "Ctrl + Shift + T", "Reopen closed tab" }, { "Ctrl + Shift + N", "New private window / folder" },
        { "Ctrl + Shift + V", "Paste as plain text" }, { "Ctrl + Shift + Z", "Redo" },
        { "Ctrl + Shift + Esc", "Task Manager" }, { "Ctrl + Alt + Delete", "Security screen" },
        { "Alt + F4", "Close window" }, { "Alt + Tab", "Switch apps" }, { "Alt + Space", "Window menu" },
        { "Alt + Enter", "Properties / full screen" }, { "Ctrl + F4", "Close document" },
        { "Ctrl + Esc", "Start menu" }, { "Shift + Delete", "Delete permanently" }
    };

    // 0 = fine, 1 = usable with a warning, 2 = blocked
    int EvaluateHotkey(int mods, int key, out string message)
    {
        if (key == 0) { message = "No shortcut — the note is only shown by hovering or the tray icon."; return 0; }
        if ((mods & (MOD_CONTROL | MOD_ALT | MOD_WIN)) == 0)
        {
            message = "Include Ctrl, Alt or Win so it doesn't get in the way of typing.";
            return 2;
        }
        if (!HotkeyAvailable(mods, key))
        {
            message = "Already taken by Windows or another app. Try a different combination.";
            return 2;
        }
        string use;
        if (CommonShortcuts.TryGetValue(FormatHotkey(mods, key), out use))
        {
            message = "Available, but most apps use it for “" + use + "” — it would stop working there while Sticky To-Do runs.";
            return 1;
        }
        message = "Available.";
        return 0;
    }

    void EditShortcut()
    {
        Theme t = UI.T;
        int mods = hotMods, key = hotKey;
        string partial = null;
        if (optionsOpen) ToggleOptions();
        UnregisterHotKey(Handle, HOTKEY_ID);   // so the current combination tests as available

        KeyCaptureForm dlg = new KeyCaptureForm
        {
            Text = "Show/hide shortcut",
            AutoScaleMode = AutoScaleMode.None,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowIcon = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(UI.D(330), UI.D(214)),
            BackColor = t.Bg,
            ForeColor = t.Text,
            Font = UI.Body,
            TopMost = true,
            KeyPreview = true
        };
        dlg.HandleCreated += delegate { Dwm.Apply(dlg.Handle, 0); };

        Label hint = new Label
        {
            Text = "Press the keys you want to use. Esc cancels, Backspace clears.",
            Font = UI.Small,
            ForeColor = t.Muted,
            Location = new Point(UI.D(16), UI.D(12)),
            Size = new Size(UI.D(298), UI.D(18))
        };

        Panel box = new Panel { Location = new Point(UI.D(16), UI.D(36)), Size = new Size(UI.D(298), UI.D(54)), BackColor = t.Bg };
        Label status = new Label
        {
            Font = UI.Small,
            Location = new Point(UI.D(16), UI.D(98)),
            Size = new Size(UI.D(298), UI.D(54)),
            UseMnemonic = false
        };
        PillButton clear = new PillButton("Clear", false, false);
        PillButton cancel = new PillButton("Cancel", false, false);
        PillButton save = new PillButton("Save", true, false);
        int btnY = dlg.ClientSize.Height - save.Height - UI.D(16);
        clear.Location = new Point(UI.D(16), btnY);
        save.Location = new Point(dlg.ClientSize.Width - save.Width - UI.D(16), btnY);
        cancel.Location = new Point(save.Left - cancel.Width - UI.D(8), btnY);

        box.Paint += delegate(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(t.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            UI.FillRound(g, t.Surface, new RectangleF(0, 0, box.Width - 1, box.Height - 1), UI.D(8));
            string text = partial ?? (key == 0 ? "None" : FormatHotkey(mods, key));
            TextRenderer.DrawText(g, text, UI.Title, box.ClientRectangle, partial != null || key == 0 ? t.Muted : t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        Action refresh = delegate
        {
            string msg;
            int state;
            if (partial != null) { state = -1; msg = "Keep holding, then press a key."; }
            else state = EvaluateHotkey(mods, key, out msg);
            status.Text = (state == 2 ? "⛔ " : state == 1 ? "⚠ " : state == 0 && key != 0 ? "✓ " : "") + msg;
            status.ForeColor = state == 2 ? t.Alert : state == 1 ? t.Accent : state == 0 && key != 0 ? t.Text : t.Muted;
            save.Enabled = state == 0 || state == 1;
            box.Invalidate();
        };

        Action trySave = delegate
        {
            if (!save.Enabled) return;
            hotMods = mods;
            hotKey = key;
            dlg.DialogResult = DialogResult.OK;
        };

        dlg.OnKey = delegate(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0, shift = (keyData & Keys.Shift) != 0;
            bool win = GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0;
            bool noMods = !ctrl && !alt && !shift && !win;
            if (noMods && k == Keys.Escape) { dlg.DialogResult = DialogResult.Cancel; return true; }
            if (noMods && k == Keys.Return) { trySave(); return true; }
            if (noMods && (k == Keys.Back || k == Keys.Delete)) { mods = 0; key = 0; partial = null; refresh(); return true; }

            int m = (alt ? MOD_ALT : 0) | (ctrl ? MOD_CONTROL : 0) | (shift ? MOD_SHIFT : 0) | (win ? MOD_WIN : 0);
            bool modifierOnly = k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin;
            if (modifierOnly) partial = FormatMods(m) + " + …";
            else { mods = m; key = (int)k; partial = null; }
            refresh();
            return true;
        };
        dlg.KeyUp += delegate { if (partial != null) { partial = null; refresh(); } };

        clear.Click += delegate { mods = 0; key = 0; partial = null; refresh(); };
        cancel.Click += delegate { dlg.DialogResult = DialogResult.Cancel; };
        save.Click += delegate { trySave(); };

        dlg.Controls.AddRange(new Control[] { hint, box, status, clear, cancel, save });
        refresh();

        dialogOpen = true;
        dlg.ShowDialog(this);
        dialogOpen = false;
        dlg.Dispose();

        if (!RegisterToggle())
            tray.ShowBalloonTip(8000, "Shortcut unavailable", FormatHotkey(hotMods, hotKey) + " is already used by another app.", ToolTipIcon.Warning);
        UpdateButtons();
        SaveSettings();
    }

    void SetAutoHide(bool on)
    {
        autoHide = on;
        ApplyTopMost();
        if (on)
        {
            SnapToEdge();
            hoverTimer.Start();
        }
        else
        {
            hoverTimer.Stop();
            slideTween.Stop();
            if (hidden || DockAxis != ShownPos) DockAxis = ShownPos;
            hidden = false;
            grip.Visible = false;
            SetClip(Rectangle.Empty);
        }
        UpdateButtons();
        SaveSettings();
    }

    int TabWidth { get { return UI.D(10); } }
    bool DockVertical { get { return dockEdge == DockEdge.Top || dockEdge == DockEdge.Bottom; } }

    // Position on the axis the note slides along: X for the side edges, Y for top and bottom
    int DockAxis
    {
        get { return DockVertical ? Top : Left; }
        set { if (DockVertical) Top = value; else Left = value; }
    }

    int ShownPos { get { return DockPos(false, Width, Height); } }
    int HiddenPos { get { return DockPos(true, Width, Height); } }

    int DockPos(bool hide, int w, int h)
    {
        switch (dockEdge)
        {
            case DockEdge.Left: return hide ? dockBounds.Left - w + TabWidth : dockBounds.Left;
            case DockEdge.Top: return hide ? dockBounds.Top - h + TabWidth : dockBounds.Top;
            case DockEdge.Bottom: return hide ? dockBounds.Bottom - TabWidth : dockBounds.Bottom - h;
            default: return hide ? dockBounds.Right - TabWidth : dockBounds.Right - w;
        }
    }

    // Where the note sits when fully out: that's the position that gets saved
    Point ShownLocation { get { return DockVertical ? new Point(Left, ShownPos) : new Point(ShownPos, Top); } }

    // Stick to the nearest edge of the current monitor (either side, top or bottom), fully shown.
    // Ties keep the current edge, so sliding along an edge into a corner doesn't flip it.
    void SnapToEdge()
    {
        Screen sc = Screen.FromRectangle(Bounds);
        Rectangle wa = sc.WorkingArea;
        int[] gap = { Left - wa.Left, wa.Right - Right, Top - wa.Top, wa.Bottom - Bottom };   // DockEdge order
        DockEdge best = dockEdge;
        for (int i = 0; i < gap.Length; i++) if (gap[i] < gap[(int)best]) best = (DockEdge)i;
        dockEdge = best;
        // Sides tuck to the monitor's outer edge; top and bottom stop at the taskbar
        dockBounds = DockVertical ? new Rectangle(sc.Bounds.Left, wa.Top, sc.Bounds.Width, wa.Height) : sc.Bounds;
        slideTween.Stop();
        hidden = false;
        grip.Visible = false;
        SetClip(Rectangle.Empty);
        Location = DockVertical
            ? new Point(Math.Max(wa.Left, Math.Min(Left, wa.Right - Width)), ShownPos)
            : new Point(ShownPos, Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height)));
        leftAt = DateTime.Now;
        UpdateButtons();
    }

    // Saved position's monitor is back: move there (unless the user has moved the note since)
    void RestoreWantedLocation()
    {
        if (wantedLocation == null || IsDisposed || !OnSomeScreen(wantedLocation.Value)) return;
        Point p = wantedLocation.Value;
        wantedLocation = null;
        slideTween.Stop();
        Location = p;
        if (autoHide) { SnapToEdge(); holdUntil = DateTime.Now.AddSeconds(1.5); }
        Rebuild();
        SaveSettings();
    }

    // Is a saved top-left corner on a connected monitor? (Whole monitor, so a note docked
    // behind a side taskbar still counts.)
    static bool OnSomeScreen(Point p)
    {
        Point probe = new Point(p.X + 20, p.Y + 20);
        return Screen.AllScreens.Any(sc => sc.Bounds.Contains(probe));
    }

    void SlideTo(bool hide)
    {
        hidden = hide;
        targetPos = hide ? HiddenPos : ShownPos;
        if (hide)
        {
            if (optionsOpen) ToggleOptions();
            LayoutGrip();
            grip.Visible = true;
            grip.BringToFront();
        }
        slideTween.Start(DockAxis, targetPos, hide ? 220 : 180);
        RequestFrames();
    }

    void SlideFrame()
    {
        DockAxis = (int)Math.Round(slideTween.Value());
        if (!slideTween.Running && !hidden) grip.Visible = false;
        if (!slideTween.Running && hidden) FlushCompleted();   // fully tucked away: now drop finished tasks
        // Sliding out: paint what just came into view now; the frame messages would otherwise starve WM_PAINT
        if (ApplyClip() && !hidden) Update();
    }

    // Window region currently applied, in window coordinates (Empty = no region)
    Rectangle clipRect;

    // Clip to the docked monitor so nothing spills onto a neighbouring screen.
    // Returns true when part of the window came into view and was invalidated.
    bool ApplyClip()
    {
        Rectangle vis = Rectangle.Intersect(Bounds, dockBounds);
        vis.Offset(-Left, -Top);
        if (vis.Size == Size) vis = Rectangle.Empty;   // fully on the monitor: no region
        return SetClip(vis);
    }

    // Applies a window region (Empty removes it). Pixels outside the old region were never drawn
    // into the window's surface and show as see-through until painted, and growing the region
    // doesn't reliably invalidate the child windows there, so repaint the whole tree when it grows.
    bool SetClip(Rectangle vis)
    {
        if (vis == clipRect && (vis.IsEmpty == (Region == null))) return false;
        bool grew = !clipRect.IsEmpty && (vis.IsEmpty || !clipRect.Contains(vis));
        clipRect = vis;
        Region = vis.IsEmpty ? null : new Region(vis);
        if (grew) Invalidate(true);
        return grew;
    }

    void CheckHover()
    {
        if (!autoHide || !Visible || dockBounds.IsEmpty) return;
        Rectangle hot = Rectangle.Intersect(Bounds, dockBounds);
        hot.Inflate(UI.D(2), UI.D(2));
        bool inside = hot.Contains(Cursor.Position);

        if (hidden)
        {
            // Brief dwell so just passing the edge doesn't pop the note out
            if (!inside) { enterAt = DateTime.MaxValue; return; }
            if (enterAt == DateTime.MaxValue) enterAt = DateTime.Now;
            else if ((DateTime.Now - enterAt).TotalMilliseconds > 120) { enterAt = DateTime.MaxValue; SlideTo(false); }
            return;
        }

        // Opened by the shortcut: stay out while it has focus (switching to another window or
        // pressing the shortcut again tucks it away), wherever the mouse goes
        if (keyOpened)
        {
            leftAt = DateTime.Now;
            return;
        }

        bool busy = editor != null || titleEditor != null || dialogOpen || UI.OpenMenus > 0
            || Control.MouseButtons != MouseButtons.None || DateTime.Now < holdUntil;
        if (inside || busy) { leftAt = DateTime.Now; return; }
        if ((DateTime.Now - leftAt).TotalMilliseconds > 200) SlideTo(true);
    }

    void LayoutGrip()
    {
        if (grip == null) return;
        // The strip that stays on screen: the side of the note facing away from its edge
        switch (dockEdge)
        {
            case DockEdge.Left: grip.SetBounds(Width - TabWidth, 0, TabWidth, Height); break;
            case DockEdge.Right: grip.SetBounds(0, 0, TabWidth, Height); break;
            case DockEdge.Top: grip.SetBounds(0, Height - TabWidth, Width, TabWidth); break;
            default: grip.SetBounds(0, 0, Width, TabWidth); break;
        }
        grip.Invalidate();
    }

    void PaintGrip(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(UI.T.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool due = items.Any(i => !i.Done && i.Remind > 0 && i.Remind <= DateTime.Now.Ticks);
        bool urgent = items.Any(i => !i.Done && i.Urgent);
        Color c = due ? UI.T.Alert : urgent ? UI.T.Accent : UI.T.Muted;
        // Pill along the tab: upright on a side tab, lying flat on a top/bottom tab
        bool flat = grip.Width > grip.Height;
        float thick = UI.D(3), len = Math.Min(UI.D(56), (flat ? grip.Width : grip.Height) / 3f);
        float w = flat ? len : thick, h = flat ? thick : len;
        UI.FillRound(g, c, new RectangleF((grip.Width - w) / 2f, (grip.Height - h) / 2f, w, h), thick / 2f);
    }

    IconButton HeaderButton(string glyph, string tipText)
    {
        IconButton b = new IconButton(glyph);
        b.Label = tipText;
        // Drawn by the options column, not added as a child window
        return b;
    }

    // Options in the order they rise from the gear, bottom to top
    IconButton[] OptionButtons { get { return new[] { doneBtn, sortBtn, themeBtn, dockBtn, keyBtn, pinBtn }; } }
    int OptionsPad { get { return UI.D(3); } }
    int OptionsFullHeight { get { return OptionButtons.Length * UI.D(30) + OptionsPad * 2; } }

    void ToggleOptions()
    {
        optionsOpen = !optionsOpen;
        gearBtn.Active = optionsOpen;
        gearBtn.Invalidate();
        if (!optionsOpen) HideHint();
        // Take the backdrop snapshot now, not mid-animation, so no frame stalls on it
        if (optionsOpen && veilSharp == null && list.Controls.OfType<TaskRow>().Any()) CaptureVeil();
        optionsTween.Start(optionsSize, optionsOpen ? OptionsFullHeight : 0, optionsOpen ? 260 : 200);
        optionsLeftAt = DateTime.Now;
        if (optionsOpen) optionsWatch.Start(); else optionsWatch.Stop();
        RequestFrames();
    }

    // Tuck the options away as soon as the mouse leaves the gear and its row
    void WatchOptions()
    {
        if (!optionsOpen) { optionsWatch.Stop(); return; }
        Rectangle area = Rectangle.Union(footer.RectangleToScreen(gearBtn.Bounds), RectangleToScreen(column.Bounds));
        area.Inflate(UI.D(4), UI.D(4));
        if (area.Contains(Cursor.Position) || dialogOpen) optionsLeftAt = DateTime.Now;
        else if ((DateTime.Now - optionsLeftAt).TotalMilliseconds > 600) ToggleOptions();
    }

    void RequestFrames()
    {
        lock (frameLock)
        {
            framesWanted = true;
            if (frameThread != null) return;
            frameThread = new System.Threading.Thread(FrameLoop) { IsBackground = true, Name = "frames" };
            frameThread.Start();
        }
    }

    // Waits for each compositor frame (vsync) and advances running animations on the UI thread
    void FrameLoop()
    {
        while (true)
        {
            lock (frameLock)
            {
                if (!framesWanted || IsDisposed) { frameThread = null; return; }
            }
            DwmFlush();
            try { Invoke((Action)OnFrame); }
            catch { lock (frameLock) { frameThread = null; } return; }
        }
    }

    // Set STICKYTODO_PERF=1 to log per-frame timings of each animation to %TEMP%\stickytodo-perf.txt
    static readonly bool perfLog = Environment.GetEnvironmentVariable("STICKYTODO_PERF") == "1";
    readonly List<string> perfFrames = new List<string>();

    void OnFrame()
    {
        double now = frameClock.Elapsed.TotalMilliseconds, dt = Math.Min(50, now - lastFrameMs);
        lastFrameMs = now;
        double t0 = frameClock.Elapsed.TotalMilliseconds, tLayout = t0, tVeil = t0;
        if (optionsTween.Running)
        {
            optionsSize = optionsTween.Value();
            LayoutColumn();
        }
        tLayout = frameClock.Elapsed.TotalMilliseconds;
        bool veilMoving = UpdateVeil(dt);
        bool hintMoving = UpdateHint(dt);
        veilMoving |= hintMoving;   // keeps frames coming while the hint pill animates
        tVeil = frameClock.Elapsed.TotalMilliseconds;
        if (optionsTween.Running || veilMoving) Update();
        if (slideTween.Running) SlideFrame();
        double t1 = frameClock.Elapsed.TotalMilliseconds;
        bool done;
        lock (frameLock)
        {
            done = !optionsTween.Running && !slideTween.Running && !veilMoving;
            if (done) framesWanted = false;
        }
        if (perfLog)
        {
            perfFrames.Add(string.Format(CultureInfo.InvariantCulture, "interval {0,6:F2}  layout {1,5:F2}  veil {2,5:F2}  paint {3,5:F2}  total {4,5:F2}",
                dt, tLayout - t0, tVeil - tLayout, t1 - tVeil, t1 - t0));
            if (done)
            {
                try { File.AppendAllLines(Path.Combine(Path.GetTempPath(), "stickytodo-perf.txt"), new[] { "--- animation, " + perfFrames.Count + " frames" }.Concat(perfFrames)); } catch { }
                perfFrames.Clear();
            }
        }
    }

    void LayoutHeader()
    {
        int w = UI.D(30), y = (footer.Height - w) / 2, pad = OptionsPad;
        gearBtn.Location = new Point(UI.D(8), y);

        column.Items = OptionButtons;
        LayoutColumn();
        if (veil != null && veil.Bounds != BackdropArea) veil.Bounds = BackdropArea;
        UpdateThumb();
    }

    // Show the hint pill beside an anchor (form coordinates); it springs out, or glides over if already out
    void ShowHint(string text, Rectangle anchor)
    {
        if (string.IsNullOrEmpty(text)) { HideHint(); return; }
        int h = UI.D(26);
        hintText = text;
        hintX = anchor.Right + UI.D(6);
        hintTextW = TextRenderer.MeasureText(text, UI.Button, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
        int maxW = ClientSize.Width - hintX - UI.D(4);
        hintTargetW = Math.Max(h, Math.Min(hintTextW + UI.D(30), maxW));
        hintTargetY = anchor.Top + (anchor.Height - h) / 2f;
        if (!hint.Visible || hintW < 1f)
        {
            hintY = hintTargetY;   // fresh pop: start at the button, no vertical glide
            hintYVel = 0;
            hintW = 0;
            hintWVel = 0;
        }
        hintWanted = true;
        if (!hint.Visible)
        {
            hint.SetBounds(hintX, (int)Math.Round(hintY), 1, h);
            hint.Visible = true;
        }
        hint.BringToFront();
        hint.Invalidate();
        RequestFrames();
    }

    void HideHint()
    {
        if (!hint.Visible) return;
        hintWanted = false;
        hintTargetW = 0;
        RequestFrames();
    }

    // Advances the pill's springs by dt ms; returns true while it's still moving
    bool UpdateHint(double dt)
    {
        if (!hint.Visible) return false;
        float t = (float)(dt / 1000.0);
        int steps = Math.Max(1, (int)Math.Ceiling(t / 0.004f));   // small substeps keep the springs stable
        float s = t / steps;
        // Opening is underdamped (pops past its size, settles back); closing is critically damped (snaps shut)
        float kW = hintWanted ? 420f : 900f, cW = hintWanted ? 20f : 60f;
        for (int i = 0; i < steps; i++)
        {
            hintWVel += ((hintTargetW - hintW) * kW - hintWVel * cW) * s;
            hintW += hintWVel * s;
            hintYVel += ((hintTargetY - hintY) * 700f - hintYVel * 45f) * s;
            hintY += hintYVel * s;
        }
        if (!hintWanted && hintW < 1f)
        {
            hint.Visible = false;
            hintW = hintWVel = 0;
            return false;
        }
        bool settled = Math.Abs(hintTargetW - hintW) < 0.3f && Math.Abs(hintWVel) < 4f
                    && Math.Abs(hintTargetY - hintY) < 0.3f && Math.Abs(hintYVel) < 4f;
        if (settled) { hintW = hintTargetW; hintY = hintTargetY; hintWVel = hintYVel = 0; }
        Rectangle b = new Rectangle(hintX, (int)Math.Round(hintY), Math.Max(1, (int)Math.Round(hintW)), UI.D(26));
        if (b != hint.Bounds) hint.Bounds = b;
        hint.Invalidate();
        return !settled;
    }

    void PaintHint(object sender, PaintEventArgs e)
    {
        Theme t = UI.T;
        Graphics g = e.Graphics;
        PaintColumnBackdrop(g, hint.Bounds);   // what's behind the rounded ends
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, hint.Width - 1.5f, hint.Height - 1.5f);
        using (GraphicsPath p = UI.Round(r, Math.Max(1f, Math.Min(r.Height, r.Width) / 2f)))
        {
            using (SolidBrush b = new SolidBrush(t.Menu)) g.FillPath(b, p);
            using (Pen pen = new Pen(t.Border, UI.S)) g.DrawPath(pen, p);
            g.SetClip(p);
        }
        // Small accent dot, then the label, revealed as the pill grows and faded in with it
        float progress = hintTargetW > 0 ? Math.Max(0f, Math.Min(1f, hintW / hintTargetW)) : 0f;
        float dot = UI.D(5);
        using (SolidBrush b = new SolidBrush(t.Accent))
            g.FillEllipse(b, UI.D(10), (hint.Height - dot) / 2f, dot, dot);
        int textX = UI.D(21);
        TextRenderer.DrawText(g, hintText, UI.Button,
            new Rectangle(textX, 0, Math.Max(hintTextW, (int)hintTargetW - textX - UI.D(8)), hint.Height),
            UI.Mix(t.Menu, t.Text, progress),
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    // Per-frame: one window move; the column grows upward from just above the gear
    void LayoutColumn()
    {
        int h = (int)Math.Round(optionsSize), pad = OptionsPad;
        int bottom = footer.Top + gearBtn.Top - UI.D(4);
        Rectangle col = new Rectangle(footer.Left + gearBtn.Left - pad, bottom - h, UI.D(30) + pad * 2, h);
        bool show = h > 0;
        if (col != column.Bounds) column.Bounds = col;
        if (show != column.Visible)
        {
            column.Visible = show;
            if (show) column.BringToFront();
        }
    }

    // Paints what lies behind the column (list snapshot, blurred as much as the backdrop currently is),
    // so the column's rounded corners blend in without a window region
    void PaintColumnBackdrop(Graphics g, Rectangle area)
    {
        g.Clear(UI.T.Bg);
        if (veilSharpH == IntPtr.Zero) return;
        Rectangle back = BackdropArea;
        Rectangle src = new Rectangle(area.X - back.Left, area.Y - back.Top, area.Width, area.Height);
        src.Intersect(new Rectangle(0, 0, veilSharp.Width, veilSharp.Height));
        if (src.Width <= 0 || src.Height <= 0) return;
        int dx = src.X + back.Left - area.X, dy = src.Y + back.Top - area.Y;
        IntPtr hdc = g.GetHdc();
        IntPtr mem = CreateCompatibleDC(hdc);
        try
        {
            IntPtr old = SelectObject(mem, veilSharpH);
            BitBlt(hdc, dx, dy, src.Width, src.Height, mem, src.X, src.Y, 0x00CC0020);
            if (veilAlpha > 0)
            {
                SelectObject(mem, veilBlurH);
                AlphaBlend(hdc, dx, dy, src.Width, src.Height, mem, src.X, src.Y, src.Width, src.Height, (int)(veilAlpha * 255) << 16);
            }
            SelectObject(mem, old);
        }
        finally
        {
            DeleteDC(mem);
            g.ReleaseHdc(hdc);
        }
    }

    // The area the blurred backdrop covers: the title bar and the task list
    Rectangle BackdropArea { get { return Rectangle.Union(header.Bounds, clip.Bounds); } }

    // True when the options column overlaps the title or any visible task row
    bool OptionsTouchTasks()
    {
        if (!column.Visible) return false;
        Rectangle col = column.Bounds;
        if (col.IntersectsWith(header.Bounds)) return true;
        foreach (Control c in list.Controls)
        {
            if (!c.Visible || c is Label) continue;
            Rectangle r = RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            r.Intersect(clip.Bounds);
            if (r.IntersectsWith(col)) return true;
        }
        return false;
    }

    // Fades the blurred backdrop in/out; returns true while still fading
    bool UpdateVeil(double dt)
    {
        float target = OptionsTouchTasks() ? 1f : 0f;
        if (veilAlpha == target)
        {
            if (!optionsOpen && veilAlpha == 0 && veilSharp != null && !optionsTween.Running) ReleaseVeil();
            return false;
        }
        if (veilSharp == null) CaptureVeil();
        float step = (float)(dt / 160.0);
        veilAlpha = target > veilAlpha ? Math.Min(target, veilAlpha + step) : Math.Max(target, veilAlpha - step);
        if (veilAlpha <= 0)
        {
            veil.Visible = false;
        }
        else
        {
            if (!veil.Visible)
            {
                veil.Visible = true;
                veil.BringToFront();
                column.BringToFront();
            }
            veil.Invalidate();
            column.Invalidate();   // its corners show the backdrop
        }
        return veilAlpha != target;
    }

    // GDI copies of the snapshots, blended with AlphaBlend (much cheaper per frame than GDI+)
    IntPtr veilSharpH, veilBlurH;
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("msimg32.dll")] static extern bool AlphaBlend(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, int blend);

    void ReleaseVeil()
    {
        if (veilSharp != null) { veilSharp.Dispose(); veilSharp = null; }
        if (veilBlur != null) { veilBlur.Dispose(); veilBlur = null; }
        if (veilSharpH != IntPtr.Zero) { DeleteObject(veilSharpH); veilSharpH = IntPtr.Zero; }
        if (veilBlurH != IntPtr.Zero) { DeleteObject(veilBlurH); veilBlurH = IntPtr.Zero; }
    }

    // Snapshot the title bar and task list and build a blurred, slightly dimmed copy (downscale, then smooth upscale)
    void CaptureVeil()
    {
        Rectangle area = BackdropArea;
        if (area.Width <= 0 || area.Height <= 0) return;
        ReleaseVeil();
        veilSharp = new Bitmap(area.Width, area.Height);
        using (Graphics g = Graphics.FromImage(veilSharp))
        {
            g.Clear(UI.T.Bg);
            // Title bar
            using (Bitmap part = new Bitmap(header.Width, header.Height))
            {
                header.DrawToBitmap(part, new Rectangle(Point.Empty, header.Size));
                g.DrawImageUnscaled(part, header.Left - area.Left, header.Top - area.Top);
            }
            // Task rows (drawn one by one: DrawToBitmap of the scrolled list misplaces them)
            Point listOrigin = new Point(clip.Left - area.Left, clip.Top - area.Top);
            foreach (Control c in list.Controls)
            {
                Rectangle r = clip.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                if (!r.IntersectsWith(new Rectangle(Point.Empty, clip.Size)) || r.Width <= 0 || r.Height <= 0) continue;
                using (Bitmap part = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(part, new Rectangle(Point.Empty, c.Size));
                    g.DrawImageUnscaled(part, r.X + listOrigin.X, r.Y + listOrigin.Y);
                }
            }
        }
        veil.Bounds = area;
        veilBlur = Blur(veilSharp, UI.D(7));
        using (Graphics g = Graphics.FromImage(veilBlur))
        using (SolidBrush dim = new SolidBrush(Color.FromArgb(70, UI.T.Bg)))
            g.FillRectangle(dim, 0, 0, veilBlur.Width, veilBlur.Height);
        veilSharpH = veilSharp.GetHbitmap();
        veilBlurH = veilBlur.GetHbitmap();
    }

    static Bitmap Blur(Bitmap src, int factor)
    {
        Bitmap small = new Bitmap(Math.Max(1, src.Width / factor), Math.Max(1, src.Height / factor));
        Bitmap dst = new Bitmap(src.Width, src.Height);
        using (ImageAttributes wrap = new ImageAttributes())
        {
            wrap.SetWrapMode(WrapMode.TileFlipXY);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(src, new Rectangle(0, 0, small.Width, small.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
            }
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, new Rectangle(0, 0, dst.Width, dst.Height), 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, wrap);
            }
        }
        small.Dispose();
        return dst;
    }

    void PaintVeil(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (veilSharpH == IntPtr.Zero || veilBlurH == IntPtr.Zero) { g.Clear(UI.T.Bg); return; }
        int w = Math.Min(veil.Width, veilSharp.Width), h = Math.Min(veil.Height, veilSharp.Height);
        IntPtr hdc = g.GetHdc();
        IntPtr mem = CreateCompatibleDC(hdc);
        try
        {
            IntPtr old = SelectObject(mem, veilSharpH);
            BitBlt(hdc, 0, 0, w, h, mem, 0, 0, 0x00CC0020);   // SRCCOPY
            SelectObject(mem, veilBlurH);
            AlphaBlend(hdc, 0, 0, w, h, mem, 0, 0, w, h, (int)(veilAlpha * 255) << 16);
            SelectObject(mem, old);
        }
        finally
        {
            DeleteDC(mem);
            g.ReleaseHdc(hdc);
        }
    }

    void PaintHeader(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        if (titleEditor != null) return;
        int left = UI.D(14);
        int open = items.Count(i => !i.Done);
        Size cs = open > 0 ? TextRenderer.MeasureText(open.ToString(), UI.Small) : Size.Empty;
        int maxW = header.Width - left * 2 - cs.Width;
        TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        Size ts = TextRenderer.MeasureText(noteTitle, UI.Title, new Size(maxW, int.MaxValue), flags);
        ts.Width = Math.Min(ts.Width, maxW);
        int top = (header.Height - ts.Height) / 2;
        TextRenderer.DrawText(g, noteTitle, UI.Title, new Rectangle(left, top, ts.Width, ts.Height), UI.T.Text, flags);
        if (open > 0)
            TextRenderer.DrawText(g, open.ToString(), UI.Small,
                new Point(left + ts.Width, top + ts.Height - cs.Height - UI.D(3)), UI.T.Muted);
    }

    // Inline rename of the note's title; Enter or clicking away saves, Esc cancels
    void StartRename()
    {
        if (titleEditor != null) { titleEditor.Box.Focus(); return; }
        RoundBox ed = new RoundBox { ShowPlus = false };
        ed.Box.Font = UI.Title;
        ed.Box.Text = noteTitle;
        ed.Box.MaxLength = 60;
        ed.SetBounds(UI.D(6), (header.Height - UI.D(34)) / 2, header.Width - UI.D(12), UI.D(34));
        ed.ApplyTheme();
        ed.Box.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FinishRename(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; FinishRename(false); }
        };
        ed.Box.LostFocus += delegate { BeginInvoke((Action)delegate { if (titleEditor == ed) FinishRename(true); }); };
        titleEditor = ed;
        header.Controls.Add(ed);
        header.Invalidate();
        Activate();
        ed.Box.Focus();
        ed.Box.SelectAll();
    }

    void FinishRename(bool save)
    {
        RoundBox ed = titleEditor;
        if (ed == null) return;
        titleEditor = null;
        if (save)
        {
            string text = ed.Box.Text.Trim();
            noteTitle = text.Length > 0 ? text : "Tasks";
            try { File.WriteAllText(titleFile, noteTitle); } catch { }
            ApplyTitle();
        }
        header.Controls.Remove(ed);
        ed.Dispose();
        header.Invalidate();
    }

    void ApplyTitle()
    {
        Text = noteTitle;
        if (tray != null) tray.Text = noteTitle.Length > 63 ? noteTitle.Substring(0, 60) + "..." : noteTitle;
    }

    void ApplyTheme()
    {
        Theme t = UI.T;
        BackColor = header.BackColor = footer.BackColor = clip.BackColor = list.BackColor = t.Bg;
        thumb.BackColor = t.Border;
        if (IsHandleCreated) Dwm.Apply(Handle, 2);

        trayMenu.Items.Clear();
        UI.MenuItem(trayMenu, "Show", "\uE8A7", false, delegate { ShowNote(); });
        UI.MenuItem(trayMenu, "Guide", "\uE897", false, delegate { ShowGuide(); });
        UI.MenuItem(trayMenu, "Exit", "\uE8BB", false, delegate { Close(); });

        UpdateButtons();
        Rebuild();
        Invalidate(true);
    }

    void CycleTheme()
    {
        int i = Array.IndexOf(Theme.All, UI.T);
        UI.T = Theme.All[(i + 1) % Theme.All.Length];
        ApplyTheme();
        SaveSettings();
    }

    void UpdateButtons()
    {
        pinBtn.Active = pinned;
        pinBtn.Label = pinned ? "Always on top \u00B7 on" : "Always on top \u00B7 off";
        dockBtn.Active = autoHide;
        // Dock left / dock right / dock bottom (the icon font has no "dock top", so top shares it)
        dockBtn.Glyph = !autoHide || dockEdge == DockEdge.Right ? "\uE90D" : dockEdge == DockEdge.Left ? "\uE90C" : "\uE90E";
        dockBtn.Label = autoHide ? "Auto-hide \u00B7 on" : "Auto-hide \u00B7 off";
        keyBtn.Active = hotKey != 0;
        keyBtn.Label = hotKey != 0 ? "Shortcut \u00B7 " + FormatHotkey(hotMods, hotKey) : "Set shortcut";
        grip.Invalidate();
        sortBtn.Glyph = newestFirst ? "\uE74B" : "\uE74A";
        sortBtn.Label = newestFirst ? "Newest first" : "Oldest first";
        doneBtn.Glyph = hideDone ? "\uED1A" : "\uE890";
        int doneCount = items.Count(i => i.Done);
        doneBtn.Label = hideDone ? "Show completed (" + doneCount + ")" : "Hide completed";
        themeBtn.Label = "Theme \u00B7 " + UI.T.Label;
        column.Invalidate();
        gearBtn.Invalidate();
        header.Invalidate();
    }

    void UpdateThumb()
    {
        if (thumb == null || list == null) return;
        int content = list.DisplayRectangle.Height, view = list.ClientSize.Height;
        if (content <= view + 1) { thumb.Visible = false; return; }
        int track = clip.Height - UI.D(8);
        int h = Math.Max(UI.D(24), track * view / content);
        int pos = -list.AutoScrollPosition.Y, max = Math.Max(1, content - view);
        thumb.SetBounds(clip.Width - UI.D(5), UI.D(4) + (track - h) * Math.Min(pos, max) / max, UI.D(3), h);
        thumb.Visible = true;
        thumb.BringToFront();
    }

    void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks == 2)
        {
            ignoreNextRelease = true;   // the release of this same click mustn't count as clicking away
            if (sender == header) StartRename(); else StartAdding();
            return;
        }
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        SaveSettings();
    }

    int RowWidth { get { return Math.Max(UI.D(120), clip.Width - UI.D(12)); } }

    // ---- Guide: a scrollable, illustrated how-to, opened from the tray icon's menu
    Form guideWindow;
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);

    void ShowGuide()
    {
        if (guideWindow != null && !guideWindow.IsDisposed) { guideWindow.Activate(); return; }
        Theme t = UI.T;
        string sc = hotKey != 0 ? FormatHotkey(hotMods, hotKey) : null;

        Form win = new Form
        {
            Text = "Sticky To-Do guide",
            Icon = Icon,
            AutoScaleMode = AutoScaleMode.None,
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = t.Bg,
            ClientSize = new Size(UI.D(540), Math.Min(UI.D(640), Screen.FromControl(this).WorkingArea.Height - UI.D(80)))
        };
        win.HandleCreated += delegate { Dwm.Apply(win.Handle, 0); };

        FlowLayoutPanel flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = t.Bg,
            Padding = new Padding(0, 0, 0, UI.D(12))
        };
        flow.HandleCreated += delegate { if (t.IsDark) SetWindowTheme(flow.Handle, "DarkMode_Explorer", null); };
        int w = win.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;

        // Heading
        PaintPanel head = new PaintPanel { Width = w, Height = UI.D(76), Margin = Padding.Empty };
        head.Paint += delegate(object s, PaintEventArgs e)
        {
            e.Graphics.Clear(t.Bg);
            using (Bitmap icon = DrawAppIcon(UI.D(34))) e.Graphics.DrawImage(icon, UI.D(20), UI.D(20));
            TextRenderer.DrawText(e.Graphics, "How to use Sticky To-Do", UI.Title, new Point(UI.D(64), UI.D(17)), t.Text);
            TextRenderer.DrawText(e.Graphics, "Everything the note can do, at a glance", UI.Small, new Point(UI.D(64), UI.D(43)), t.Muted);
        };
        flow.Controls.Add(head);

        Action<string, string, Action<Graphics, Rectangle>> add = delegate(string title, string body, Action<Graphics, Rectangle> art)
        {
            flow.Controls.Add(new GuideSection(title, body, art, w));
        };

        add("Add a task",
            "When the note is focused, just start typing — a new task opens with what you type. " +
            "You can also double-click empty space or press Ctrl+N. Enter saves it and starts the next one, " +
            "Shift+Enter adds a line break, and Esc cancels.",
            delegate(Graphics g, Rectangle a)
            {
                int top = a.Y + (a.Height - UI.D(60)) / 2;
                GuideArt.Editor(g, new Rectangle(a.X + UI.D(10), top, a.Width - UI.D(20), UI.D(28)), "Buy milk", null, false);
                GuideArt.KeyRow(g, a, top + UI.D(36), "Enter");
            });

        add("Show and hide anywhere",
            sc != null
                ? "Press " + sc + " from any app to bring the note out, ready to type. Press it again to tuck it away " +
                  "and go straight back to what you were doing. Clicking another window also tucks it away."
                : "Set a shortcut from ⋯ → keyboard. Then press it from any app to bring the note out, ready to type, " +
                  "and again to tuck it away and return to what you were doing.",
            delegate(Graphics g, Rectangle a)
            {
                string[] keys = sc != null ? sc.Split(new[] { " + " }, StringSplitOptions.None) : new[] { "Alt", "Q" };
                GuideArt.Combo(g, a, a.Y + (a.Height - UI.D(26)) / 2, keys);
            });

        add("Complete, edit, delete",
            "Click a task's circle to complete it — it stays ticked until the note next hides. " +
            "Click the text to edit it in place; Enter or clicking away saves. The bin appears when you hover a task.",
            delegate(Graphics g, Rectangle a)
            {
                int x = a.X + UI.D(12), top = a.Y + (a.Height - UI.D(46)) / 2;
                GuideArt.Row(g, x, top, a.Width - UI.D(24), "Call Sam", true, false);
                UI.FillRound(g, t.Hover, new RectangleF(x - UI.D(5), top + UI.D(24), a.Width - UI.D(14), UI.D(24)), UI.D(5));
                GuideArt.Row(g, x, top + UI.D(27), a.Width - UI.D(46), "Buy milk", false, false);
                UI.Glyph(g, "", UI.IconSmall, new Rectangle(a.Right - UI.D(30), top + UI.D(27), UI.D(18), UI.D(18)), t.Muted);
            });

        add("Keyboard",
            "↑ / ↓ select a task (↑ starts from the bottom, ↓ from the top). " +
            "Space completes the selected task, Delete removes it, Enter edits it, and Esc clears the selection.",
            delegate(Graphics g, Rectangle a)
            {
                int top = a.Y + (a.Height - UI.D(58)) / 2;
                GuideArt.KeyRow(g, a, top, "↑", "↓");
                GuideArt.KeyRow(g, a, top + UI.D(32), "Space", "Del");
            });

        add("Notes",
            "While editing a task, press ↓ or click the grey “Notes” line under it to add a note. " +
            "Notes show in small grey text under the task.",
            delegate(Graphics g, Rectangle a)
            {
                GuideArt.Editor(g, new Rectangle(a.X + UI.D(10), a.Y + (a.Height - UI.D(44)) / 2, a.Width - UI.D(20), UI.D(44)),
                    "Dry cleaning", "Bring the receipt", true);
            });

        add("Right-click a task",
            "Add or edit a note, mark it urgent (its text turns red), set a reminder for a desktop notification, " +
            "or delete it. The top line of the menu shows when the task was added.",
            delegate(Graphics g, Rectangle a)
            {
                int x = a.X + UI.D(12), top = a.Y + (a.Height - UI.D(40)) / 2;
                GuideArt.Row(g, x, top, a.Width - UI.D(24), "Pay rent", false, true);
                UI.Glyph(g, "", UI.IconSmall, new Rectangle(x + UI.D(20), top + UI.D(22), UI.D(14), UI.D(16)), t.Muted);
                TextRenderer.DrawText(g, "Today, 17:00", UI.Small, new Point(x + UI.D(36), top + UI.D(22)), t.Muted, TextFormatFlags.NoPrefix);
            });

        add("Options  ⋯",
            "The dots in the bottom-left open the options: show or hide completed tasks, sort oldest or newest first, " +
            "switch theme (Light, Dark, Sticky), auto-hide at the screen edge, set the shortcut, and keep the note always on top.",
            delegate(Graphics g, Rectangle a)
            {
                // Sized to fit the box: dots at the bottom, a short column of options above them
                int cell = UI.D(18), x = a.X + UI.D(16), dotsY = a.Bottom - UI.D(14);
                string[] glyphs = { "", "", "" };
                int colH = glyphs.Length * cell + UI.D(6);
                Rectangle col = new Rectangle(x - UI.D(3), dotsY - UI.D(8) - colH, cell + UI.D(6), colH);
                UI.FillRound(g, t.Bg, col, UI.D(6));
                using (GraphicsPath p = UI.Round(new RectangleF(col.X + 0.5f, col.Y + 0.5f, col.Width - 1, col.Height - 1), UI.D(6)))
                using (Pen pen = new Pen(t.Border)) g.DrawPath(pen, p);
                for (int i = 0; i < glyphs.Length; i++)
                    UI.Glyph(g, glyphs[i], UI.IconSmall, new Rectangle(x, col.Bottom - UI.D(3) - (i + 1) * cell, cell, cell), i == 1 ? t.Accent : t.Muted);
                // the hover pill beside one option
                Rectangle pill = new Rectangle(col.Right + UI.D(5), col.Bottom - UI.D(3) - 2 * cell + UI.D(1), UI.D(70), cell - UI.D(2));
                UI.FillRound(g, t.Menu, pill, pill.Height / 2f);
                using (SolidBrush b = new SolidBrush(t.Accent)) g.FillEllipse(b, pill.X + UI.D(7), pill.Y + (pill.Height - UI.D(4)) / 2f, UI.D(4), UI.D(4));
                TextRenderer.DrawText(g, "Shortcut", UI.Small, new Rectangle(pill.X + UI.D(15), pill.Y, pill.Width - UI.D(15), pill.Height), t.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                // the dots button
                float d = 3.6f * UI.S, gap = 2f * UI.S, dx = x + (cell - (3 * d + 2 * gap)) / 2f, dy = dotsY;
                using (Pen p = new Pen(t.Accent, 1.1f * UI.S))
                    for (int i = 0; i < 3; i++) g.DrawEllipse(p, dx + i * (d + gap), dy, d, d);
            });

        add("Auto-hide at the screen edge",
            "With auto-hide on, the note docks to the nearest edge of the screen (left, right, top or bottom) and tucks into a thin tab " +
            "when the mouse leaves. Hover the tab to bring it back. Drag the title to move it to another edge or screen.",
            delegate(Graphics g, Rectangle a)
            {
                int edge = a.Right - UI.D(22);
                using (Pen p = new Pen(t.Muted, 1.2f * UI.S) { DashStyle = DashStyle.Dash })
                    g.DrawLine(p, edge, a.Y + UI.D(8), edge, a.Bottom - UI.D(8));
                Rectangle ghost = new Rectangle(edge - UI.D(70), a.Y + UI.D(16), UI.D(62), a.Height - UI.D(32));
                using (GraphicsPath p = UI.Round(ghost, UI.D(6)))
                using (Pen pen = new Pen(UI.Mix(t.Border, t.Surface, 0.2f), UI.S) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, p);
                Rectangle tab = new Rectangle(edge - UI.D(6), a.Y + UI.D(16), UI.D(6), a.Height - UI.D(32));
                using (SolidBrush b = new SolidBrush(t.Menu)) g.FillRectangle(b, tab);
                UI.FillRound(g, t.Accent, new RectangleF(tab.X + UI.D(2), tab.Y + tab.Height / 2f - UI.D(9), UI.D(2), UI.D(18)), UI.D(1));
                UI.Glyph(g, "", UI.Icon, new Rectangle(ghost.X + UI.D(14), ghost.Y, UI.D(30), ghost.Height), t.Muted);
            });

        add("Rename the note",
            "Double-click the title at the top to rename the note. Enter or clicking away saves it.",
            delegate(Graphics g, Rectangle a)
            {
                GuideArt.Editor(g, new Rectangle(a.X + UI.D(12), a.Y + (a.Height - UI.D(28)) / 2, a.Width - UI.D(24), UI.D(28)), "Shopping", null, false);
            });

        add("System tray",
            "The note lives in the system tray, not on the taskbar, and starts with Windows. " +
            "Click the tray icon to show the note; right-click it to open this guide or exit.",
            delegate(Graphics g, Rectangle a)
            {
                // Menu on the left, the tray icon down at the bottom-right (like next to the clock)
                Rectangle menu = new Rectangle(a.X + UI.D(10), a.Y + UI.D(8), UI.D(74), UI.D(54));
                UI.FillRound(g, t.Menu, menu, UI.D(6));
                using (GraphicsPath p = UI.Round(new RectangleF(menu.X + 0.5f, menu.Y + 0.5f, menu.Width - 1, menu.Height - 1), UI.D(6)))
                using (Pen pen = new Pen(t.Border)) g.DrawPath(pen, p);
                int iconSize = UI.D(18);
                using (Bitmap icon = DrawAppIcon(iconSize)) g.DrawImage(icon, menu.Right + UI.D(10), a.Bottom - iconSize - UI.D(8));
                string[] lines = { "Show", "Guide", "Exit" };
                for (int i = 0; i < lines.Length; i++)
                {
                    Rectangle row = new Rectangle(menu.X + UI.D(4), menu.Y + UI.D(4) + i * UI.D(15), menu.Width - UI.D(8), UI.D(15));
                    if (i == 1) UI.FillRound(g, UI.Mix(t.Menu, t.Text, 0.1f), row, UI.D(4));
                    TextRenderer.DrawText(g, lines[i], UI.Small, new Rectangle(row.X + UI.D(6), row.Y, row.Width, row.Height), t.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            });

        win.Controls.Add(flow);
        win.FormClosed += delegate { guideWindow = null; };
        guideWindow = win;
        win.Show();
        win.Activate();
    }

    // Tasks ticked off while the note is out stay listed (ticked) until it next tucks away or hides,
    // even with "hide completed" on, so they don't vanish from under the pointer
    readonly HashSet<TodoItem> justCompleted = new HashSet<TodoItem>();

    bool IsShown(TodoItem i) { return !(hideDone && i.Done) || justCompleted.Contains(i); }

    void ToggleDone(TodoItem it)
    {
        it.Done = !it.Done;
        if (it.Done) justCompleted.Add(it); else justCompleted.Remove(it);
        Changed();
    }

    // Called once the note is out of sight: drop the tasks that were kept for show
    void FlushCompleted()
    {
        if (justCompleted.Count == 0) return;
        justCompleted.Clear();
        if (hideDone) Rebuild();
    }

    // ---- Keyboard selection: Up/Down pick a task, Space completes, Delete removes, Enter edits, Esc clears
    TodoItem selectedItem;
    int selectedIndex = -1;   // where the selection was, so it can land on the next task when that one goes away

    List<TaskRow> VisibleRows { get { return list.Controls.OfType<TaskRow>().ToList(); } }

    void SetSelected(TodoItem it)
    {
        selectedItem = it;
        TaskRow target = null;
        List<TaskRow> rows = VisibleRows;
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Selected = rows[i].Item == it;
            if (rows[i].Item == it) { target = rows[i]; selectedIndex = i; }
        }
        if (target != null) { list.ScrollControlIntoView(target); UpdateThumb(); }
        else selectedIndex = -1;
    }

    // dir -1 = up, +1 = down; with nothing selected, up starts from the bottom and down from the top
    void MoveSelection(int dir)
    {
        List<TaskRow> rows = VisibleRows;
        if (rows.Count == 0) return;
        int i = selectedItem == null ? -1 : rows.FindIndex(r => r.Item == selectedItem);
        int next = i < 0 ? (dir < 0 ? rows.Count - 1 : 0) : Math.Max(0, Math.Min(rows.Count - 1, i + dir));
        SetSelected(rows[next].Item);
    }

    // After the list is rebuilt: keep the selection, or move it to whatever took its place
    void RestoreSelection()
    {
        if (selectedItem == null) return;
        List<TaskRow> rows = VisibleRows;
        if (rows.Any(r => r.Item == selectedItem)) { SetSelected(selectedItem); return; }
        if (rows.Count == 0 || selectedIndex < 0) { SetSelected(null); return; }
        SetSelected(rows[Math.Min(selectedIndex, rows.Count - 1)].Item);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool free = !(ActiveControl is TextBox) && !dialogOpen && editor == null && titleEditor == null;
        if (free)
        {
            switch (keyData)
            {
                case Keys.Up: MoveSelection(-1); return true;
                case Keys.Down: MoveSelection(1); return true;
                case Keys.Space:
                    if (selectedItem == null) break;   // no selection: a space just starts a new task
                    ToggleDone(selectedItem);
                    return true;
                case Keys.Delete:
                    if (selectedItem == null) break;
                    items.Remove(selectedItem);
                    Changed();
                    return true;
                case Keys.Enter:
                    if (selectedItem == null) break;   // no selection: Enter starts a new task
                    TodoItem it = selectedItem;
                    SetSelected(null);
                    StartEditing(it, false);
                    return true;
                case Keys.Escape:
                    if (selectedItem == null) break;
                    SetSelected(null);
                    return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // A mouse button was released over one of this app's windows. Deferred, so whatever was clicked
    // (another task's text, its circle, a menu item) handles the click first.
    bool ignoreNextRelease;

    void ClickedSomewhere(IntPtr hwnd)
    {
        if (ignoreNextRelease) { ignoreNextRelease = false; return; }
        Control target = Control.FromChildHandle(hwnd);
        if (editor != null && !IsInside(target, editor))
        {
            TaskEditor ed = editor;
            BeginInvoke((Action)delegate { if (editor == ed) FinishEditor(true, false); });
        }
        if (titleEditor != null && !IsInside(target, titleEditor))
        {
            RoundBox ed = titleEditor;
            BeginInvoke((Action)delegate { if (titleEditor == ed) FinishRename(true); });
        }
    }

    static bool IsInside(Control c, Control container)
    {
        for (; c != null; c = c.Parent) if (c == container) return true;
        return false;
    }

    // Inline editor for a new task; Enter adds it and opens the next, Esc or clicking away closes
    void StartAdding()
    {
        if (editor != null && editing == null) { editor.Title.Focus(); return; }
        OpenEditor(null, false);
    }

    // Click on a task's text: edit it in place (optionally starting in its notes)
    void StartEditing(TodoItem it, bool notes)
    {
        if (editor != null && editing == it) { (notes ? editor.Notes : editor.Title).Focus(); return; }
        OpenEditor(it, notes);
    }

    void OpenEditor(TodoItem it, bool focusNotes)
    {
        if (editor != null) FinishEditor(true, false);   // save whatever was being edited
        if (selectedItem != null) SetSelected(null);
        TaskEditor ed = new TaskEditor(it == null) { Urgent = it != null && it.Urgent && !it.Done };
        if (it != null)
        {
            ed.Title.Text = it.Text.Replace("\n", "\r\n");
            ed.Notes.Text = it.Notes.Replace("\n", "\r\n");
        }
        ed.ApplyTheme();
        ed.Finished = delegate(bool save, bool another) { if (editor == ed) FinishEditor(save, another); };
        ed.ContentChanged += delegate { if (editor == ed) BeginInvoke((Action)AutoSizeNote); };
        editor = ed;
        editing = it;

        Rebuild();
        list.ScrollControlIntoView(ed);
        Activate();
        TextBox box = focusNotes ? ed.Notes : ed.Title;
        box.Focus();
        box.SelectionStart = box.TextLength;
    }

    static string CleanText(string s)
    {
        return s.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Trim();
    }

    void FinishEditor(bool save, bool another)
    {
        TaskEditor ed = editor;
        if (ed == null) return;
        TodoItem it = editing;
        editor = null;
        editing = null;
        string text = CleanText(ed.Title.Text), notes = CleanText(ed.Notes.Text);
        if (save && text.Length > 0)
        {
            if (it != null) { it.Text = text; it.Notes = notes; }
            else items.Add(new TodoItem { Created = DateTime.Now.Ticks, Text = text, Notes = notes });
            SaveItems();
            UpdateButtons();
        }
        else another = false;
        Rebuild();   // disposes the editor
        if (another && it == null) StartAdding();
    }

    void Changed()
    {
        SaveItems();
        UpdateButtons();
        BeginInvoke((Action)Rebuild);
    }

    void Rebuild()
    {
        RebuildRows();
        AutoSizeNote();
    }

    bool autoSizing;

    // Fit the note to its content: minimum size when empty, otherwise wide enough for the longest
    // task line (capped, then wrapping) and tall enough for every task (capped, then scrolling)
    void AutoSizeNote()
    {
        if (autoSizing || clip == null) return;
        autoSizing = true;
        try
        {
            Rectangle wa = Screen.FromRectangle(autoHide && !dockBounds.IsEmpty ? dockBounds : Bounds).WorkingArea;
            int minW = MinimumSize.Width, minH = MinimumSize.Height;
            int maxW = Math.Max(minW, Math.Min(UI.D(460), wa.Width / 2));
            int maxH = Math.Max(minH, wa.Height - UI.D(24));
            List<TodoItem> visible = items.Where(IsShown).ToList();
            if (editor != null) editor.Height = editor.PreferredHeight(editor.Width);

            int w = minW, h = minH;
            if (visible.Count > 0 || editor != null)
            {
                const TextFormatFlags one = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                int textW = 0;
                Func<string, Font, int> widest = delegate(string s, Font f)
                {
                    int m = 0;
                    foreach (string line in s.Replace("\r", "").Split('\n'))
                        m = Math.Max(m, TextRenderer.MeasureText(line, f, new Size(int.MaxValue, int.MaxValue), one).Width);
                    return m;
                };
                foreach (TodoItem it in visible)
                {
                    if (it == editing) continue;
                    textW = Math.Max(textW, widest(it.Text, it.Done ? UI.BodyStrike : UI.Body));
                    if (it.Notes.Length > 0) textW = Math.Max(textW, widest(it.Notes, UI.Small));
                }
                if (editor != null)
                    textW = Math.Max(textW, Math.Max(widest(editor.Title.Text, UI.Body), widest(editor.Notes.Text, UI.Small)) + UI.D(12));
                // row text area = clip width - 12 (list margins) - 44 (checkbox) - 38 (delete button)
                int needW = textW + UI.D(12 + 44 + 38 + 4) + Padding.Horizontal;

                int open = items.Count(i => !i.Done);
                int titleW = TextRenderer.MeasureText(noteTitle, UI.Title).Width
                    + (open > 0 ? TextRenderer.MeasureText(open.ToString(), UI.Small).Width : 0);
                needW = Math.Max(needW, titleW + UI.D(28 + 8) + Padding.Horizontal);
                w = Math.Max(minW, Math.Min(maxW, needW));

                // Rows wrap differently at the new width, so lay them out before measuring height
                if (w != Width) { SetSize(w, Height); RebuildRows(); }
                int content = list.Padding.Vertical;
                foreach (Control c in list.Controls) content += c.Height + c.Margin.Vertical;
                h = Math.Max(minH, Math.Min(maxH, header.Height + footer.Height + Padding.Vertical + content));
            }
            if (w != Width || h != Height)
            {
                bool widthChanged = w != Width;
                SetSize(w, h);
                if (widthChanged) RebuildRows();
            }
        }
        finally { autoSizing = false; }
    }

    // Resize while keeping the note on screen and attached to its docked edge
    void SetSize(int w, int h)
    {
        Rectangle wa = Screen.FromRectangle(autoHide && !dockBounds.IsEmpty ? dockBounds : Bounds).WorkingArea;
        bool docked = autoHide && !dockBounds.IsEmpty, slidesY = docked && DockVertical;
        int left = Left, top = Top;
        // Docked: stay attached to the edge (a bottom-docked note grows upwards)
        if (docked && !slideTween.Running)
        {
            int p = DockPos(hidden, w, h);
            if (slidesY) top = p; else left = p;
        }
        // Keep the other axis (or both, when not docked) on screen
        if (!slidesY) top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - h));
        if (!docked || slidesY) left = Math.Max(wa.Left, Math.Min(left, wa.Right - w));
        SetBounds(left, top, w, h);
        if (autoHide && !dockBounds.IsEmpty) ApplyClip();
    }

    void RebuildRows()
    {
        // The old rows are disposed and new ones created; without this the list would show
        // its empty background for a frame or more until each new row paints itself
        bool freeze = list.IsHandleCreated;
        if (freeze) SendMessage(list.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try { RebuildRowsCore(); }
        finally
        {
            if (freeze)
            {
                SendMessage(list.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                // Paint the list and every new row now, in one pass
                RedrawWindow(list.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
        }
    }

    void RebuildRowsCore()
    {
        list.SuspendLayout();
        // Keep an in-progress new task in place (removing it would steal its focus and commit it)
        foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != editor) c.Dispose();

        int rowW = RowWidth;
        List<TodoItem> visible = items.Where(IsShown).ToList();
        IEnumerable<TodoItem> sorted = newestFirst
            ? visible.OrderByDescending(i => i.Created)
            : visible.OrderBy(i => i.Created);
        // The editor takes the place of the task being edited, or goes at the end (start, if newest first)
        bool placed = false;
        Action placeEditor = delegate
        {
            editor.Size = new Size(rowW, editor.PreferredHeight(rowW));
            editor.ApplyTheme();
            if (!list.Controls.Contains(editor)) list.Controls.Add(editor);
            list.Controls.SetChildIndex(editor, list.Controls.Count - 1);
            placed = true;
        };
        foreach (TodoItem it in sorted)
        {
            if (editor != null && it == editing) placeEditor();
            else list.Controls.Add(MakeRow(it, rowW));
        }

        if (editor != null)
        {
            if (!placed) placeEditor();
            if (editing == null && newestFirst) list.Controls.SetChildIndex(editor, 0);
        }
        else if (visible.Count == 0)
        {
            int hidden = items.Count(i => i.Done);
            Label empty = new Label
            {
                Text = (hidden > 0 && hideDone ? "All done \u00B7 " + hidden + " completed hidden" : "Nothing here yet")
                    + "\nDouble-click to add a task",
                Font = UI.Small,
                ForeColor = UI.T.Muted,
                BackColor = UI.T.Bg,
                Size = new Size(rowW, UI.D(80)),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty
            };
            empty.DoubleClick += delegate { StartAdding(); };
            list.Controls.Add(empty);
        }

        list.ResumeLayout();
        UpdateThumb();
        RestoreSelection();
        if (veilSharp != null) { CaptureVeil(); veil.Invalidate(); }   // keep the blurred backdrop current
    }

    Control MakeRow(TodoItem it, int rowW)
    {
        TaskRow row = new TaskRow(it, rowW);
        row.ToggleClicked += delegate { ToggleDone(it); };
        row.DeleteClicked += delegate { items.Remove(it); Changed(); };
        // Deferred: opening the editor rebuilds the list, which disposes this row mid-click
        row.EditClicked += delegate { BeginInvoke((Action)delegate { StartEditing(it, false); }); };
        // Right-click menu: task info, then notes, urgent and reminder
        ContextMenuStrip menu = UI.Menu();
        ToolStripMenuItem info = UI.MenuItem(menu, "Added " + new DateTime(it.Created).ToString("ddd d MMM yyyy, HH:mm"), "", false, null);
        info.Enabled = false;   // information only
        if (it.Remind > 0)
        {
            ToolStripMenuItem when = UI.MenuItem(menu, "Reminder " + FormatWhen(new DateTime(it.Remind)), "", false, null);
            when.Enabled = false;
        }
        menu.Items.Add(new ToolStripSeparator());
        UI.MenuItem(menu, it.Notes.Length > 0 ? "Edit note" : "Add note", "", false,
            delegate { BeginInvoke((Action)delegate { StartEditing(it, true); }); });
        UI.MenuItem(menu, it.Urgent ? "Unmark urgent" : "Mark urgent", "\uE7C1", false,
            delegate { it.Urgent = !it.Urgent; Changed(); });
        UI.MenuItem(menu, it.Remind > 0 ? "Change reminder\u2026" : "Set reminder\u2026", "\uE823", false,
            delegate { EditReminder(it); });
        if (it.Remind > 0)
            UI.MenuItem(menu, "Remove reminder", "\uE711", false,
                delegate { it.Remind = 0; it.Notified = false; Changed(); });
        menu.Items.Add(new ToolStripSeparator());
        UI.MenuItem(menu, "Delete", "\uE74D", true, delegate { items.Remove(it); Changed(); });
        row.ContextMenuStrip = menu;
        return row;
    }

    public static string FormatWhen(DateTime at)
    {
        return FormatDay(at.Date) + ", " + at.ToString("HH:mm");
    }

    static string FormatDay(DateTime day)
    {
        if (day == DateTime.Today) return "Today";
        if (day == DateTime.Today.AddDays(1)) return "Tomorrow";
        return day.ToString("ddd d MMM");
    }

    static bool TryParseTime(string s, out TimeSpan time)
    {
        DateTime t;
        string[] formats = { "H:mm", "HH:mm", "H.mm", "HHmm", "Hmm", "H", "h:mmtt", "h:mm tt", "htt", "h tt" };
        bool ok = DateTime.TryParseExact(s.Trim().ToUpperInvariant(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out t);
        time = ok ? t.TimeOfDay : TimeSpan.Zero;
        return ok;
    }

    void EditReminder(TodoItem it)
    {
        DateTime start = it.Remind > 0 ? new DateTime(it.Remind) : RoundMinute(DateTime.Now.AddHours(1));
        DateTime day = start.Date;
        Theme t = UI.T;

        Form dlg = new Form
        {
            Text = "Remind me",
            AutoScaleMode = AutoScaleMode.None,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowIcon = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(UI.D(300), UI.D(194)),
            BackColor = t.Bg,
            ForeColor = t.Text,
            Font = UI.Body,
            TopMost = true,
            KeyPreview = true
        };
        dlg.HandleCreated += delegate { Dwm.Apply(dlg.Handle, 0); };

        Label name = new Label
        {
            Text = it.Text,
            Font = UI.Title,
            ForeColor = t.Text,
            AutoEllipsis = true,
            UseMnemonic = false,
            Location = new Point(UI.D(16), UI.D(12)),
            Size = new Size(UI.D(268), UI.D(28))
        };

        int rowY = UI.D(50);
        IconButton prev = new IconButton("\uE76B") { Location = new Point(UI.D(12), rowY) };
        Label dayLbl = new Label
        {
            Font = UI.Body,
            ForeColor = t.Text,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(UI.D(42), rowY),
            Size = new Size(UI.D(116), UI.D(30))
        };
        IconButton next = new IconButton("\uE76C") { Location = new Point(UI.D(158), rowY) };
        RoundBox timeBox = new RoundBox { ShowPlus = false, Location = new Point(UI.D(200), rowY), Size = new Size(UI.D(84), UI.D(30)) };
        timeBox.ApplyTheme();
        TextBox timeTxt = timeBox.Box;
        timeTxt.TextAlign = HorizontalAlignment.Center;
        timeTxt.Text = start.ToString("HH:mm");

        Action refreshDay = delegate { dayLbl.Text = FormatDay(day); };
        refreshDay();
        prev.Click += delegate { if (day > DateTime.Today) { day = day.AddDays(-1); refreshDay(); } };
        next.Click += delegate { day = day.AddDays(1); refreshDay(); };

        FlowLayoutPanel chips = new FlowLayoutPanel
        {
            Location = new Point(UI.D(12), UI.D(94)),
            Size = new Size(UI.D(280), UI.D(32)),
            BackColor = t.Bg,
            WrapContents = false
        };
        Action<string, Func<DateTime>> chip = delegate(string label, Func<DateTime> when)
        {
            PillButton b = new PillButton(label, false, true) { Margin = new Padding(UI.D(4), 0, UI.D(2), 0) };
            b.Click += delegate
            {
                DateTime at = when();
                day = at.Date;
                timeTxt.Text = at.ToString("HH:mm");
                refreshDay();
            };
            chips.Controls.Add(b);
        };
        chip("+15 min", delegate { return RoundMinute(DateTime.Now.AddMinutes(15)); });
        chip("+1 hour", delegate { return RoundMinute(DateTime.Now.AddHours(1)); });
        chip("Tomorrow 9:00", delegate { return DateTime.Today.AddDays(1).AddHours(9); });

        PillButton set = new PillButton("Set reminder", true, false);
        PillButton cancel = new PillButton("Cancel", false, false);
        int btnY = dlg.ClientSize.Height - set.Height - UI.D(16);
        set.Location = new Point(dlg.ClientSize.Width - set.Width - UI.D(16), btnY);
        cancel.Location = new Point(set.Left - cancel.Width - UI.D(8), btnY);

        Action doSet = delegate
        {
            TimeSpan time;
            if (!TryParseTime(timeTxt.Text, out time))
            {
                timeBox.Error = true;
                timeBox.Invalidate();
                timeTxt.Focus();
                return;
            }
            it.Remind = day.Add(time).Ticks;
            it.Notified = false;
            it.Urgent = true;
            dlg.DialogResult = DialogResult.OK;
        };
        set.Click += delegate { doSet(); };
        cancel.Click += delegate { dlg.DialogResult = DialogResult.Cancel; };
        dlg.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doSet(); }
            else if (e.KeyCode == Keys.Escape) dlg.DialogResult = DialogResult.Cancel;
        };

        dlg.Controls.AddRange(new Control[] { name, prev, dayLbl, next, timeBox, chips, cancel, set });
        dlg.Shown += delegate { timeTxt.Focus(); timeTxt.SelectAll(); };

        dialogOpen = true;
        if (dlg.ShowDialog(this) == DialogResult.OK) Changed();
        dialogOpen = false;
        dlg.Dispose();
    }

    static DateTime RoundMinute(DateTime t)
    {
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
    }

    void CheckReminders()
    {
        long now = DateTime.Now.Ticks;
        List<TodoItem> due = items.Where(i => i.Remind > 0 && i.Remind <= now && !i.Notified && !i.Done).ToList();
        if (due.Count == 0) return;
        foreach (TodoItem it in due) it.Notified = true;
        SaveItems();

        string title = due.Count == 1 ? "Reminder" : due.Count + " reminders";
        string body = string.Join("\n", due.Select(i => "\u2022 " + i.Text));
        tray.ShowBalloonTip(15000, title, body, ToolTipIcon.Warning);
        SystemSounds.Exclamation.Play();
        Rebuild();
    }

    // Task text may contain line breaks; files from this version start with a header and escape them
    const string DataHeader = "#StickyTodo 2";

    static string EscapeText(string s) { return s.Replace("\\", "\\\\").Replace("\n", "\\n"); }

    static string UnescapeText(string s)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && (s[i + 1] == 'n' || s[i + 1] == '\\'))
            {
                sb.Append(s[i + 1] == 'n' ? '\n' : '\\');
                i++;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    void LoadItems()
    {
        if (!File.Exists(dataFile)) return;
        string[] lines = File.ReadAllLines(dataFile);
        bool escaped = lines.Length > 0 && lines[0] == DataHeader;
        foreach (string line in lines)
        {
            string[] p = line.Split('\t');
            long ticks;
            if (p.Length < 3 || !long.TryParse(p[0], out ticks)) continue;
            TodoItem it = new TodoItem { Created = ticks, Done = p[1] == "1" };
            if (p.Length >= 6)
            {
                // created, done, urgent, remind, notified, text
                it.Urgent = p[2] == "1";
                long.TryParse(p[3], out it.Remind);
                it.Notified = p[4] == "1";
                it.Text = escaped ? UnescapeText(p[5]) : p[5];
                if (p.Length >= 7) it.Notes = UnescapeText(p[6]);   // created, done, urgent, remind, notified, text, notes
            }
            else
            {
                it.Text = p[2]; // original format: created, done, text
            }
            items.Add(it);
        }
    }

    void SaveItems()
    {
        File.WriteAllLines(dataFile, new[] { DataHeader }.Concat(items.Select(i => string.Join("\t", new[] {
            i.Created.ToString(CultureInfo.InvariantCulture), i.Done ? "1" : "0", i.Urgent ? "1" : "0",
            i.Remind.ToString(CultureInfo.InvariantCulture), i.Notified ? "1" : "0", EscapeText(i.Text), EscapeText(i.Notes) }))));
    }

    // settings: x,y (screen pixels), w,h (unscaled size), topmost, newestFirst, hideDone, theme, version,
    // autoHide, shortcut (mods:key), dock edge (L/R/T/B).
    // Older files: 7 fields = DPI-unaware coordinates, 8 fields = sizes in system-DPI pixels.
    void LoadSettings()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Point loc = new Point(wa.Right - (int)(330 * systemScale), wa.Top + (int)(60 * systemScale));
        float w = 290, h = 380;

        string[] s = File.Exists(settingsFile) ? File.ReadAllText(settingsFile).Trim().Split(',') : new string[0];
        if (s.Length >= 6)
        {
            int x, y;
            float sw, sh;
            if (int.TryParse(s[0], out x) && int.TryParse(s[1], out y)
                && float.TryParse(s[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sw)
                && float.TryParse(s[3], NumberStyles.Float, CultureInfo.InvariantCulture, out sh))
            {
                if (s.Length == 7) { x = (int)(x * systemScale); y = (int)(y * systemScale); }
                else if (s.Length == 8) { sw /= systemScale; sh /= systemScale; }
                w = sw;
                h = sh;
                if (OnSomeScreen(new Point(x, y))) loc = new Point(x, y);
                else
                {
                    // Its monitor isn't connected (yet): wait on the nearest one, and go back
                    // to the saved spot if that monitor shows up (see RestoreWantedLocation)
                    wantedLocation = new Point(x, y);
                    Rectangle near = Screen.FromPoint(new Point(x, y)).WorkingArea;
                    loc = new Point(Math.Max(near.Left, Math.Min(x, near.Right - UI.D(w))),
                                    Math.Max(near.Top, Math.Min(y, near.Bottom - UI.D(h))));
                }
            }
            pinned = s[4] == "1";
            newestFirst = s[5] == "1";
            hideDone = s.Length > 6 && s[6] == "1";
            autoHide = s.Length > 9 && s[9] == "1";
            if (s.Length > 10)
            {
                string[] hk = s[10].Split(':');
                if (hk.Length == 2 && int.TryParse(hk[0], out hotMods) && int.TryParse(hk[1], out hotKey)) { }
                else hotMods = hotKey = 0;
            }
            if (s.Length > 11)
                switch (s[11])
                {
                    case "L": dockEdge = DockEdge.Left; break;
                    case "R": dockEdge = DockEdge.Right; break;
                    case "T": dockEdge = DockEdge.Top; break;
                    case "B": dockEdge = DockEdge.Bottom; break;
                }
            if (s.Length > 7)
                foreach (Theme t in Theme.All)
                    if (t.Name == s[7]) UI.T = t;
        }

        ScaleTo(ScaleAt(loc));
        Bounds = new Rectangle(loc, new Size(Math.Max(UI.D(w), MinimumSize.Width), Math.Max(UI.D(h), MinimumSize.Height)));
    }

    void SaveSettings()
    {
        if (WindowState != FormWindowState.Normal) return;
        CultureInfo inv = CultureInfo.InvariantCulture;
        // A saved spot still waiting for its monitor wins over wherever the note is parked meanwhile
        Point at = wantedLocation ?? (autoHide && !dockBounds.IsEmpty ? ShownLocation : Location);
        File.WriteAllText(settingsFile, string.Join(",", new[] {
            at.X.ToString(inv), at.Y.ToString(inv),
            (Width / UI.S).ToString("0.#", inv), (Height / UI.S).ToString("0.#", inv),
            pinned ? "1" : "0", newestFirst ? "1" : "0", hideDone ? "1" : "0", UI.T.Name, "3",
            autoHide ? "1" : "0", hotKey != 0 ? hotMods + ":" + hotKey : "", "LRTB"[(int)dockEdge].ToString() }));
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-icon") { WriteIconFile(args[1]); return; }

        bool firstInstance;
        instanceMutex = new System.Threading.Mutex(true, "StickyTodo.SingleInstance." + Environment.UserName, out firstInstance);
        if (!firstInstance)
        {
            AllowSetForegroundWindow(-1);   // ASFW_ANY: let the running copy come to the front
            ShowRunningInstance(args.Contains("--guide"));
            return;
        }
        openGuideOnStart = args.Contains("--guide");
        // Per-monitor v2 so the note renders natively on every monitor; fall back to system DPI
        bool perMonitor = false;
        try { perMonitor = SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }
        if (!perMonitor) try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) systemScale = g.DpiX / 96f;
        UI.Init(systemScale);
        Application.Run(new StickyForm());
    }
}

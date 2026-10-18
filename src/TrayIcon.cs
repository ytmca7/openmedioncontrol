using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenMedionControl.Hardware;

namespace OpenMedionControl;

public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly MainWindow _window;
    readonly ToolStripMenuItem _office = new("Office");
    readonly ToolStripMenuItem _gaming = new("Gaming");
    readonly ToolStripMenuItem _turbo = new("Turbo");
    readonly ToolStripMenuItem _fan = new("Max fan speed");
    readonly ToolStripMenuItem _lights = new("Lighting");

    public TrayIcon(MainWindow window)
    {
        _window = window;

        _office.Click += (_, _) => _window.RequestMode(PerformanceMode.Office);
        _gaming.Click += (_, _) => _window.RequestMode(PerformanceMode.Gaming);
        _turbo.Click += (_, _) => _window.RequestMode(PerformanceMode.Turbo);
        _fan.Click += (_, _) => _window.RequestFanBoost(!_fan.Checked);
        _lights.Click += (_, _) => _window.RequestLights(!_lights.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_office);
        menu.Items.Add(_gaming);
        menu.Items.Add(_turbo);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_fan);
        menu.Items.Add(_lights);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open", null, (_, _) => _window.ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => _window.ExitApp());
        menu.Opening += (_, _) => _lights.Checked = _window.LightsOn;

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "OpenMedionControl",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _window.ShowFromTray(); };

        _window.StatusChanged += OnStatus;
    }

    void OnStatus(HardwareStatus s)
    {
        _office.Checked = s.Mode == PerformanceMode.Office;
        _gaming.Checked = s.Mode == PerformanceMode.Gaming;
        _turbo.Checked = s.Mode == PerformanceMode.Turbo;
        _turbo.Enabled = s.TurboAvailable;
        _fan.Checked = s.FanBoost == true;

        string text = s.Error != null
            ? "OpenMedionControl – EC error"
            : $"{HardwareStatus.ModeName(s.Mode)} · CPU {s.CpuTemp}°C · GPU {s.GpuTemp}°C";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    static Bitmap DrawLogo(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var bg = new SolidBrush(System.Drawing.Color.FromArgb(0, 166, 235));
        g.FillEllipse(bg, 0, 0, size - 1, size - 1);
        using var font = new Font("Segoe UI", size * 0.5f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("M", font, System.Drawing.Brushes.White, new RectangleF(0, 0, size, size + 1), fmt);
        return bmp;
    }

    static Icon CreateIcon()
    {
        using var bmp = DrawLogo(32);
        return Icon.FromHandle(bmp.GetHicon());
    }

    public static ImageSource CreateImageSource()
    {
        using var bmp = DrawLogo(64);
        return Imaging.CreateBitmapSourceFromHBitmap(bmp.GetHbitmap(), IntPtr.Zero,
            System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }
}

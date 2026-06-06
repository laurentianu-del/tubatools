using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MetroFramework;
using MetroFramework.Controls;
using TubaWinUi3.Compatible.Models;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible.Forms
{
    public class ToolListPage : UserControl
    {
        private Panel _scrollHost;
        private FlowLayoutPanel _flow;
        private Label _statusLabel;
        private Panel _statusBar;
        private string _category;
        private IReadOnlyList<ToolItem> _tools;
        private bool _dark = true;

        const int STATUS_H = 28;
        const int CARD_W = 190;
        const int CARD_H = 88;

        public ToolListPage()
        {
            BackColor = Color.FromArgb(22, 22, 26);

            _statusBar = new Panel();
            _statusBar.BackColor = Color.FromArgb(28, 28, 32);
            _statusBar.Height = STATUS_H;
            _statusBar.Dock = DockStyle.Bottom;
            Controls.Add(_statusBar);

            _statusLabel = new Label();
            _statusLabel.Font = new Font("Microsoft YaHei UI", 8.5f);
            _statusLabel.ForeColor = Color.FromArgb(120, 120, 128);
            _statusLabel.BackColor = Color.Transparent;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusBar.Controls.Add(_statusLabel);

            _scrollHost = new Panel();
            _scrollHost.Dock = DockStyle.Fill;
            _scrollHost.AutoScroll = true;
            _scrollHost.BackColor = BackColor;
            Controls.Add(_scrollHost);

            _flow = new FlowLayoutPanel();
            _flow.Dock = DockStyle.Fill;
            _flow.WrapContents = true;
            _flow.BackColor = BackColor;
            _flow.Padding = new Padding(14, 10, 14, 10);
            _flow.FlowDirection = FlowDirection.LeftToRight;
            _flow.AutoScroll = true;
            _scrollHost.Controls.Add(_flow);
        }

        public new void SetBounds(int x, int y, int w, int h)
        {
            base.SetBounds(x, y, w, h);
        }

        public void SetCategory(string cat)
        {
            _category = cat;
            LoadTools();
        }

        private void LoadTools()
        {
            _flow.Controls.Clear();

            if (_category == null)
            {
                var all = new List<ToolItem>();
                foreach (var c in ToolCatalog.GetCategories())
                    all.AddRange(ToolCatalog.GetTools(c));
                _tools = all;
            }
            else
            {
                _tools = ToolCatalog.GetTools(_category);
            }

            if (_tools == null || _tools.Count == 0)
            {
                _statusLabel.Text = "  未找到工具";
                return;
            }

            ToolIconService.LoadIcons(_tools);
            foreach (var t in _tools)
                _flow.Controls.Add(MakeCard(t));

            _statusLabel.Text = "  共 " + _tools.Count + " 个工具" +
                (_category != null ? " · " + _category : "");
        }

        private Control MakeCard(ToolItem tool)
        {
            var bg = _dark ? Color.FromArgb(38, 38, 42) : Color.White;
            var hover = _dark ? Color.FromArgb(48, 48, 54) : Color.FromArgb(236, 242, 255);
            var fg = _dark ? Color.FromArgb(215, 215, 220) : Color.FromArgb(25, 25, 30);
            var sub = _dark ? Color.FromArgb(130, 130, 138) : Color.FromArgb(105, 105, 112);

            var card = new Panel();
            card.Size = new Size(CARD_W, CARD_H);
            card.BackColor = bg;
            card.Cursor = Cursors.Hand;
            card.Margin = new Padding(4);

            var accent = new Panel();
            accent.Size = new Size(CARD_W, 3);
            accent.Location = new Point(0, 0);
            accent.BackColor = Color.FromArgb(0, 120, 215);
            card.Controls.Add(accent);

            var icon = new PictureBox();
            icon.Size = new Size(32, 32);
            icon.Location = new Point(12, 12);
            icon.SizeMode = PictureBoxSizeMode.StretchImage;
            icon.BackColor = Color.Transparent;

            if (!string.IsNullOrEmpty(tool.IconPath) && File.Exists(tool.IconPath))
            {
                try { using (var img = Image.FromFile(tool.IconPath)) icon.Image = new Bitmap(img, new Size(32, 32)); }
                catch { }
            }
            else
            {
                icon.Image = DefaultIcon(tool.Extension);
            }
            card.Controls.Add(icon);

            var name = new Label();
            name.Text = Trunc(tool.Name, 12);
            name.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            name.ForeColor = fg;
            name.Size = new Size(CARD_W - 56, 22);
            name.Location = new Point(50, 10);
            name.BackColor = Color.Transparent;
            card.Controls.Add(name);

            var desc = new Label();
            desc.Text = !string.IsNullOrEmpty(tool.Description) ? Trunc(tool.Description, 16) : tool.Extension;
            desc.Font = new Font("Microsoft YaHei UI", 8f);
            desc.ForeColor = sub;
            desc.Size = new Size(CARD_W - 24, 18);
            desc.Location = new Point(12, 48);
            desc.BackColor = Color.Transparent;
            card.Controls.Add(desc);

            var btn = new Button();
            btn.Text = "打开";
            btn.Font = new Font("Microsoft YaHei UI", 8f);
            btn.Size = new Size(44, 22);
            btn.Location = new Point(CARD_W - 58, 64);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 215);
            btn.FlatAppearance.BorderSize = 1;
            btn.BackColor = Color.FromArgb(0, 120, 215);
            btn.ForeColor = Color.White;
            btn.Cursor = Cursors.Hand;
            btn.Tag = tool;
            btn.Click += (s, e) => Launch(tool);
            card.Controls.Add(btn);

            card.MouseEnter += (s, e) => { card.BackColor = hover; accent.BackColor = Color.FromArgb(0, 150, 255); };
            card.MouseLeave += (s, e) => { card.BackColor = bg; accent.BackColor = Color.FromArgb(0, 120, 215); };
            card.Click += (s, e) => Launch(tool);

            var tip = new ToolTip();
            var t = tool.Name;
            if (!string.IsNullOrEmpty(tool.Description)) t += "\n" + tool.Description;
            tip.SetToolTip(card, t);

            return card;
        }

        private Image DefaultIcon(string ext)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                var bg = _dark ? Color.FromArgb(52, 52, 58) : Color.FromArgb(215, 220, 235);
                var fg = _dark ? Color.FromArgb(165, 165, 172) : Color.FromArgb(75, 75, 85);
                using (var b1 = new SolidBrush(bg))
                using (var b2 = new SolidBrush(fg))
                {
                    g.FillRectangle(b1, 1, 1, 30, 30);
                    var txt = string.IsNullOrEmpty(ext) ? "?" : ext.ToUpperInvariant();
                    if (txt.Length > 4) txt = txt.Substring(0, 4);
                    using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
                        g.DrawString(txt, f, b2, new RectangleF(1, 7, 30, 18), new StringFormat { Alignment = StringAlignment.Center });
                }
            }
            return bmp;
        }

        static string Trunc(string s, int n) { return string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        private void Launch(ToolItem tool)
        {
            var path = tool.EffectivePath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!File.Exists(path)) { MessageBox.Show("文件不存在: " + path, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                var psi = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = tool.EffectiveWorkingDir };
                Process.Start(psi);
            }
            catch (Exception ex) { MessageBox.Show("启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        public void Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) { LoadTools(); return; }
            _flow.Controls.Clear();
            var r = ToolCatalog.Search(q);
            _tools = r;
            if (r == null || r.Count == 0) { _statusLabel.Text = "  搜索无结果"; return; }
            ToolIconService.LoadIcons(r);
            foreach (var t in r) _flow.Controls.Add(MakeCard(t));
            _statusLabel.Text = "  搜索结果: " + r.Count + " 个工具";
        }

        public void ApplyTheme(bool dark)
        {
            _dark = dark;
            BackColor = dark ? Color.FromArgb(22, 22, 26) : Color.FromArgb(242, 242, 246);
            _scrollHost.BackColor = BackColor;
            _flow.BackColor = BackColor;
            _statusBar.BackColor = dark ? Color.FromArgb(28, 28, 32) : Color.FromArgb(232, 232, 237);
            _statusLabel.ForeColor = dark ? Color.FromArgb(120, 120, 128) : Color.FromArgb(110, 110, 118);
            LoadTools();
        }
    }
}
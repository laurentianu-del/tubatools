using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using TubaWinUi3.Compatible.Models;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible.Forms
{
    public class HardwarePage : UserControl
    {
        private Panel _loadingPanel;
        private Label _loadingLabel;
        private IReadOnlyList<HardwareInfoSection> _sections;
        private bool _dark = true;
        private bool _loaded;

        public HardwarePage()
        {
            BackColor = Color.FromArgb(22, 22, 26);
            _loadingPanel = new Panel();
            _loadingPanel.BackColor = BackColor;
            Controls.Add(_loadingPanel);

            _loadingLabel = new Label();
            _loadingLabel.Text = "正在读取硬件信息...";
            _loadingLabel.Font = new Font("Microsoft YaHei UI", 11f);
            _loadingLabel.ForeColor = Color.FromArgb(140, 140, 148);
            _loadingLabel.BackColor = Color.Transparent;
            _loadingPanel.Controls.Add(_loadingLabel);
        }

        public new void SetBounds(int x, int y, int w, int h)
        {
            base.SetBounds(x, y, w, h);
            _loadingPanel.SetBounds(0, 0, w, h);
            _loadingLabel.SetBounds(30, 30, 300, 30);
        }

        public void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var bw = new BackgroundWorker();
            bw.DoWork += (s, e) => { e.Result = HardwareInfoService.LoadAsync(); };
            bw.RunWorkerCompleted += (s, e) =>
            {
                if (e.Error != null) { _loadingLabel.Text = "加载失败: " + e.Error.Message; return; }
                _sections = e.Result as IReadOnlyList<HardwareInfoSection>;
                BuildUI();
            };
            bw.RunWorkerAsync();
        }

        private void BuildUI()
        {
            Controls.Clear();
            if (_sections == null) return;

            var bg = _dark ? Color.FromArgb(22, 22, 26) : Color.FromArgb(242, 242, 246);
            var cardBg = _dark ? Color.FromArgb(38, 38, 42) : Color.White;
            var secClr = _dark ? Color.FromArgb(200, 200, 206) : Color.FromArgb(30, 30, 36);
            var lblClr = _dark ? Color.FromArgb(120, 120, 128) : Color.FromArgb(100, 100, 108);
            var valClr = _dark ? Color.FromArgb(225, 225, 230) : Color.FromArgb(25, 25, 30);
            var divClr = _dark ? Color.FromArgb(50, 50, 55) : Color.FromArgb(225, 228, 238);

            var scroll = new Panel();
            scroll.AutoScroll = true;
            scroll.BackColor = bg;
            scroll.SetBounds(0, 0, Width, Height);
            Controls.Add(scroll);

            int top = 10;
            int rowW = Math.Max(Width - 60, 200);
            int pad = 20;

            foreach (var section in _sections)
            {
                var h = new Label();
                h.Text = section.Title;
                h.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
                h.ForeColor = secClr;
                h.AutoSize = true;
                h.SetBounds(pad, top, 300, 28);
                h.BackColor = Color.Transparent;
                scroll.Controls.Add(h);
                top += 36;

                foreach (var item in section.Items)
                {
                    var row = new Panel();
                    row.SetBounds(pad, top, rowW, 52);
                    row.BackColor = cardBg;
                    row.Cursor = Cursors.Hand;

                    var accent = new Panel();
                    accent.SetBounds(0, 0, 3, 52);
                    accent.BackColor = !string.IsNullOrEmpty(item.BrandKey) ? BrandColor(item.BrandKey) : Color.FromArgb(0, 120, 215);
                    row.Controls.Add(accent);

                    var lbl = new Label();
                    lbl.Text = item.Label;
                    lbl.Font = new Font("Microsoft YaHei UI", 9f);
                    lbl.ForeColor = lblClr;
                    lbl.SetBounds(16, 6, 200, 18);
                    lbl.AutoSize = true;
                    lbl.BackColor = Color.Transparent;
                    row.Controls.Add(lbl);

                    var val = new Label();
                    val.Text = item.Value;
                    val.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
                    val.ForeColor = valClr;
                    val.SetBounds(16, 26, rowW - 30, 20);
                    val.BackColor = Color.Transparent;
                    row.Controls.Add(val);

                    if (item.IsVerified)
                    {
                        var badge = new Label();
                        badge.Text = "已验证";
                        badge.Font = new Font("Microsoft YaHei UI", 7f);
                        badge.ForeColor = Color.FromArgb(0, 180, 80);
                        badge.SetBounds(rowW - 60, 6, 50, 16);
                        badge.BackColor = Color.Transparent;
                        row.Controls.Add(badge);
                    }

                    var tt = new ToolTip();
                    tt.SetToolTip(row, item.Label + ": " + item.Value + "\n双击复制");

                    var rowBg = cardBg;
                    var rowHv = _dark ? Color.FromArgb(48, 48, 54) : Color.FromArgb(235, 240, 252);
                    row.MouseEnter += (s, e) => { row.BackColor = rowHv; accent.BackColor = Color.FromArgb(0, 150, 255); };
                    row.MouseLeave += (s, e) => { row.BackColor = rowBg; accent.BackColor = !string.IsNullOrEmpty(item.BrandKey) ? BrandColor(item.BrandKey) : Color.FromArgb(0, 120, 215); };
                    row.DoubleClick += (s, e) => { try { Clipboard.SetText(item.Value); } catch { } };

                    scroll.Controls.Add(row);
                    top += 56;
                }

                var div = new Panel();
                div.SetBounds(pad, top, rowW, 1);
                div.BackColor = divClr;
                scroll.Controls.Add(div);
                top += 12;
            }

            scroll.AutoScrollMinSize = new Size(0, top + 20);
        }

        static Color BrandColor(string k)
        {
            if (k == null) return Color.FromArgb(0, 120, 215);
            switch (k)
            {
                case "intel": return Color.FromArgb(0, 130, 210);
                case "amd": return Color.FromArgb(220, 50, 50);
                case "nvidia": return Color.FromArgb(118, 185, 0);
                case "apple": return Color.FromArgb(160, 160, 160);
                case "qualcomm": return Color.FromArgb(60, 60, 180);
                default: return Color.FromArgb(0, 120, 215);
            }
        }

        public void ApplyTheme(bool dark)
        {
            _dark = dark;
            BackColor = dark ? Color.FromArgb(22, 22, 26) : Color.FromArgb(242, 242, 246);
            if (_loaded) BuildUI();
        }
    }
}
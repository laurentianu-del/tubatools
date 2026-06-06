using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MetroFramework;
using MetroFramework.Controls;
using TubaWinUi3.Compatible.Forms;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible
{
    public class MainForm : Form
    {
        private Panel _sidebar;
        private Panel _header;
        private Panel _content;
        private Panel _hLine;
        private Panel _sLine;
        private MetroTextBox _searchBox;
        private MetroButton _themeToggle;
        private Label _titleLabel;
        private FlowLayoutPanel _navList;
        private ToolListPage _toolListPage;
        private HardwarePage _hardwarePage;
        private int _selectedIdx = -1;
        private bool _dark = true;
        private List<Label> _navs = new List<Label>();

        const int HEADER_H = 50;
        const int SIDEBAR_W = 190;

        public MainForm()
        {
            Text = "图吧工具箱 - 兼容版";
            Size = new Size(1200, 800);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(22, 22, 26);
            FormBorderStyle = FormBorderStyle.Sizable;

            InitUI();
        }

        private void InitUI()
        {
            // === Header ===
            _header = new Panel();
            _header.BackColor = Color.FromArgb(28, 28, 32);
            Controls.Add(_header);

            _titleLabel = new Label();
            _titleLabel.Text = "  图吧工具箱 · 兼容版";
            _titleLabel.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            _titleLabel.ForeColor = Color.FromArgb(220, 220, 225);
            _titleLabel.BackColor = Color.Transparent;
            _header.Controls.Add(_titleLabel);

            _hLine = new Panel();
            _hLine.BackColor = Color.FromArgb(50, 50, 55);
            _header.Controls.Add(_hLine);

            _searchBox = new MetroTextBox();
            _searchBox.WaterMark = "搜索工具...";
            _searchBox.Theme = MetroThemeStyle.Dark;
            _searchBox.TextChanged += (s, e) =>
            {
                if (_toolListPage != null)
                {
                    _toolListPage.Search(_searchBox.Text.Trim());
                    if (_selectedIdx != 0) SelectNav(0);
                }
            };
            _header.Controls.Add(_searchBox);

            _themeToggle = new MetroButton();
            _themeToggle.Text = "主题";
            _themeToggle.Theme = MetroThemeStyle.Dark;
            _themeToggle.Style = MetroColorStyle.Blue;
            _themeToggle.Click += (s, e) => ToggleTheme();
            _header.Controls.Add(_themeToggle);

            // === Sidebar ===
            _sidebar = new Panel();
            _sidebar.BackColor = Color.FromArgb(32, 32, 36);
            Controls.Add(_sidebar);

            _sLine = new Panel();
            _sLine.BackColor = Color.FromArgb(50, 50, 55);
            _sidebar.Controls.Add(_sLine);

            _navList = new FlowLayoutPanel();
            _navList.FlowDirection = FlowDirection.TopDown;
            _navList.WrapContents = false;
            _navList.AutoScroll = true;
            _navList.BackColor = _sidebar.BackColor;
            _navList.Padding = new Padding(0, 8, 0, 8);
            _sidebar.Controls.Add(_navList);

            AddNav("全部工具", 0);
            AddNav("硬件信息", 1);

            var sep = new Label();
            sep.Text = "  工具分类";
            sep.Font = new Font("Microsoft YaHei UI", 8f);
            sep.ForeColor = Color.FromArgb(85, 85, 90);
            sep.Size = new Size(SIDEBAR_W - 2, 26);
            sep.TextAlign = ContentAlignment.BottomLeft;
            sep.BackColor = Color.Transparent;
            _navList.Controls.Add(sep);

            int idx = 2;
            foreach (var cat in ToolCatalog.GetCategories())
                AddNav(cat, idx++);

            // === Content ===
            _content = new Panel();
            _content.BackColor = Color.FromArgb(22, 22, 26);
            Controls.Add(_content);

            _toolListPage = new ToolListPage();
            _hardwarePage = new HardwarePage();

            DoLayout();
            SelectNav(0);
        }

        private void AddNav(string text, int idx)
        {
            var lbl = new Label();
            lbl.Text = "  " + text;
            lbl.Font = new Font("Microsoft YaHei UI", 9.5f);
            lbl.ForeColor = _dark ? Color.FromArgb(185, 185, 190) : Color.FromArgb(55, 55, 60);
            lbl.Size = new Size(SIDEBAR_W - 2, 34);
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.BackColor = Color.Transparent;
            lbl.Cursor = Cursors.Hand;
            lbl.Tag = idx;
            lbl.Click += (s, e) => SelectNav(idx);
            lbl.MouseEnter += (s, e) => { if (idx != _selectedIdx) lbl.BackColor = _dark ? Color.FromArgb(38, 38, 42) : Color.FromArgb(232, 236, 248); };
            lbl.MouseLeave += (s, e) => { if (idx != _selectedIdx) lbl.BackColor = Color.Transparent; };
            _navList.Controls.Add(lbl);
            _navs.Add(lbl);
        }

        private void SelectNav(int idx)
        {
            foreach (var lbl in _navs)
            {
                int i = (int)lbl.Tag;
                if (i == idx)
                {
                    lbl.BackColor = _dark ? Color.FromArgb(40, 40, 46) : Color.FromArgb(228, 234, 250);
                    lbl.ForeColor = Color.FromArgb(0, 120, 215);
                    lbl.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
                }
                else
                {
                    lbl.BackColor = Color.Transparent;
                    lbl.ForeColor = _dark ? Color.FromArgb(185, 185, 190) : Color.FromArgb(55, 55, 60);
                    lbl.Font = new Font("Microsoft YaHei UI", 9.5f);
                }
            }

            _selectedIdx = idx;
            _content.Controls.Clear();

            if (idx == 1)
            {
                _hardwarePage.EnsureLoaded();
                _content.Controls.Add(_hardwarePage);
            }
            else
            {
                string category = null;
                if (idx >= 2)
                {
                    var cats = ToolCatalog.GetCategories();
                    var catNavs = _navs.FindAll(n => (int)n.Tag >= 2);
                    int ci = idx - 2;
                    if (ci < catNavs.Count)
                    {
                        var t = catNavs[ci].Text.Trim();
                        foreach (var c in cats)
                            if (t == c) { category = c; break; }
                    }
                }
                _toolListPage.SetCategory(category);
                _content.Controls.Add(_toolListPage);
            }

            DoLayout();
        }

        private void DoLayout()
        {
            if (_header == null || _content == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _header.SetBounds(0, 0, w, HEADER_H);
            _titleLabel.SetBounds(0, 0, w - 350, HEADER_H);
            _hLine.SetBounds(0, HEADER_H - 1, w, 1);
            _searchBox.SetBounds(w - 320, 12, 240, 26);
            _themeToggle.SetBounds(w - 70, 12, 60, 26);

            _sidebar.SetBounds(0, HEADER_H, SIDEBAR_W, h - HEADER_H);
            _sLine.SetBounds(SIDEBAR_W - 1, 0, 1, h - HEADER_H);
            _navList.SetBounds(0, 0, SIDEBAR_W - 1, h - HEADER_H);

            int cx = SIDEBAR_W;
            int cy = HEADER_H;
            int cw = w - SIDEBAR_W;
            int ch = h - HEADER_H;
            _content.SetBounds(cx, cy, cw, ch);

            foreach (Control c in _content.Controls)
                c.SetBounds(0, 0, cw, ch);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        private void ToggleTheme()
        {
            _dark = !_dark;
            BackColor = _dark ? Color.FromArgb(22, 22, 26) : Color.FromArgb(242, 242, 246);
            _header.BackColor = _dark ? Color.FromArgb(28, 28, 32) : Color.White;
            _titleLabel.ForeColor = _dark ? Color.FromArgb(220, 220, 225) : Color.FromArgb(30, 30, 30);
            _hLine.BackColor = _dark ? Color.FromArgb(50, 50, 55) : Color.FromArgb(220, 225, 235);
            _sidebar.BackColor = _dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(248, 248, 252);
            _sLine.BackColor = _dark ? Color.FromArgb(50, 50, 55) : Color.FromArgb(220, 225, 235);
            _navList.BackColor = _sidebar.BackColor;
            _content.BackColor = _dark ? Color.FromArgb(22, 22, 26) : Color.FromArgb(242, 242, 246);

            SelectNav(_selectedIdx);
            if (_toolListPage != null) _toolListPage.ApplyTheme(_dark);
            if (_hardwarePage != null) _hardwarePage.ApplyTheme(_dark);
        }
    }
}
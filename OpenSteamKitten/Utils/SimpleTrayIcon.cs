using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace OpenSteamKitten.Utils
{
    /// <summary>
    /// 简单的系统托盘图标封装
    /// </summary>
    public class SimpleTrayIcon : IDisposable
    {
        private NotifyIcon? _notifyIcon;

        public SimpleTrayIcon(string text, Action onDoubleClick, Action onExit)
        {
            _notifyIcon = new NotifyIcon
            {
                Text = text,
                Visible = true,
                Icon = CreateCatIcon() // 使用自定义小猫图标
            };

            _notifyIcon.DoubleClick += (s, e) => onDoubleClick?.Invoke();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("退出", null, (s, e) => onExit?.Invoke());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private Icon CreateCatIcon()
        {
            // 创建一个 32x32 的位图
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Black);

                // 绘制白色圆形背景
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(26, 26, 26)))
                {
                    g.FillEllipse(brush, 2, 2, 28, 28);
                }

                // 绘制小猫 emoji
                using (Font font = new Font(new FontFamily("Segoe UI Emoji"), 18, System.Drawing.FontStyle.Regular))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString("🐱", font, textBrush, new RectangleF(0, 0, 32, 32), sf);
                }

                // 转换为图标
                IntPtr hIcon = bitmap.GetHicon();
                Icon icon = Icon.FromHandle(hIcon);
                return (Icon)icon.Clone();
            }
        }

        /// <summary>
        /// 追加一个可勾选的菜单项（插在"退出"前面）。状态由外部回调决定，
        /// 封装在此处避免调用方（MainWindow）引入 WinForms 依赖。
        /// </summary>
        public void AddCheckableItem(string text, Func<bool> getCurrent, Action<bool> onToggle)
        {
            var menu = _notifyIcon?.ContextMenuStrip;
            if (menu == null) return;

            var item = new ToolStripMenuItem(text);
            // 菜单打开时从真相源同步勾选状态
            menu.Opening += (s, e) => item.Checked = getCurrent();
            // 点击翻转状态并通知调用方
            item.Click += (s, e) =>
            {
                item.Checked = !item.Checked;
                onToggle(item.Checked);
            };
            // 插在"退出"前面 → [勾选项, 退出]
            menu.Items.Insert(0, item);
        }

        /// <summary>
        /// 追加一个动态文案菜单项（文案在每次打开时刷新，点击触发回调）。插在"退出"前面。
        /// </summary>
        public void AddDynamicItem(Func<string> getText, Action onClick)
        {
            var menu = _notifyIcon?.ContextMenuStrip;
            if (menu == null) return;

            var item = new ToolStripMenuItem();
            menu.Opening += (s, e) => item.Text = getText();
            item.Click += (s, e) => onClick();
            menu.Items.Insert(0, item);
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;
using CampusLogin.Core;

namespace CampusLogin.UI
{
    /// <summary>Clash / mihomo 等代理开着 TUN 模式时，引导用户给认证地址加直连规则。</summary>
    internal sealed class ClashHelpForm : Form
    {
        public ClashHelpForm(string tunAdapter, string portalBase)
        {
            Text = $"{AppInfo.Name} - 代理软件设置";
            Icon = Icons.App;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.Fill,
            };

            string detected = tunAdapter != null
                ? $"检测到代理软件的 TUN 虚拟网卡：{tunAdapter}。\r\n\r\n"
                : "";
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Text = detected
                     + "如果你此时在校园网内，却一直显示\"连不上认证服务器\"，多半是 Clash Verge 等代理开着 TUN 模式，"
                     + "把 2.2.2.2 转发给了代理节点。本程序没法绕过 TUN，需要在代理软件里给认证地址加上直连规则：",
                Margin = new Padding(0, 0, 0, 8),
            });

            var rules = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints + 0.5f),
                Text = ProxyDiagnostics.BuildDirectRules(portalBase),
                Width = 460,
                Height = 70,
                BackColor = SystemColors.Window,
            };
            panel.Controls.Add(rules);

            var copy = new Button { Text = "复制规则", AutoSize = true, Margin = new Padding(0, 6, 0, 8) };
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(rules.Text);
                    copy.Text = "已复制 ✓";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"复制失败：{ex.Message}", AppInfo.Name);
                }
            };
            panel.Controls.Add(copy);

            panel.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Text = "Clash Verge 的操作步骤：\r\n"
                     + "  1. 打开 Clash Verge →「订阅」页\r\n"
                     + "  2. 右键当前使用的订阅 →「编辑规则」\r\n"
                     + "  3. 把上面的规则粘贴到「前置规则」（必须在最前面）→ 保存\r\n\r\n"
                     + "其他代理软件（v2rayN、sing-box 等）也是同样的思路：让 2.2.2.2 和认证服务器走直连。"
                     + "加好以后，在托盘图标菜单里点「立即检查」即可。",
                Margin = new Padding(0, 0, 0, 10),
            });

            var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
            panel.Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;

            Controls.Add(panel);
        }
    }
}

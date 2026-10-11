// 自定义确认对话框：只有「是/否」两个按钮；点右上角 X、按 Esc 均等同「否」。
using System.Drawing;
using System.Windows.Forms;

namespace AutoDateShift
{
    internal static class Ui
    {
        public static bool Confirm(IWin32Window owner, string text, string title)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(440, 150);
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var lbl = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = text,
                    Padding = new Padding(16, 14, 16, 4),
                    AutoSize = false
                };
                var panel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 42,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(0, 4, 10, 4)
                };
                var btnNo = new Button { Text = Lang.T("否(N)"), DialogResult = DialogResult.No, Width = 88, Height = 28, Margin = new Padding(6, 3, 3, 3) };
                var btnYes = new Button { Text = Lang.T("是(Y)"), DialogResult = DialogResult.Yes, Width = 88, Height = 28, Margin = new Padding(6, 3, 3, 3) };
                panel.Controls.Add(btnNo);
                panel.Controls.Add(btnYes);
                f.Controls.Add(lbl);
                f.Controls.Add(panel);
                f.AcceptButton = btnYes;
                f.CancelButton = btnNo;
                f.DialogResult = DialogResult.No; // 右上角 X = 取消（否）
                return f.ShowDialog(owner) == DialogResult.Yes;
            }
        }
    }
}

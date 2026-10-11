// GUI 主窗体 v1.8：自检间隔可配置（预设+自定义秒数）/ 每日零点单次自检模式
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace AutoDateShift
{
    internal class MainForm : Form
    {
        private const string Version = CsvLogger.Version;

        private static readonly Padding LblMargin = new Padding(4, 9, 4, 4);
        private static readonly Padding InputMargin = new Padding(4, 6, 4, 4);
        private static readonly Padding BtnMargin = new Padding(4, 5, 4, 4);
        private static readonly Padding ChkMargin = new Padding(4, 8, 4, 4);
        private static readonly Padding PicMargin = new Padding(4, 7, 4, 4);

        private readonly bool _isAdmin;
        private TabControl _tabs;
        private List<ConfigEntry> _entries = new List<ConfigEntry>();
        private Settings _settings;
        private bool _suppressEvents;
        private bool _forceExit;
        private NotifyIcon _tray;
        private Bitmap _appIconBmp;
        private EventWaitHandle _showEvent;

        // 页签1
        private DataGridView _grid;
        private DateTimePicker _dtpDate, _dtpTarget;
        private ComboBox _cmbMode, _cmbWeekday;
        private TextBox _txtNote;
        private Label _lblPreview;
        private Button _btnUpsert, _btnDelete, _btnSave, _btnImport, _btnDiscard;

        // 页签2
        private Label _lblSysTime, _lblRealTime, _lblOffset, _lblTodayEntry, _lblAdmin, _lblService, _lblDataDirPath;
        private PictureBox _picService, _picShield;
        private CheckBox _chkAutostart, _chkCloseToTray, _chkUseNtp, _chkAdminRestart, _chkDailyOnce;
        private ComboBox _cmbNtp, _cmbPolicy, _cmbLang, _cmbInterval;
        private TextBox _txtInterval;
        private Label _lblSec;
        private DateTimePicker _dtpCalib;
        private Button _btnTick, _btnCalib, _btnService, _btnChangeDir, _btnResetDir;
        private System.Windows.Forms.Timer _clockTimer, _statusTimer;

        // 页签3
        private ComboBox _cmbLogFile;
        private TextBox _txtFilter;
        private TextBox _logView;

        public MainForm()
        {
            _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
            _settings = JsonStore.Read<Settings>(Paths.SettingsFile);
            if (string.IsNullOrWhiteSpace(_settings.NtpServer)) _settings.NtpServer = "ntp.aliyun.com";
            if (string.IsNullOrWhiteSpace(_settings.ServicePolicy)) _settings.ServicePolicy = "Memory";
            if (string.IsNullOrWhiteSpace(_settings.Language)) _settings.Language = "zh";
            if (_settings.CheckIntervalSeconds <= 0) _settings.CheckIntervalSeconds = 60;
            Lang.Current = _settings.Language;

            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(920, 640);
            MinimumSize = new Size(860, 560);
            Font = new Font("Microsoft YaHei UI", 9F);

            _appIconBmp = IconFactory.Clock(32);
            IntPtr hIcon = _appIconBmp.GetHicon();
            Icon = Icon.FromHandle(hIcon);

            _tray = new NotifyIcon { Icon = Icon, Visible = true };
            _tray.DoubleClick += delegate { ShowFromTray(); };

            BuildUi();

            if (!_isAdmin)
            {
                MessageBox.Show(T("当前为非管理员模式：仅可查看规则、状态与日志，无法修改。\n\n如需管理，请到「运行状态」页勾选「以管理员身份重新启动」。"),
                    T("非管理员模式"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _clockTimer.Tick += delegate { UpdateClockLabels(); };
            _clockTimer.Start();
            _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statusTimer.Tick += delegate { RefreshStatus(); };
            _statusTimer.Start();

            _showEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Program.GuiShowEventName);
            var t = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        _showEvent.WaitOne();
                        _showEvent.Reset();
                        BeginInvoke((MethodInvoker)delegate { ShowFromTray(); });
                    }
                    catch { return; }
                }
            }) { IsBackground = true };
            t.Start();
        }

        private static string T(string zh) { return Lang.T(zh); }

        /// <summary>构建/重建全部界面（语言切换时调用，保持当前页签）</summary>
        private void BuildUi()
        {
            int keepTab = _tabs != null ? _tabs.SelectedIndex : 0;
            Controls.Clear();
            SetTitle();
            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(BuildRuleTab());
            _tabs.TabPages.Add(BuildStatusTab());
            _tabs.TabPages.Add(BuildLogTab());
            _tabs.TabPages.Add(BuildAboutTab());
            _tabs.SelectedIndexChanged += delegate
            {
                if (_tabs.SelectedIndex == 2) { ReloadLogFiles(); LoadLog(); } // 进入日志页自动刷新
            };
            if (keepTab >= 0 && keepTab < _tabs.TabPages.Count) _tabs.SelectedIndex = keepTab;
            Controls.Add(_tabs);
            BuildTrayMenu();
            LoadConfigToGrid();
            UpdateModeControls();
            UpdateCalibControlStates();
            UpdateIntervalControlStates();
            RefreshStatus();
            if (!_isAdmin) ApplyReadOnlyMode();
        }

        private void SetTitle()
        {
            Text = T("系统日期自动切换 - 管理工具 ") + Version + "（AutoDateShift）" +
                   T(_isAdmin ? "［管理员］" : "［非管理员·只读］");
        }

        /// <summary>星期显示（界面语言；日志/CSV 始终中文）</summary>
        private static string Wd(int num)
        {
            return Lang.IsEn ? Weekdays.NameEn(num) : Weekdays.Name(num);
        }

        private string TargetDisp(ConfigEntry e)
        {
            return e.IsDateMode ? e.TargetDate : Wd(e.TargetWeekday);
        }

        // ==================== 托盘 ====================
        private void BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(T("打开管理界面"), null, delegate { ShowFromTray(); });
            menu.Items.Add(T("退出管理界面（不影响后台服务）"), null, delegate
            {
                _forceExit = true;
                Close();
            });
            _tray.ContextMenuStrip = menu;
            _tray.Text = T("AutoDateShift 系统日期自动切换");
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit && _settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosing(e);
        }

        // ==================== 页签1：调整规则 ====================
        private TabPage BuildRuleTab()
        {
            var page = new TabPage(T("调整规则"));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _btnUpsert = MkIconBtn(T("添加/更新"), IconFactory.Plus(16, IconFactory.Green), OnUpsert);
            _btnDelete = MkIconBtn(T("删除选中"), IconFactory.Trash(16, IconFactory.Red), OnDelete);
            _btnSave = MkIconBtn(T("保存规则"), IconFactory.Save(16, IconFactory.Blue), OnSave);
            _btnImport = MkIconBtn(T("导入JSON"), IconFactory.ArrowIn(16, IconFactory.Dark), OnImport);
            _btnDiscard = MkIconBtn(T("放弃修改"), IconFactory.Refresh(16, IconFactory.Orange), OnDiscard);
            toolbar.Controls.Add(_btnUpsert);
            toolbar.Controls.Add(_btnDelete);
            toolbar.Controls.Add(_btnSave);
            toolbar.Controls.Add(_btnImport);
            toolbar.Controls.Add(MkIconBtn(T("导出JSON"), IconFactory.ArrowOut(16, IconFactory.Dark), OnExport));
            toolbar.Controls.Add(_btnDiscard);
            layout.Controls.Add(toolbar, 0, 0);

            var editors = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            editors.Controls.Add(MkLbl(T("日期:")));
            _dtpDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 105, Margin = InputMargin };
            _dtpDate.ValueChanged += delegate { UpdateRulePreview(); };
            editors.Controls.Add(_dtpDate);
            editors.Controls.Add(MkLbl(T("调整为:")));
            _cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Margin = InputMargin };
            _cmbMode.Items.AddRange(new object[] { T("按星期"), T("按日期") });
            _cmbMode.SelectedIndex = 0;
            _cmbMode.SelectedIndexChanged += delegate { UpdateModeControls(); UpdateRulePreview(); };
            editors.Controls.Add(_cmbMode);
            _cmbWeekday = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 82, Margin = InputMargin };
            for (int i = 1; i <= 7; i++) _cmbWeekday.Items.Add(Wd(i));
            _cmbWeekday.SelectedIndex = 2;
            _cmbWeekday.SelectedIndexChanged += delegate { UpdateRulePreview(); };
            editors.Controls.Add(_cmbWeekday);
            _dtpTarget = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 105, Margin = InputMargin, Visible = false };
            _dtpTarget.ValueChanged += delegate { UpdateRulePreview(); };
            editors.Controls.Add(_dtpTarget);
            editors.Controls.Add(MkLbl(T("备注(可空):")));
            _txtNote = new TextBox { Width = 200, Margin = InputMargin };
            editors.Controls.Add(_txtNote);
            _lblPreview = new Label { AutoSize = true, ForeColor = IconFactory.Blue, Margin = LblMargin };
            editors.Controls.Add(_lblPreview);
            layout.Controls.Add(editors, 0, 1);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };
            SetupGrid(_grid);
            _grid.Columns.Add("date", T("真实日期"));
            _grid.Columns.Add("weekday", T("当天星期"));
            _grid.Columns.Add("target", T("调整为"));
            _grid.Columns.Add("note", T("备注"));
            _grid.SelectionChanged += delegate { LoadSelectedToEditors(); };
            _grid.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (_grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None)
                    _grid.ClearSelection();
            };
            layout.Controls.Add(_grid, 0, 2);

            page.Controls.Add(layout);
            return page;
        }

        private void UpdateModeControls()
        {
            bool dateMode = _cmbMode.SelectedIndex == 1;
            _cmbWeekday.Visible = !dateMode;
            _dtpTarget.Visible = dateMode;
        }

        private void UpdateRulePreview()
        {
            DateTime d = _dtpDate.Value.Date;
            string arrow = Lang.IsEn ? "-> system will show " : "→ 系统将显示为 ";
            string offsetW = Lang.IsEn ? ", offset " : "，偏移 ";
            string dayW = Lang.IsEn ? "d)" : " 天）";
            if (_cmbMode.SelectedIndex == 1)
            {
                DateTime td = _dtpTarget.Value.Date;
                int dd = (int)(td - d).TotalDays;
                _lblPreview.Text = dd == 0
                    ? T("（与当天相同，无需调整）")
                    : arrow + td.ToString("yyyy-MM-dd") + "（" + Wd(Weekdays.Num(td)) + offsetW + (dd > 0 ? "+" : "") + dd + dayW;
                if (Lang.IsEn) _lblPreview.Text = _lblPreview.Text.Replace("（", "(");
                return;
            }
            int target = _cmbWeekday.SelectedIndex + 1;
            int diff = ShiftEngine.NearestDayDiff(target, Weekdays.Num(d));
            if (diff == 0) { _lblPreview.Text = T("（与当天星期相同，无需调整）"); return; }
            var fd = d.AddDays(diff);
            _lblPreview.Text = arrow + fd.ToString("yyyy-MM-dd") + "（" + Wd(Weekdays.Num(fd)) + offsetW + (diff > 0 ? "+" : "") + diff + dayW;
            if (Lang.IsEn) _lblPreview.Text = _lblPreview.Text.Replace("（", "(");
        }

        private void LoadConfigToGrid()
        {
            Config cfg;
            if (!JsonStore.TryRead(Paths.ConfigFile, out cfg) && File.Exists(Paths.ConfigFile))
            {
                CsvLogger.Error("GUI", "Failed to parse config.json, rules not loaded");
                MessageBox.Show(T("config.json 解析失败，请检查文件或从 config.json.bak 恢复。"), T("错误"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            _entries = cfg.Entries;
            _entries.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            _grid.Rows.Clear();
            foreach (var e in _entries)
            {
                DateTime d;
                string wd = DateTime.TryParseExact(e.Date, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out d) ? Wd(Weekdays.Num(d)) : "?";
                _grid.Rows.Add(e.Date, wd, TargetDisp(e), e.Note);
            }
            _grid.ClearSelection();
        }

        private void LoadSelectedToEditors()
        {
            if (_grid.SelectedRows.Count == 0) return;
            string key = Convert.ToString(_grid.SelectedRows[0].Cells[0].Value);
            var en = _entries.Find(x => x.Date == key);
            if (en == null) return;
            DateTime d;
            if (DateTime.TryParseExact(en.Date, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out d))
                _dtpDate.Value = d;
            if (en.IsDateMode)
            {
                _cmbMode.SelectedIndex = 1;
                DateTime td;
                if (DateTime.TryParseExact(en.TargetDate, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out td))
                    _dtpTarget.Value = td;
            }
            else
            {
                _cmbMode.SelectedIndex = 0;
                if (en.TargetWeekday >= 1 && en.TargetWeekday <= 7)
                    _cmbWeekday.SelectedIndex = en.TargetWeekday - 1;
            }
            _txtNote.Text = en.Note;
            UpdateModeControls();
        }

        private void OnUpsert(object s, EventArgs e)
        {
            DateTime d = _dtpDate.Value.Date;
            string key = d.ToString("yyyy-MM-dd");
            bool dateMode = _cmbMode.SelectedIndex == 1;

            var entry = _entries.Find(x => x.Date == key);
            bool isNew = entry == null;
            if (isNew) { entry = new ConfigEntry { Date = key }; _entries.Add(entry); }

            if (dateMode)
            {
                DateTime td = _dtpTarget.Value.Date;
                if (td == d)
                {
                    if (isNew) _entries.Remove(entry);
                    MessageBox.Show(T("目标日期与当天相同，无需调整。"), T("提示"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                entry.Mode = "date";
                entry.TargetDate = td.ToString("yyyy-MM-dd");
                entry.TargetWeekday = 0;
            }
            else
            {
                int target = _cmbWeekday.SelectedIndex + 1;
                if (target == Weekdays.Num(d))
                {
                    if (isNew) _entries.Remove(entry);
                    MessageBox.Show(T("目标星期与当天相同，无需调整。"), T("提示"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                entry.Mode = null;
                entry.TargetDate = null;
                entry.TargetWeekday = target;
            }
            entry.Note = _txtNote.Text.Trim();
            _entries.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
            RefreshGrid();
        }

        private void OnDelete(object s, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            string key = Convert.ToString(_grid.SelectedRows[0].Cells[0].Value);
            _entries.RemoveAll(x => x.Date == key);
            RefreshGrid();
        }

        private void OnDiscard(object s, EventArgs e)
        {
            if (Ui.Confirm(this, T("将放弃所有未保存的修改，还原为上次保存的规则。\n\n确定？"), T("放弃修改")))
                LoadConfigToGrid();
        }

        private void OnSave(object s, EventArgs e)
        {
            try
            {
                Paths.EnsureDirs();
                var oldCfg = JsonStore.Read<Config>(Paths.ConfigFile);
                JsonStore.Write(Paths.ConfigFile, new Config { Entries = _entries });

                int changes = 0;
                foreach (var n in _entries)
                {
                    var o = oldCfg.Entries.Find(x => x.Date == n.Date);
                    if (o == null) { LogRule("Rule added", n, null); changes++; }
                    else if (RulesDiffer(o, n)) { LogRule("Rule modified", n, o); changes++; }
                }
                foreach (var o in oldCfg.Entries)
                    if (_entries.Find(x => x.Date == o.Date) == null) { LogRule("Rule deleted", o, null); changes++; }
                if (changes == 0)
                    CsvLogger.Info("GUI", "Rules saved: no changes");

                MessageBox.Show(Lang.IsEn
                        ? "Saved (" + changes + " change(s) logged). The background service will apply them at its next check."
                        : "已保存（" + changes + " 项变更已记录日志）。后台服务会在下一次自检时按新规则执行。",
                    T("成功"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshStatus();
            }
            catch (Exception ex)
            {
                CsvLogger.Error("GUI", "Failed to save rules: " + ex.Message);
                MessageBox.Show(T("保存失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool RulesDiffer(ConfigEntry o, ConfigEntry n)
        {
            return o.TargetWeekday != n.TargetWeekday
                || (o.Mode ?? "") != (n.Mode ?? "")
                || (o.TargetDate ?? "") != (n.TargetDate ?? "")
                || (o.Note ?? "") != (n.Note ?? "");
        }

        /// <summary>规则变更留痕（英文记录）</summary>
        private void LogRule(string action, ConfigEntry n, ConfigEntry old)
        {
            string detail;
            if (action == "Rule added")
                detail = "target=" + n.TargetDisplayEn() + NoteText(n.Note);
            else if (action == "Rule deleted")
                detail = "removed target=" + n.TargetDisplayEn() + NoteText(n.Note);
            else
            {
                detail = "";
                if (old.TargetDisplay() != n.TargetDisplay())
                    detail += "target " + old.TargetDisplayEn() + " -> " + n.TargetDisplayEn() + "; ";
                if ((old.Note ?? "") != (n.Note ?? ""))
                    detail += "note \"" + (old.Note ?? "") + "\" -> \"" + (n.Note ?? "") + "\"";
            }
            CsvLogger.Info("GUI", action + ": date=" + n.Date +
                (string.IsNullOrEmpty(detail) ? "" : ", " + detail.TrimEnd(' ', ';')));
        }

        private static string NoteText(string note)
        {
            return string.IsNullOrEmpty(note) ? "" : ", note: " + note;
        }

        private void OnImport(object s, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = T("JSON 文件|*.json"), Title = T("导入调整规则") })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Config));
                    Config cfg;
                    using (var fs = File.OpenRead(dlg.FileName))
                        cfg = (Config)ser.ReadObject(fs);
                    if (cfg == null || cfg.Entries == null) throw new FormatException("empty file");
                    foreach (var en in cfg.Entries) en.GetDate();
                    _entries = cfg.Entries;
                    _entries.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
                    RefreshGrid();
                    MessageBox.Show(Lang.IsEn
                            ? "Imported " + _entries.Count + " rule(s). Click \"Save Rules\" to apply (changes will be logged)."
                            : "导入成功（" + _entries.Count + " 条），请确认后点击「保存规则」生效（变更会记录日志）。",
                        T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    CsvLogger.Error("GUI", "Failed to import rules: " + ex.Message);
                    MessageBox.Show(T("导入失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExport(object s, EventArgs e)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = T("JSON 文件|*.json"),
                FileName = (Lang.IsEn ? "rules_" : "调整规则_") + DateTime.Now.ToString("yyyyMMdd") + ".json"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    JsonStore.Write(dlg.FileName, new Config { Entries = _entries });
                    MessageBox.Show(T("已导出: ") + dlg.FileName, T("成功"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    CsvLogger.Error("GUI", "Failed to export rules: " + ex.Message);
                    MessageBox.Show(T("导出失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ==================== 页签2：运行状态（左列时间状态整列，右列运行环境+常规设置） ====================
        private TabPage BuildStatusTab()
        {
            var page = new TabPage(T("运行状态"));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- 左列：时间状态（含校时，整列高度） ----
            var grpTime = MkGroup(T("时间状态"));
            var timeBox = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int r = 0;
            _lblSysTime = AddRow(grid, r++, T("当前系统时间:"));
            _lblRealTime = AddRow(grid, r++, T("回推真实时间:"));
            _lblOffset = AddRow(grid, r++, T("已应用偏移:"));
            _lblTodayEntry = AddRow(grid, r++, T("今日调整规则:"));
            timeBox.Controls.Add(grid);
            var tickRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _btnTick = MkIconBtn(T("立即自检"), IconFactory.Refresh(16, IconFactory.Blue), OnTickNow);
            tickRow.Controls.Add(_btnTick);
            timeBox.Controls.Add(tickRow);
            // 自检设置：每日单次模式勾选 + 间隔选择
            var dailyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _chkDailyOnce = new CheckBox { Text = T("仅在每日零点后自检一次"), AutoSize = true, Checked = _settings.DailyOnce, Margin = ChkMargin };
            _chkDailyOnce.CheckedChanged += OnDailyOnceChanged;
            dailyRow.Controls.Add(_chkDailyOnce);
            timeBox.Controls.Add(dailyRow);
            var ivRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            ivRow.Controls.Add(MkLbl(T("自检间隔:")));
            _cmbInterval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135, Margin = InputMargin };
            _cmbInterval.Items.AddRange(new object[] { T("30 秒"), T("1 分钟（默认）"), T("2 分钟"), T("5 分钟"), T("10 分钟"), T("30 分钟"), T("自定义") });
            _cmbInterval.SelectedIndex = IntervalToIndex(_settings.CheckIntervalSeconds);
            _cmbInterval.SelectedIndexChanged += OnIntervalChanged;
            ivRow.Controls.Add(_cmbInterval);
            _txtInterval = new TextBox { Width = 70, Margin = InputMargin, Text = _settings.CheckIntervalSeconds.ToString() };
            _txtInterval.Leave += OnIntervalCustom;
            ivRow.Controls.Add(_txtInterval);
            _lblSec = MkLbl(T("秒"));
            ivRow.Controls.Add(_lblSec);
            timeBox.Controls.Add(ivRow);
            var ntpRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _chkUseNtp = new CheckBox { Text = T("使用网络校时(NTP)"), AutoSize = true, Checked = _settings.UseNtp, Margin = ChkMargin };
            _chkUseNtp.CheckedChanged += OnSettingsChanged;
            ntpRow.Controls.Add(_chkUseNtp);
            _cmbNtp = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDown, Margin = InputMargin };
            _cmbNtp.Items.AddRange(new object[]
            {
                "ntp.aliyun.com", "ntp.tencent.com", "time.windows.com",
                "cn.pool.ntp.org", "ntp.ntsc.ac.cn"
            });
            _cmbNtp.Text = _settings.NtpServer;
            _cmbNtp.SelectedIndexChanged += OnSettingsChanged;
            _cmbNtp.Leave += OnSettingsChanged;
            ntpRow.Controls.Add(_cmbNtp);
            timeBox.Controls.Add(ntpRow);
            var manualRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _dtpCalib = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm:ss",
                ShowUpDown = true,
                Width = 165,
                Margin = InputMargin,
                Value = DateTime.Now
            };
            manualRow.Controls.Add(_dtpCalib);
            _btnCalib = MkIconBtn(T("执行校时"), IconFactory.Clock(16), OnCalibrate);
            manualRow.Controls.Add(_btnCalib);
            timeBox.Controls.Add(manualRow);
            MakeAdaptive(timeBox);
            grpTime.Controls.Add(timeBox);
            root.Controls.Add(grpTime, 0, 0);

            // ---- 右列：上「运行环境」下「常规设置」 ----
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var grpEnv = MkGroup(T("运行环境"));
            var envBox = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var rowAdmin = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _picShield = new PictureBox
            {
                Size = new Size(18, 18),
                Margin = PicMargin,
                Image = IconFactory.Shield(18, _isAdmin ? IconFactory.Green : IconFactory.Red)
            };
            rowAdmin.Controls.Add(_picShield);
            _lblAdmin = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = LblMargin };
            _lblAdmin.ForeColor = _isAdmin ? Color.DarkGreen : Color.DarkRed;
            _lblAdmin.Text = T(_isAdmin
                ? "当前以管理员身份运行，可执行全部管理操作。"
                : "当前为非管理员模式（只读），无法修改规则/校时/服务设置。");
            rowAdmin.Controls.Add(_lblAdmin);
            envBox.Controls.Add(rowAdmin);
            if (!_isAdmin)
            {
                _chkAdminRestart = new CheckBox
                {
                    Text = T("以管理员身份重新启动（会弹出系统 UAC 确认框）"),
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                    Margin = ChkMargin
                };
                _chkAdminRestart.CheckedChanged += OnAdminRestartToggled;
                envBox.Controls.Add(_chkAdminRestart);
            }
            var svcRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _picService = new PictureBox { Size = new Size(18, 18), Margin = PicMargin };
            svcRow.Controls.Add(_picService);
            _lblService = new Label { Text = "-", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = LblMargin };
            svcRow.Controls.Add(_lblService);
            _btnService = MkIconBtn(T("启动"), IconFactory.Play(16, IconFactory.Green), OnServiceToggle);
            svcRow.Controls.Add(_btnService);
            envBox.Controls.Add(svcRow);
            var polRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            polRow.Controls.Add(MkLbl(T("断电重启后:")));
            _cmbPolicy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Margin = InputMargin };
            _cmbPolicy.Items.AddRange(new object[] { T("记忆上次状态"), T("自动开启"), T("保持关闭") });
            _cmbPolicy.SelectedIndex = _settings.ServicePolicy == "AlwaysOn" ? 1 : (_settings.ServicePolicy == "AlwaysOff" ? 2 : 0);
            _cmbPolicy.SelectedIndexChanged += OnPolicyChanged;
            polRow.Controls.Add(_cmbPolicy);
            envBox.Controls.Add(polRow);
            MakeAdaptive(envBox);
            grpEnv.Controls.Add(envBox);
            right.Controls.Add(grpEnv, 0, 0);

            var grpOpt = MkGroup(T("常规设置"));
            var optBox = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            _chkAutostart = new CheckBox { Text = T("开机自启（本工具）"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = ChkMargin };
            _chkAutostart.CheckedChanged += OnAutostartToggled;
            optBox.Controls.Add(_chkAutostart);
            _chkCloseToTray = new CheckBox { Text = T("关闭窗口时最小化到任务栏托盘"), AutoSize = true, Checked = _settings.CloseToTray, Margin = ChkMargin };
            _chkCloseToTray.CheckedChanged += OnSettingsChanged;
            optBox.Controls.Add(_chkCloseToTray);
            var langRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            langRow.Controls.Add(MkLbl(T("语言/Language:")));
            _cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105, Margin = InputMargin };
            _cmbLang.Items.AddRange(new object[] { "简体中文", "English" });
            _cmbLang.SelectedIndex = Lang.IsEn ? 1 : 0;
            _cmbLang.SelectedIndexChanged += OnLanguageChanged;
            langRow.Controls.Add(_cmbLang);
            optBox.Controls.Add(langRow);
            _lblDataDirPath = new Label { Text = Paths.DataDir, AutoSize = true, ForeColor = Color.DimGray, Margin = LblMargin };
            optBox.Controls.Add(_lblDataDirPath);
            var dirBtns = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _btnChangeDir = MkIconBtn(T("更改目录"), IconFactory.Folder(16, IconFactory.Blue), OnChangeDataDir);
            _btnResetDir = MkIconBtn(T("恢复默认"), IconFactory.Refresh(16, IconFactory.Gray), OnResetDataDir);
            dirBtns.Controls.Add(_btnChangeDir);
            dirBtns.Controls.Add(_btnResetDir);
            dirBtns.Controls.Add(MkIconBtn(T("打开数据目录"), IconFactory.Folder(16, IconFactory.Dark), delegate { OpenDir(Paths.DataDir); }));
            optBox.Controls.Add(dirBtns);
            MakeAdaptive(optBox);
            grpOpt.Controls.Add(optBox);
            right.Controls.Add(grpOpt, 0, 1);

            root.Controls.Add(right, 1, 0);
            page.Controls.Add(root);
            return page;
        }

        private GroupBox MkGroup(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Padding = new Padding(8, 4, 8, 8),
                Font = new Font(Font, FontStyle.Bold)
            };
        }

        /// <summary>
        /// 组内控件随容器宽度自适应：长文本（如英文）自动换行而非裁切，
        /// 任何语言都无需重新排版。在组内 FlowLayoutPanel 填充完毕后调用。
        /// </summary>
        private static void MakeAdaptive(FlowLayoutPanel box)
        {
            box.HandleCreated += delegate { FitWidth(box); };
            box.Layout += delegate { FitWidth(box); };
        }

        private static void FitWidth(FlowLayoutPanel box)
        {
            int w = box.ClientSize.Width - 24;
            if (w < 80) return;
            foreach (Control c in box.Controls)
            {
                if (c.MaximumSize.Width == w) continue; // 防止重复触发布局
                c.MaximumSize = new Size(w, 0);
                var row = c as FlowLayoutPanel;
                if (row != null)
                {
                    foreach (Control inner in row.Controls)
                        if (inner is Label || inner is CheckBox)
                            inner.MaximumSize = new Size(w - 30, 0);
                }
                var grid = c as TableLayoutPanel;
                if (grid != null)
                {
                    foreach (Control inner in grid.Controls)
                        if (inner is Label)
                            inner.MaximumSize = new Size(w - 140, 0); // 扣除标签列宽度，值列文本可换行
                }
            }
        }

        private Label MkLbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = LblMargin };
        }

        private Label AddRow(TableLayoutPanel grid, int row, string title)
        {
            grid.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 6, 4, 4) }, 0, row);
            var val = new Label { Text = "-", AutoSize = true, Margin = new Padding(4, 6, 4, 4) };
            grid.Controls.Add(val, 1, row);
            return val;
        }

        private void ApplyReadOnlyMode()
        {
            Control[] rw = { _dtpDate, _cmbMode, _cmbWeekday, _dtpTarget, _txtNote, _chkAutostart, _cmbPolicy,
                             _chkUseNtp, _cmbNtp, _dtpCalib, _chkDailyOnce, _cmbInterval, _txtInterval,
                             _btnUpsert, _btnDelete, _btnSave, _btnImport, _btnDiscard,
                             _btnTick, _btnCalib, _btnService, _btnChangeDir, _btnResetDir };
            foreach (var c in rw) c.Enabled = false;
        }

        private void OnLanguageChanged(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            _settings.Language = _cmbLang.SelectedIndex == 1 ? "en" : "zh";
            SaveSettings();
            Lang.Current = _settings.Language;
            BuildUi(); // 重建界面即时生效
        }

        private void UpdateClockLabels()
        {
            var state = JsonStore.Read<State>(Paths.StateFile);
            DateTime sys = DateTime.Now;
            DateTime real = sys.AddSeconds(-state.AppliedOffsetSeconds);
            _lblSysTime.Text = sys.ToString("yyyy-MM-dd HH:mm:ss") + "（" + Wd(Weekdays.Num(sys)) + "）";
            _lblRealTime.Text = real.ToString("yyyy-MM-dd HH:mm:ss") + "（" + Wd(Weekdays.Num(real)) + "）";
            if (Lang.IsEn)
            {
                _lblSysTime.Text = _lblSysTime.Text.Replace("（", "(").Replace("）", ")");
                _lblRealTime.Text = _lblRealTime.Text.Replace("（", "(").Replace("）", ")");
            }
            long days = state.AppliedOffsetSeconds / ShiftEngine.SecondsPerDay;
            if (state.AppliedOffsetSeconds == 0)
            {
                _lblOffset.Text = T("无（真实日期）");
            }
            else
            {
                _lblOffset.Text = Lang.IsEn
                    ? days + " day(s) shifted (system date " + (days < 0 ? "moved back " : "moved forward ") + Math.Abs(days) + " day(s))"
                    : days + " 天（调整中，系统日期" + (days < 0 ? "提前" : "推迟") + Math.Abs(days) + "天）";
            }
            _lblOffset.ForeColor = state.AppliedOffsetSeconds == 0 ? Color.DarkGreen : Color.DarkRed;
        }

        private void RefreshStatus()
        {
            try
            {
                UpdateClockLabels();
                var state = JsonStore.Read<State>(Paths.StateFile);
                DateTime real = DateTime.Now.AddSeconds(-state.AppliedOffsetSeconds);

                Config cfg;
                string todayText = T("无");
                if (JsonStore.TryRead(Paths.ConfigFile, out cfg))
                {
                    var e = cfg.FindByDate(real.Date);
                    if (e != null)
                    {
                        todayText = e.Date + (Lang.IsEn ? " -> " : " 调整为 ") + TargetDisp(e);
                    }
                }
                else todayText = T("config.json 解析失败！");
                _lblTodayEntry.Text = todayText;

                bool running = IsServiceRunning(state);
                string stamp = !string.IsNullOrEmpty(state.LastTickReal) ? state.LastTickReal : state.LastTick;
                _lblService.Text = running
                    ? (Lang.IsEn ? "Running (heartbeat " + stamp + ")" : "正在运行（心跳 " + stamp + "）")
                    : T("已关闭");
                _lblService.ForeColor = running ? Color.DarkGreen : Color.DarkRed;
                SetImage(_picService, running
                    ? IconFactory.Play(18, IconFactory.Green)
                    : IconFactory.Stop(18, IconFactory.Gray));
                SetBtnImage(_btnService, running
                    ? IconFactory.Stop(16, IconFactory.Red)
                    : IconFactory.Play(16, IconFactory.Green));
                _btnService.Text = running ? " " + T("停止") : " " + T("启动");

                SyncAutostartCheckBox();
            }
            catch { }
        }

        private static bool IsServiceRunning(State state)
        {
            if (!state.ServiceEnabled) return false;
            bool realDomain = !string.IsNullOrEmpty(state.LastTickReal);
            DateTime last;
            if (!DateTime.TryParse(realDomain ? state.LastTickReal : state.LastTick, out last)) return false;
            DateTime now = realDomain ? DateTime.Now.AddSeconds(-state.AppliedOffsetSeconds) : DateTime.Now;
            double minutes = (now - last).TotalMinutes;
            return minutes >= -1 && minutes < 3;
        }

        private static void SetImage(PictureBox pb, Image img)
        {
            var old = pb.Image;
            pb.Image = img;
            if (old != null) old.Dispose();
        }

        private static void SetBtnImage(Button b, Image img)
        {
            var old = b.Image;
            b.Image = img;
            if (old != null) old.Dispose();
        }

        private void SyncAutostartCheckBox()
        {
            bool installed = Autostart.IsInstalled();
            _suppressEvents = true;
            _chkAutostart.Checked = installed;
            _chkAutostart.Text = T(installed ? "开机自启（本工具）——已启用" : "开机自启（本工具）——未启用");
            _chkAutostart.ForeColor = installed ? Color.DarkGreen : Color.DarkRed;
            _suppressEvents = false;
        }

        private void OnAutostartToggled(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            if (!RequireAdmin()) { SyncAutostartCheckBox(); return; }
            bool enable = _chkAutostart.Checked;
            string err = enable ? Autostart.Install() : Autostart.Uninstall();
            if (err != null)
            {
                CsvLogger.Error("GUI", (enable ? "Failed to enable auto-start: " : "Failed to disable auto-start: ") + err);
                MessageBox.Show(T(enable ? "启用" : "停用") + T("失败: ") + err, T("错误"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                CsvLogger.Info("GUI", (enable ? "Auto-start enabled: " : "Auto-start disabled: ") + Autostart.TaskName);
                if (enable) Autostart.RunNow(); // 启用自启的同时拉起后台服务
            }
            RefreshStatus();
        }

        private void OnPolicyChanged(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            _settings.ServicePolicy = _cmbPolicy.SelectedIndex == 1 ? "AlwaysOn" : (_cmbPolicy.SelectedIndex == 2 ? "AlwaysOff" : "Memory");
            SaveSettings();
            CsvLogger.Info("GUI", "Power-loss policy changed: " + Worker.PolicyNameEn(_settings.ServicePolicy));
        }

        private void OnSettingsChanged(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            _settings.CloseToTray = _chkCloseToTray.Checked;
            _settings.UseNtp = _chkUseNtp.Checked;
            _settings.NtpServer = _cmbNtp.Text.Trim();
            SaveSettings();
            UpdateCalibControlStates();
        }

        private void SaveSettings()
        {
            try { Paths.EnsureDirs(); JsonStore.Write(Paths.SettingsFile, _settings); } catch { }
        }

        // ---- 自检设置 ----
        private static readonly int[] IntervalValues = { 30, 60, 120, 300, 600, 1800 };

        private static int IntervalToIndex(int seconds)
        {
            for (int i = 0; i < IntervalValues.Length; i++)
                if (IntervalValues[i] == seconds) return i;
            return IntervalValues.Length; // 自定义
        }

        private void OnDailyOnceChanged(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            _settings.DailyOnce = _chkDailyOnce.Checked;
            SaveSettings();
            UpdateIntervalControlStates();
            CsvLogger.Info("GUI", _settings.DailyOnce
                ? "Daily-only check enabled (once after midnight, interval ignored)"
                : "Daily-only check disabled, interval=" + Worker.EffectiveInterval(_settings) + "s");
        }

        private void OnIntervalChanged(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            bool custom = _cmbInterval.SelectedIndex == IntervalValues.Length;
            UpdateIntervalControlStates();
            if (!custom)
            {
                _settings.CheckIntervalSeconds = IntervalValues[_cmbInterval.SelectedIndex];
                SaveSettings();
                CsvLogger.Info("GUI", "Check interval changed: " + _settings.CheckIntervalSeconds + "s");
            }
        }

        private void OnIntervalCustom(object s, EventArgs e)
        {
            if (_suppressEvents) return;
            int v;
            if (int.TryParse(_txtInterval.Text.Trim(), out v))
            {
                if (v < 5) v = 5;
                if (v > 86400) v = 86400;
                _settings.CheckIntervalSeconds = v;
                SaveSettings();
                CsvLogger.Info("GUI", "Check interval changed: " + v + "s (custom)");
            }
            _txtInterval.Text = _settings.CheckIntervalSeconds.ToString(); // 非法输入回显为当前生效值
        }

        private void UpdateIntervalControlStates()
        {
            bool custom = _cmbInterval.SelectedIndex == IntervalValues.Length;
            bool editable = _isAdmin && !_settings.DailyOnce;
            _cmbInterval.Enabled = editable;
            _txtInterval.Visible = custom;
            _lblSec.Visible = custom;
            _txtInterval.Enabled = editable && custom;
        }

        private void UpdateCalibControlStates()
        {
            bool ntp = _chkUseNtp.Checked;
            _cmbNtp.Enabled = _isAdmin && ntp;
            _dtpCalib.Enabled = _isAdmin && !ntp;
        }

        private void OnChangeDataDir(object s, EventArgs e)
        {
            if (!RequireAdmin()) return;
            using (var dlg = new FolderBrowserDialog
            {
                Description = T("更改数据目录"),
                SelectedPath = Paths.DataDir,
                ShowNewFolderButton = true
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string newDir = dlg.SelectedPath;
                if (string.Equals(newDir.TrimEnd('\\'), Paths.DataDir.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase)) return;
                bool migrate = Ui.Confirm(this, T("是否将现有数据（规则/状态/设置/日志）复制到新目录？"), T("更改数据目录"));
                try
                {
                    if (migrate && Directory.Exists(Paths.DataDir)) CopyDir(Paths.DataDir, newDir);
                    Paths.SetCustomDataDir(newDir);
                    Paths.EnsureDirs();
                    CsvLogger.Info("GUI", "Data folder changed: " + newDir);
                    AfterDataDirChanged();
                    MessageBox.Show(T("数据目录已更改。若后台服务正在运行，请停止后重新启动以使用新目录。"),
                        T("成功"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    CsvLogger.Error("GUI", "Failed to change data folder: " + ex.Message);
                    MessageBox.Show(T("更改失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnResetDataDir(object s, EventArgs e)
        {
            if (!RequireAdmin()) return;
            if (!Ui.Confirm(this,
                Lang.IsEn ? "Restore the default data folder?\n(Existing data stays in the current folder.)"
                          : "恢复默认数据目录？\n（现有数据保留在原目录，不会自动迁移）",
                T("更改数据目录"))) return;
            try
            {
                Paths.SetCustomDataDir(null);
                Paths.EnsureDirs();
                CsvLogger.Info("GUI", "Data folder reset to default: " + Paths.DataDir);
                AfterDataDirChanged();
                MessageBox.Show(T("数据目录已更改。若后台服务正在运行，请停止后重新启动以使用新目录。"),
                    T("成功"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CsvLogger.Error("GUI", "Failed to reset data folder: " + ex.Message);
                MessageBox.Show(T("更改失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AfterDataDirChanged()
        {
            _lblDataDirPath.Text = Paths.DataDir;
            LoadConfigToGrid();
            ReloadLogFiles();
            LoadLog();
            RefreshStatus();
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        private void OnAdminRestartToggled(object s, EventArgs e)
        {
            if (_suppressEvents || !_chkAdminRestart.Checked) return;
            if (!Ui.Confirm(this, T("将关闭当前窗口，并以管理员身份重新启动本工具。\n系统会弹出 UAC 确认框，请点击「是」。\n\n是否继续？"),
                T("以管理员身份重新启动")))
            {
                _suppressEvents = true;
                _chkAdminRestart.Checked = false;
                _suppressEvents = false;
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(Autostart.ExePath) { Verb = "runas", UseShellExecute = true });
                _forceExit = true;
                Close();
            }
            catch
            {
                _suppressEvents = true;
                _chkAdminRestart.Checked = false;
                _suppressEvents = false;
            }
        }

        private void OnServiceToggle(object s, EventArgs e)
        {
            if (!RequireAdmin()) return;
            var state = JsonStore.Read<State>(Paths.StateFile);

            if (!IsServiceRunning(state))
            {
                try
                {
                    state.ServiceEnabled = true;
                    JsonStore.Write(Paths.StateFile, state);
                    string err = Autostart.IsInstalled() ? Autostart.RunNow() : StartWorkerProcess();
                    if (err != null)
                    {
                        CsvLogger.Error("GUI", "Failed to start service: " + err);
                        MessageBox.Show(T("启动失败: ") + err, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        CsvLogger.Info("GUI", "Service started: rules activated");
                    }
                }
                catch (Exception ex)
                {
                    CsvLogger.Error("GUI", "Failed to start service: " + ex.Message);
                    MessageBox.Show(T("启动失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                try
                {
                    bool needRestore = state.AppliedOffsetSeconds != 0;
                    if (needRestore)
                    {
                        var clock = new RealSystemClock();
                        DateTime before = clock.GetLocalNow();
                        clock.AddSeconds(-state.AppliedOffsetSeconds);
                        DateTime after = clock.GetLocalNow();
                        CsvLogger.Info("GUI", "Real date restored: system time " +
                            before.ToString("yyyy-MM-dd HH:mm:ss") + " -> " + after.ToString("yyyy-MM-dd HH:mm:ss") +
                            " (auto-restore before stopping service)");
                        state.AppliedOffsetSeconds = 0;
                    }
                    state.ServiceEnabled = false;
                    JsonStore.Write(Paths.StateFile, state);
                    try { Process.GetProcessById(state.WorkerPid).Kill(); }
                    catch { Autostart.EndTask(); }
                    CsvLogger.Info("GUI", needRestore
                        ? "Service stopped: real date restored, rules deactivated"
                        : "Service stopped: rules deactivated");
                }
                catch (Exception ex)
                {
                    CsvLogger.Error("GUI", "Failed to stop service: " + ex.Message);
                    MessageBox.Show(T("停止失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            RefreshStatus();
        }

        private string StartWorkerProcess()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Autostart.ExePath, "--worker")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                });
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private void OnTickNow(object s, EventArgs e)
        {
            if (!RequireAdmin()) return;
            var state = JsonStore.Read<State>(Paths.StateFile);
            if (!IsServiceRunning(state))
            {
                MessageBox.Show(T("后台服务未在运行，手动自检不会应用任何调整。\n如需规则生效，请先点击「启动」后台服务。"),
                    T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                string result = Worker.TickOnce(false);
                CsvLogger.Info("GUI", "Manual check triggered");
                MessageBox.Show(T("自检结果: ") + T(result), T("完成"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CsvLogger.Error("GUI", "Manual check failed: " + ex.Message);
                MessageBox.Show(T("自检失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshStatus();
        }

        private void OnCalibrate(object s, EventArgs e)
        {
            if (!RequireAdmin()) return;

            DateTime pickedTrue;
            string calibSource;
            if (_chkUseNtp.Checked)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    pickedTrue = NtpClient.GetNetworkTime(_cmbNtp.Text.Trim(), 4000);
                    calibSource = "GUI(NTP:" + _cmbNtp.Text.Trim() + ")";
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    CsvLogger.Error("GUI", "NTP calibration failed (server " + _cmbNtp.Text.Trim() + "): " + ex.Message);
                    MessageBox.Show(T("网络校时失败（可能电脑未联网或服务器不可达）:\n") + ex.Message +
                        T("\n\n可取消勾选「使用网络校时」改用手动校时。"), T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Cursor = Cursors.Default;
                try { _dtpCalib.Value = pickedTrue; } catch { }
            }
            else
            {
                pickedTrue = _dtpCalib.Value;
                calibSource = "GUI(Manual)";
            }

            var state = JsonStore.Read<State>(Paths.StateFile);
            DateTime newSys = pickedTrue.AddSeconds(state.AppliedOffsetSeconds);
            string calibMsg = Lang.IsEn
                ? "The REAL time will be calibrated to: " + pickedTrue.ToString("yyyy-MM-dd HH:mm:ss") +
                  "\n" + (state.AppliedOffsetSeconds == 0
                      ? "No shift is currently applied."
                      : "A shift is active; the system clock will show: " + newSys.ToString("yyyy-MM-dd HH:mm:ss")) +
                  "\n\nProceed?"
                : "将把【真实时间】校准为: " + pickedTrue.ToString("yyyy-MM-dd HH:mm:ss") +
                  "\n当前" + (state.AppliedOffsetSeconds == 0 ? "无调整" : "处于调整状态，系统时间将显示为: " + newSys.ToString("yyyy-MM-dd HH:mm:ss")) +
                  "\n\n确认执行？";
            if (!Ui.Confirm(this, calibMsg, T("校时确认"))) return;
            try
            {
                var clock = new RealSystemClock();
                DateTime before = clock.GetLocalNow();
                clock.AddSeconds((newSys - before).TotalSeconds);
                CsvLogger.Info(calibSource, "Time calibrated: system time " +
                    before.ToString("yyyy-MM-dd HH:mm:ss") + " -> " + newSys.ToString("yyyy-MM-dd HH:mm:ss") +
                    " (real time " + pickedTrue.ToString("yyyy-MM-dd HH:mm:ss") + ")");
                MessageBox.Show(T("校时完成。"), T("成功"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CsvLogger.Error("GUI", "Calibration failed: " + ex.Message);
                MessageBox.Show(T("校时失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshStatus();
        }

        private bool RequireAdmin()
        {
            if (_isAdmin) return true;
            MessageBox.Show(T("此操作需要管理员权限。\n请勾选本页「以管理员身份重新启动」，或右键 exe →「以管理员身份运行」。"),
                T("权限不足"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // ==================== 页签3：日志查看（一行一条，横排显示全） ====================
        private TabPage BuildLogTab()
        {
            var page = new TabPage(T("日志查看"));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            panel.Controls.Add(MkLbl(T("日志文件:")));
            _cmbLogFile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = InputMargin };
            _cmbLogFile.SelectedIndexChanged += delegate { LoadLog(); };
            panel.Controls.Add(_cmbLogFile);
            panel.Controls.Add(MkLbl(T("筛选:")));
            _txtFilter = new TextBox { Width = 150, Margin = InputMargin };
            _txtFilter.TextChanged += delegate { LoadLog(); };
            panel.Controls.Add(_txtFilter);
            panel.Controls.Add(MkIconBtn(T("导出日志"), IconFactory.ArrowOut(16, IconFactory.Dark), OnExportLog));
            panel.Controls.Add(MkIconBtn(T("打开日志文件夹"), IconFactory.Folder(16, IconFactory.Dark), delegate { OpenDir(Paths.LogDir); }));
            layout.Controls.Add(panel, 0, 0);

            _logView = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 9.5F)
            };
            layout.Controls.Add(_logView, 0, 1);

            page.Controls.Add(layout);
            ReloadLogFiles();
            LoadLog();
            return page;
        }

        private void ReloadLogFiles()
        {
            _cmbLogFile.Items.Clear();
            if (!Directory.Exists(Paths.LogDir)) return;
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(Paths.LogDir, "*.log"));
            files.AddRange(Directory.GetFiles(Paths.LogDir, "*.csv")); // 兼容旧版 CSV 日志
            files.Sort();
            files.Reverse();
            foreach (var f in files) _cmbLogFile.Items.Add(Path.GetFileName(f));
            if (_cmbLogFile.Items.Count > 0) _cmbLogFile.SelectedIndex = 0;
        }

        private void LoadLog()
        {
            _logView.Clear();
            if (_cmbLogFile.SelectedItem == null) return;
            string file = Path.Combine(Paths.LogDir, _cmbLogFile.SelectedItem.ToString());
            if (!File.Exists(file)) return;
            try
            {
                string filter = (_txtFilter.Text ?? "").Trim();
                var sb = new System.Text.StringBuilder();
                foreach (var raw in File.ReadAllLines(file))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (filter.Length > 0 && raw.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sb.Append(raw).Append("\r\n");
                }
                _logView.Text = sb.ToString();
                _logView.SelectionStart = _logView.TextLength; // 滚动到最新一条
                _logView.ScrollToCaret();
            }
            catch (Exception ex)
            {
                _logView.Text = (Lang.IsEn ? "Failed to read log: " : "日志读取失败: ") + ex.Message;
            }
        }

        private void OnExportLog(object s, EventArgs e)
        {
            if (_cmbLogFile.SelectedItem == null) return;
            string src = Path.Combine(Paths.LogDir, _cmbLogFile.SelectedItem.ToString());
            using (var dlg = new SaveFileDialog { Filter = T("日志文件|*.log;*.csv"), FileName = _cmbLogFile.SelectedItem.ToString() })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try { File.Copy(src, dlg.FileName, true); MessageBox.Show(T("已导出。"), T("成功"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex)
                {
                    CsvLogger.Error("GUI", "Failed to export log: " + ex.Message);
                    MessageBox.Show(T("导出失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ==================== 页签4：关于 ====================
        private TabPage BuildAboutTab()
        {
            var page = new TabPage(T("关于")) { Padding = new Padding(10) };
            var txt = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Text = AboutText()
            };
            page.Controls.Add(txt);
            return page;
        }

        private static string AboutText()
        {
            if (Lang.IsEn)
                return
"AutoDateShift " + Version + "\r\n" +
"Copyright (c) 2026 rekimaple. Released under the MIT License.\r\n" +
"\r\n" +
"[ Overview ]\r\n" +
"Automatically adjusts the system date according to preset rules. Two rule modes:\r\n" +
"- By weekday: adjusts a given date to the nearest chosen weekday (within +/-3 days). Suitable for software that schedules tasks by weekday only.\r\n" +
"- By date: adjusts a given date to any target date, with no offset limit.\r\n" +
"On dates without rules, the real date is restored automatically; the time of day is never changed.\r\n" +
"Example: Oct 10 (Saturday) should run as Wednesday -> date = Oct 10, mode = By weekday, target = Wednesday.\r\n" +
"\r\n" +
"[ Usage ]\r\n" +
"1. This program is the management console: rule maintenance, status monitoring, time calibration and log review. The actual date adjustment is performed by the background service.\r\n" +
"2. Rules take effect only while the background service is running (Status page -> Start). The service self-checks at a configurable interval (default 60 seconds), or only once a day after midnight if that option is enabled. When the service is stopped while a shift is active, the real date is restored first.\r\n" +
"3. With auto-start enabled, the tool and the service launch at system startup and require no user logon. The \"After power loss\" policy determines the service state after an unexpected power-cycle.\r\n" +
"4. Closing the window minimizes it to the system tray and does not affect the background service.\r\n" +
"5. Only one instance runs at a time; launching again activates the existing window.\r\n" +
"6. Deployment: run as administrator -> start the background service (enabling auto-start is recommended) -> add rules and save.\r\n" +
"\r\n" +
"[ Uninstall and cleanup ]\r\n" +
"To remove this software completely and leave a clean system:\r\n" +
"1. On the Status page, stop the background service and disable auto-start.\r\n" +
"2. Exit this program (tray menu -> Exit Manager).\r\n" +
"3. Delete the program file and the data folder (default C:\\ProgramData\\AutoDateShift, which contains all rules, state and logs).\r\n" +
"4. If the system date is still shifted, correct it manually in Control Panel -> Date and Time (check the actual time on your phone, for example).\r\n" +
"After these steps the system date will no longer be adjusted automatically.\r\n" +
"If the program file has already been lost, run the bundled Emergency-Uninstall.bat as administrator, or execute the following in an elevated command prompt, then delete the data folder:\r\n" +
"     schtasks /end /tn AutoDateShiftWorker\r\n" +
"     schtasks /delete /f /tn AutoDateShiftWorker\r\n" +
"     taskkill /f /im AutoDateShift.exe\r\n" +
"\r\n" +
"[ Security ]\r\n" +
"- No network access and no data upload, except a single query to the selected NTP server when network calibration is used.\r\n" +
"- Rules, state and logs are stored locally as plain JSON / text files and can be audited directly.\r\n" +
"- Changing the system time requires elevated privileges; the background service runs under the SYSTEM account. Add an antivirus whitelist entry if alerts occur.\r\n" +
"- All icons are vector-drawn in code; no external resource files.\r\n" +
"\r\n" +
"[ Disclaimer ]\r\n" +
"Parts of this software were generated with AI assistance. Changing the system time may affect other software that depends on the system date (licenses, certificates, scheduled tasks, file timestamps, etc.). Evaluate before use; all consequences are borne by the user.";

            return
"AutoDateShift（系统日期自动切换）" + Version + "\r\n" +
"版权所有 © 2026 rekimaple，基于 MIT 协议发布。\r\n" +
"\r\n" +
"■ 功能简介\r\n" +
"按预设规则自动调整系统日期，支持两种规则模式：\r\n" +
"· 按星期：将指定日期调整为最近的某个星期几（±3 天内取最近），适用于仅按星期几调度任务的软件。\r\n" +
"· 按日期：将指定日期调整为任意目标日期，偏移天数不限。\r\n" +
"无规则的日期自动还原为真实日期，时分秒保持不变。\r\n" +
"示例：10 月 10 日（周六）需按周三运行 → 日期选 10 月 10 日，模式选“按星期”，目标选周三。\r\n" +
"\r\n" +
"■ 使用方法\r\n" +
"1. 本程序为管理界面，负责规则维护、状态查看、时间校准与日志查询；实际调整由后台服务执行。\r\n" +
"2. 在“运行状态”页启动后台服务后规则方可生效，服务默认每 60 秒自检一次（间隔可调整，也可设为仅在每日零点后自检一次）。停止服务时若日期处于调整状态，将先自动还原真实日期。\r\n" +
"3. 启用“开机自启”后，系统启动时自动运行本工具与后台服务，无需用户登录；“断电重启后”策略用于指定意外断电重启后的服务状态。\r\n" +
"4. 关闭窗口默认最小化至系统托盘，不影响后台服务运行。\r\n" +
"5. 本软件为单实例运行，重复启动将激活已有窗口。\r\n" +
"6. 部署步骤：以管理员身份运行 → 启动后台服务（建议同时启用开机自启） → 录入规则并保存。\r\n" +
"\r\n" +
"■ 卸载与清理\r\n" +
"若不再使用本软件，按以下步骤可彻底清理、不留残留：\r\n" +
"1. 在“运行状态”页停止后台服务，并取消勾选“开机自启”。\r\n" +
"2. 退出本程序（托盘菜单 → 退出管理界面）。\r\n" +
"3. 删除程序文件与数据目录（默认 C:\\ProgramData\\AutoDateShift，含全部规则、状态与日志）。\r\n" +
"4. 若系统日期仍处于调整状态，按实际时间（如手机时间）在控制面板“日期和时间”中手动修正。\r\n" +
"完成以上步骤后，系统不会再被自动调整日期。\r\n" +
"若程序文件已经丢失：以管理员身份运行随软件附带的“应急卸载.bat”，或在管理员命令提示符中依次执行以下命令，然后删除数据目录：\r\n" +
"     schtasks /end /tn AutoDateShiftWorker\r\n" +
"     schtasks /delete /f /tn AutoDateShiftWorker\r\n" +
"     taskkill /f /im AutoDateShift.exe\r\n" +
"\r\n" +
"■ 安全性\r\n" +
"· 软件不主动联网、不上传数据；仅在使用网络校时（NTP）时访问一次所选时间服务器。\r\n" +
"· 规则、状态与日志均以明文 JSON / 文本格式保存于本机，可直接查阅审计。\r\n" +
"· 修改系统时间需最高权限，后台服务以 SYSTEM 账户运行；如安全软件告警，请加入白名单。\r\n" +
"· 界面图标由代码矢量绘制，不含外部资源文件。\r\n" +
"\r\n" +
"■ 免责声明\r\n" +
"本软件部分代码由 AI 辅助生成。修改系统时间可能影响依赖系统日期的其他软件（授权许可、证书、计划任务、文件时间戳等）。请在充分评估后使用，由此产生的后果由使用者自行承担。";
        }

        private void OpenDir(string path)
        {
            try
            {
                Paths.EnsureDirs();
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("打开失败: ") + ex.Message, T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ==================== 控件工厂 ====================
        private static void SetupGrid(DataGridView g)
        {
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            g.DefaultCellStyle.SelectionBackColor = Color.LightSteelBlue;
            g.DefaultCellStyle.SelectionForeColor = Color.Black;
            g.BackgroundColor = Color.White;
        }

        private Button MkIconBtn(string text, Bitmap icon, EventHandler onClick)
        {
            var b = new Button
            {
                Text = " " + text,
                Image = icon,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                AutoSize = true,
                Margin = BtnMargin,
                Padding = new Padding(6, 2, 6, 2)
            };
            b.Click += onClick;
            return b;
        }
    }
}

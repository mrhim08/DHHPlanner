#if !LIB
// DHHPanel - built-in DHHPlanner plugin, compiled into MissionPlanner.exe (found by PluginLoader.InitPlugin "self"). Desktop only (guarded by !LIB).
// Tab "DHH" trong Flight Data: trang thai VTOL, nut mode, checklist truoc bay, ARM/DISARM, kiem tra/ap preset tham so.
// Doc ho so may bay tu <UserDataDir>\dhh-aircraft\*\profile.json (ghi de cua nguoi dung) hoac <RunningDir>\dhh-aircraft\ (di kem ung dung).
//
// Quy tac EKF (check "ekf"): dat khi da nhan EKF_STATUS_REPORT (cs.ekfflags != 0) va cs.ekfstatus < 0.5.
// cs.ekfstatus = max(variance) va HUD cua MP to vang khi > 0.5, do khi > 0.8; ta dung nguong vang lam ranh gioi "dat".

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using Newtonsoft.Json.Linq;

namespace DHH.Planner
{
    public class DHHPanelPlugin : MissionPlanner.Plugin.Plugin
    {
        private TabPage tab;
        private DHHControl ui;

        public override string Name { get { return "DHHPanel"; } }
        public override string Version { get { return "0.1.0"; } }
        public override string Author { get { return "DHH RC Lab"; } }
        public override float loopratehz { get { return 2; } set { } }

        public override bool Init()
        {
            return true;
        }

        public override bool Loaded()
        {
            try
            {
                ui = new DHHControl();
                ui.Dock = DockStyle.Fill;

                tab = new TabPage();
                tab.Name = "tabDHH";
                tab.Text = "DHH";
                tab.Controls.Add(ui);

                EnsureTabSetting();

                Host.MainForm.FlightData.TabListOriginal.Add(tab);
                Host.MainForm.FlightData.tabControlactions.TabPages.Insert(0, tab);

                ThemeManager.ApplyThemeTo(tab);
                ui.AfterThemed();
                ui.LoadProfilesSafe();
            }
            catch (Exception ex)
            {
                DHHControl.Log("Loaded", ex);
            }
            return true;
        }

        // Giong OpenDroneID2.forceSettings: neu khong co ten tab trong setting thi tab bi an o lan khoi dong sau.
        private void EnsureTabSetting()
        {
            // chi tu them tab dung 1 lan; nguoi dung an tab thi giu nguyen
            if (Settings.Instance["dhh_tab_added"] == "1")
                return;
            string tabs = Settings.Instance["tabcontrolactions"];
            if (string.IsNullOrEmpty(tabs))
            {
                // chua co cau hinh: luu cau hinh hien tai (cac tab goc) roi them tabDHH
                Host.MainForm.FlightData.saveTabControlActions();
                tabs = Settings.Instance["tabcontrolactions"] ?? "";
            }
            if (!tabs.Split(';').Contains("tabDHH"))
            {
                Settings.Instance["tabcontrolactions"] = "tabDHH;" + tabs;
            }
            Settings.Instance["dhh_tab_added"] = "1";
        }

        public override bool Loop()
        {
            try
            {
                if (ui == null || ui.IsDisposed || !ui.IsHandleCreated)
                    return true;
                ui.LoopTick();
            }
            catch (Exception ex)
            {
                DHHControl.Log("Loop", ex);
            }
            return true;
        }

        public override bool Exit()
        {
            try { if (ui != null) ui.Shutdown(); } catch { }
            return true;
        }
    }

    // ------------------------------------------------------------------ du lieu

    internal class ModeDef
    {
        public string Mode, Label, Group;
        public bool Confirm;
    }

    internal class CheckItem
    {
        public string Id, Type, Check, Text;
        public bool Implicit;
        public bool IsAuto { get { return string.Equals(Type, "auto", StringComparison.OrdinalIgnoreCase); } }
    }

    internal class Profile
    {
        public string Id, Name, Description, Status, Dir, Firmware;
        public Dictionary<string, double> RequireParams = new Dictionary<string, double>();
        public List<ModeDef> Modes = new List<ModeDef>();
        public List<CheckItem> Items = new List<CheckItem>();
        public Dictionary<string, double> Preset = new Dictionary<string, double>();
        public string[] AirspeedMinParam = new string[0];
        public double AirspeedWarnMargin = 2, CellCount = 1, CellWarnV = 3.6, MinSats = 10, MaxHdop = 1.2, MaxVibe = 30;
        public override string ToString() { return string.IsNullOrEmpty(Name) ? Id : Name; }

        public static Profile Load(string profileJsonPath)
        {
            var dir = Path.GetDirectoryName(profileJsonPath);
            var o = JObject.Parse(File.ReadAllText(profileJsonPath, Encoding.UTF8));
            var p = new Profile();
            p.Dir = dir;
            p.Id = (string)o["id"] ?? Path.GetFileName(dir);
            p.Name = (string)o["name"] ?? p.Id;
            p.Description = (string)o["description"] ?? "";
            p.Status = (string)o["status"] ?? "";
            var m = o["match"] as JObject;
            if (m != null)
            {
                p.Firmware = (string)m["firmware"];
                var rp = m["requireParams"] as JObject;
                if (rp != null)
                    foreach (var kv in rp)
                        p.RequireParams[kv.Key] = (double)kv.Value;
            }
            var modes = o["operatorModes"] as JArray;
            if (modes != null)
                foreach (JObject mo in modes.OfType<JObject>())
                    p.Modes.Add(new ModeDef
                    {
                        Mode = (string)mo["mode"],
                        Label = (string)mo["label"] ?? (string)mo["mode"],
                        Group = (string)mo["group"] ?? "fw",
                        Confirm = mo["confirm"] != null && (bool)mo["confirm"]
                    });
            var lim = o["limits"] as JObject;
            if (lim != null)
            {
                var names = lim["airspeedMinParam"] as JArray;
                if (names != null) p.AirspeedMinParam = names.Select(t => (string)t).ToArray();
                p.AirspeedWarnMargin = D(lim, "airspeedWarnMargin", p.AirspeedWarnMargin);
                p.CellCount = D(lim, "batteryCellCount", p.CellCount);
                p.CellWarnV = D(lim, "batteryCellWarnV", p.CellWarnV);
                p.MinSats = D(lim, "minSats", p.MinSats);
                p.MaxHdop = D(lim, "maxHdop", p.MaxHdop);
                p.MaxVibe = D(lim, "maxVibe", p.MaxVibe);
            }

            var presetRel = (string)o["paramPreset"];
            if (!string.IsNullOrEmpty(presetRel))
                p.Preset = ParamFile.loadParamFile(Path.Combine(dir, presetRel));

            var clRel = (string)o["checklist"];
            if (!string.IsNullOrEmpty(clRel))
            {
                var c = JObject.Parse(File.ReadAllText(Path.Combine(dir, clRel), Encoding.UTF8));
                var items = c["items"] as JArray;
                if (items != null)
                    foreach (JObject it in items.OfType<JObject>())
                        p.Items.Add(new CheckItem
                        {
                            Id = (string)it["id"],
                            Type = (string)it["type"] ?? "manual",
                            Check = (string)it["check"],
                            Text = (string)it["text"] ?? (string)it["id"]
                        });
            }
            // cac check an toan bat buoc: them vao neu checklist bo sot (hien thi nhu muc ngam dinh)
            var required = new[]
            {
                new[] { "prearm", "Không có lỗi PreArm (mặc định)" },
                new[] { "gps", "GPS 3D fix, đủ vệ tinh, HDOP đạt (mặc định)" },
                new[] { "ekf", "EKF ổn định (mặc định)" }
            };
            foreach (var rq in required)
            {
                if (!p.Items.Any(i => i.IsAuto && i.Check == rq[0]))
                    p.Items.Add(new CheckItem { Id = "implicit-" + rq[0], Type = "auto", Check = rq[0], Text = rq[1], Implicit = true });
            }
            return p;
        }

        private static double D(JObject o, string key, double def)
        {
            var t = o[key];
            return t == null ? def : (double)t;
        }
    }

    internal class ParamRow
    {
        public string Name;          // ten hien thi (ten trong preset)
        public string VehicleName;   // ten thuc te tren may bay (co the la alias), null neu thieu
        public double PresetValue;
        public double? VehicleValue;
        public bool Ok;
        public string Status;
    }

    // Anh chup bat bien (theo quy uoc: chi gan truoc khi cong bo) cua trang thai, tao o thread nen.
    internal class Snapshot
    {
        public Profile Profile;
        public bool Connected, Fresh, Armed, PrearmOk, PrearmSeen, VibeSeen, HasAirspeed, HardOk;
        public bool Live { get { return Connected && Fresh; } }
        public List<ParamRow> ReqRows = new List<ParamRow>();
        public byte SysId, CompId;
        public string Firmware = "", Mode = "";
        public byte VtolState, LandedState;
        public double Airspeed, MinAirspeed = double.NaN;   // m/s
        public double GpsFix, Sats, Hdop, BattV, EkfStatus, VibeMax;
        public bool EkfSeen;
        public string AssistMsg;
        public int ParamCount;
        public int AutoProfileIndex = -1;
        public HashSet<string> Modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<ParamRow> Rows = new List<ParamRow>();
        public bool[] AutoOk = new bool[0];
        public string[] AutoDetail = new string[0];
        public bool ProfileMatch;
        public bool AirspeedWarn;
    }

    // ------------------------------------------------------------------ UI

    internal class DHHControl : UserControl
    {
        private static readonly Color ColVtol = Color.FromArgb(0, 105, 180);
        private static readonly Color ColFw = Color.FromArgb(46, 125, 50);
        private static readonly Color ColActive = Color.FromArgb(255, 193, 7);
        private static readonly Color ColOk = Color.FromArgb(40, 180, 70);
        private static readonly Color ColBad = Color.FromArgb(230, 60, 50);
        private static readonly Color ColDisabled = Color.FromArgb(90, 90, 90);

        private static readonly Dictionary<string, string> Alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AIRSPEED_MIN", "ARSPD_FBW_MIN" }, { "ARSPD_FBW_MIN", "AIRSPEED_MIN" },
            { "AIRSPEED_MAX", "ARSPD_FBW_MAX" }, { "ARSPD_FBW_MAX", "AIRSPEED_MAX" }
        };

        private readonly List<Profile> profiles = new List<Profile>();
        private volatile Profile current;
        private Snapshot last;
        private bool userOverride, lastConnected, lastArmed, chosen;
        private volatile bool busy;
        private int lastApplyTick;
        private System.Windows.Forms.Timer staleTimer;
        // dang ky VIBRATION / theo doi ket noi (bao ve boi subLock)
        private readonly object subLock = new object();
        private MAVLinkInterface vibeCom;
        private int vibeSub;
        private bool wasLive;
        private volatile bool vibeSeen;
        private volatile int vibeTick;
        private string signature = "";
        private string loadErrors = "";

        private ComboBox cmbProfile;
        private Label lblConn, lblProfileInfo, lblMsg;
        private Label lblVtol, lblLanded, lblMode, lblAirspeed, lblWarn, lblAssist;
        private FlowLayoutPanel modePanel, checkPanel;
        private readonly List<Button> modeButtons = new List<Button>();
        private readonly Dictionary<CheckItem, Control> itemControls = new Dictionary<CheckItem, Control>();
        private Button btnArm, btnDisarm, btnReset, btnRecheck, btnApply;
        private ListView lvParams;
        private FlowLayoutPanel root;

        public DHHControl()
        {
            try
            {
                BuildUi();
                staleTimer = new System.Windows.Forms.Timer();
                staleTimer.Interval = 1000;
                staleTimer.Tick += H("staleTimer", () => { if (!IsDisposed && last != null) ApplyGating(last); });
                staleTimer.Start();
            }
            catch (Exception ex) { Log("BuildUi", ex); }
        }

        public static void Log(string where, Exception ex)
        {
            try { Console.WriteLine("[DHHPanel] " + where + ": " + ex); } catch { }
        }

        private EventHandler H(string name, Action a)
        {
            return (s, e) => { try { a(); } catch (Exception ex) { Log(name, ex); SetMsg("Lỗi: " + ex.Message, true); } };
        }

        private void PostUi(Action a)
        {
            try
            {
                if (IsDisposed) return;
                this.BeginInvokeIfRequired(a);
            }
            catch (Exception ex) { Log("PostUi", ex); }
        }

        private void SetMsg(string text, bool bad)
        {
            PostUi(() =>
            {
                if (IsDisposed || lblMsg == null) return;
                lblMsg.Text = text;
                lblMsg.ForeColor = bad ? ColBad : ColOk;
            });
        }

        // ---------- dung giao dien

        private GroupBox Group(string title)
        {
            var g = new GroupBox();
            g.Text = title;
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.MinimumSize = new Size(560, 0);
            g.Margin = new Padding(4, 4, 4, 8);
            // noi dung khong dock (dock + AutoSize lam GroupBox co ve 0): dat vao vung hien thi cua khung
            g.ControlAdded += (s, e) => { e.Control.Location = new Point(g.DisplayRectangle.X, g.DisplayRectangle.Y); };
            return g;
        }

        private FlowLayoutPanel Flow(FlowDirection dir)
        {
            var f = new FlowLayoutPanel();
            f.FlowDirection = dir;
            f.WrapContents = dir == FlowDirection.LeftToRight;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Dock = DockStyle.None;
            if (f.WrapContents) f.MaximumSize = new Size(550, 0);   // can be rong toi da de nut xuong dong
            return f;
        }

        private Label Lbl(string text, float size, bool bold)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(550, 0);
            l.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            l.Margin = new Padding(4, 2, 4, 2);
            return l;
        }

        private Button Btn(string text, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.Size = new Size(w, h);
            b.Margin = new Padding(4);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            return b;
        }

        private void BuildUi()
        {
            SuspendLayout();
            AutoScroll = true;

            root = Flow(FlowDirection.TopDown);
            root.Dock = DockStyle.None;
            root.Location = new Point(0, 0);
            Controls.Add(root);

            // Header
            var gh = Group("Hồ sơ máy bay");
            var fh = Flow(FlowDirection.TopDown);
            cmbProfile = new ComboBox();
            cmbProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProfile.Width = 400;
            cmbProfile.Margin = new Padding(4);
            cmbProfile.SelectionChangeCommitted += H("profileCommit", () =>
            {
                userOverride = true;
                chosen = true;
                ApplyProfile(cmbProfile.SelectedItem as Profile);
            });
            lblConn = Lbl("Chưa kết nối", 10, true);
            lblProfileInfo = Lbl("", 9, false);
            lblMsg = Lbl("", 10, true);
            fh.Controls.Add(cmbProfile);
            fh.Controls.Add(lblConn);
            fh.Controls.Add(lblProfileInfo);
            fh.Controls.Add(lblMsg);
            gh.Controls.Add(fh);
            root.Controls.Add(gh);

            // VTOL
            var gv = Group("Trạng thái VTOL");
            var fv = Flow(FlowDirection.TopDown);
            lblVtol = Lbl("KHÔNG RÕ", 28, true);
            lblLanded = Lbl("Landed: -", 11, false);
            lblMode = Lbl("Mode: -", 11, false);
            lblAirspeed = Lbl("Airspeed: -", 11, false);
            lblWarn = Lbl("", 12, true);
            lblWarn.ForeColor = ColBad;
            lblWarn.Visible = false;
            lblAssist = Lbl("", 11, true);
            lblAssist.ForeColor = Color.DarkOrange;
            lblAssist.Visible = false;
            foreach (var c in new Control[] { lblVtol, lblLanded, lblMode, lblAirspeed, lblWarn, lblAssist }) fv.Controls.Add(c);
            gv.Controls.Add(fv);
            root.Controls.Add(gv);

            // Mode
            var gm = Group("Chuyển mode");
            modePanel = Flow(FlowDirection.LeftToRight);
            gm.Controls.Add(modePanel);
            root.Controls.Add(gm);

            // Checklist
            var gc = Group("Checklist trước bay");
            var fc = Flow(FlowDirection.TopDown);
            checkPanel = Flow(FlowDirection.TopDown);
            var arow = Flow(FlowDirection.LeftToRight);
            btnArm = Btn("ARM", 130, 48);
            btnDisarm = Btn("DISARM", 130, 48);
            btnReset = Btn("Đặt lại", 110, 48);
            btnArm.Click += H("arm", OnArm);
            btnDisarm.Click += H("disarm", OnDisarm);
            btnReset.Click += H("reset", ResetManual);
            arow.Controls.Add(btnArm);
            arow.Controls.Add(btnDisarm);
            arow.Controls.Add(btnReset);
            fc.Controls.Add(checkPanel);
            fc.Controls.Add(arow);
            gc.Controls.Add(fc);
            root.Controls.Add(gc);

            // Param
            var gp = Group("Kiểm tra tham số");
            var fp = Flow(FlowDirection.TopDown);
            lvParams = new ListView();
            lvParams.View = View.Details;
            lvParams.FullRowSelect = true;
            lvParams.GridLines = true;
            lvParams.Size = new Size(550, 240);
            lvParams.Columns.Add("Tham số", 170);
            lvParams.Columns.Add("Preset", 90);
            lvParams.Columns.Add("Máy bay", 90);
            lvParams.Columns.Add("Trạng thái", 180);
            var prow = Flow(FlowDirection.LeftToRight);
            btnRecheck = Btn("Kiểm tra lại", 140, 40);
            btnApply = Btn("Áp preset", 140, 40);
            btnRecheck.Click += H("recheck", () => { signature = ""; SetMsg("Đã kiểm tra lại tham số.", false); });
            btnApply.Click += H("apply", OnApplyPreset);
            prow.Controls.Add(btnRecheck);
            prow.Controls.Add(btnApply);
            fp.Controls.Add(lvParams);
            fp.Controls.Add(prow);
            gp.Controls.Add(fp);
            root.Controls.Add(gp);

            ResumeLayout(true);
            Resize += (s, e) => { };
        }

        public void AfterThemed()
        {
            // theme co the doi mau nut; cap nhat lai o vong UI tiep theo
            signature = "";
        }

        // ---------- ho so

        public void LoadProfilesSafe()
        {
            try
            {
                profiles.Clear();
                var err = new StringBuilder();
                var root0 = Path.Combine(Settings.GetUserDataDirectory(), "dhh-aircraft");
                if (!Directory.Exists(root0))
                    root0 = Path.Combine(Settings.GetRunningDirectory(), "dhh-aircraft");
                if (!Directory.Exists(root0))
                {
                    err.AppendLine("Không tìm thấy thư mục hồ sơ: " + root0);
                }
                else
                {
                    foreach (var d in Directory.GetDirectories(root0))
                    {
                        var pj = Path.Combine(d, "profile.json");
                        if (!File.Exists(pj)) continue;
                        try { profiles.Add(Profile.Load(pj)); }
                        catch (Exception ex) { err.AppendLine(Path.GetFileName(d) + ": " + ex.Message); Log("Profile " + d, ex); }
                    }
                    if (profiles.Count == 0 && err.Length == 0)
                        err.AppendLine("Không có hồ sơ nào trong " + root0);
                }
                loadErrors = err.ToString().Trim();

                cmbProfile.Items.Clear();
                foreach (var p in profiles) cmbProfile.Items.Add(p);
                if (profiles.Count > 0)
                {
                    cmbProfile.SelectedIndex = 0;
                    ApplyProfile(profiles[0]);
                }
                if (loadErrors.Length > 0) SetMsg(loadErrors, true);
            }
            catch (Exception ex)
            {
                Log("LoadProfiles", ex);
                SetMsg("Lỗi tải hồ sơ: " + ex.Message, true);
            }
        }

        private void ApplyProfile(Profile p)
        {
            current = p;
            btnArm.Enabled = false;
            signature = "";
            modeButtons.Clear();
            modePanel.Controls.Clear();
            checkPanel.Controls.Clear();
            itemControls.Clear();
            if (p == null) return;

            lblProfileInfo.Text = p.Description + (string.IsNullOrEmpty(p.Status) ? "" : "\r\n" + p.Status);
            lblProfileInfo.ForeColor = (p.Status ?? "").IndexOf("DRAFT", StringComparison.OrdinalIgnoreCase) >= 0 ? ColBad : lblConn.ForeColor;

            foreach (var m in p.Modes)
            {
                var b = Btn(m.Label, 130, 56);
                b.Tag = m;
                var md = m;
                b.Click += H("mode " + m.Mode, () => OnMode(md));
                modeButtons.Add(b);
                modePanel.Controls.Add(b);
            }

            foreach (var it in p.Items)
            {
                Control c;
                if (it.IsAuto)
                {
                    c = Lbl(it.Text, 10, false);
                }
                else
                {
                    var cb = new CheckBox();
                    cb.Text = it.Text;
                    cb.AutoSize = true;
                    cb.MaximumSize = new Size(550, 0);
                    cb.Font = new Font("Segoe UI", 10);
                    cb.Margin = new Padding(4, 2, 4, 2);
                    cb.CheckedChanged += H("manual", () => { btnArm.Enabled = false; });
                    c = cb;
                }
                c.Tag = it;
                itemControls[it] = c;
                checkPanel.Controls.Add(c);
            }
            ThemeManager.ApplyThemeTo(modePanel);
            ThemeManager.ApplyThemeTo(checkPanel);
            ResetManual();
        }

        private void ResetManual()
        {
            btnArm.Enabled = false;
            foreach (var kv in itemControls)
            {
                var cb = kv.Value as CheckBox;
                if (cb != null) cb.Checked = false;
            }
        }

        // ---------- Loop (thread nen)

        public void LoopTick()
        {
            var prof = current;
            Snapshot s;
            try { s = BuildSnapshot(prof); }
            catch (Exception ex)
            {
                Log("BuildSnapshot", ex);
                s = EmptySnapshot(prof);
            }
            PostUi(() => UpdateUi(s));
        }

        // Snapshot "khong ket noi" dung khi doc du lieu bi loi: moi thu deu that bai.
        private Snapshot EmptySnapshot(Profile prof)
        {
            var s = new Snapshot();
            s.Profile = prof;
            if (prof != null)
            {
                s.AutoOk = new bool[prof.Items.Count];
                s.AutoDetail = new string[prof.Items.Count];
                for (int i = 0; i < s.AutoDetail.Length; i++) s.AutoDetail[i] = "lỗi đọc dữ liệu";
                foreach (var kv in prof.Preset)
                    s.Rows.Add(new ParamRow { Name = kv.Key, PresetValue = kv.Value, Status = "Chưa kết nối" });
            }
            return s;
        }

        private static string ResolveName(string name, Func<string, bool> has)
        {
            if (has(name)) return name;
            string alt;
            if (Alias.TryGetValue(name, out alt) && has(alt)) return alt;
            return null;
        }

        private static bool Same(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-4 * Math.Max(1, Math.Abs(a));
        }

        private bool MatchesProfile(Profile p, string fw, int paramCount, Func<string, bool> has, Func<string, double> get)
        {
            if (p == null) return false;
            if (!string.Equals(p.Firmware, fw, StringComparison.OrdinalIgnoreCase)) return false;
            if (p.RequireParams.Count > 0 && paramCount == 0) return false;
            foreach (var kv in p.RequireParams)
            {
                var n = ResolveName(kv.Key, has);
                if (n == null || !Same(kv.Value, get(n))) return false;
            }
            return true;
        }

        // Theo doi vong doi ket noi "song" (mo cong + du lieu moi): dang ky VIBRATION, yeu cau EXTENDED_SYS_STATE.
        private void HandleLink(MAVLinkInterface com, bool live)
        {
            lock (subLock)
            {
                if (down) return;
                if (live == wasLive) return;
                wasLive = live;
                UnsubVibe();
                if (!live || com == null) return;
                var mav = com.MAV;
                var sys = (byte)mav.sysid; var comp = (byte)mav.compid;
                try
                {
                    vibeCom = com;
                    vibeSub = com.SubscribeToPacketType(MAVLink.MAVLINK_MSG_ID.VIBRATION, m =>
                    {
                        vibeTick = Environment.TickCount;
                        vibeSeen = true;
                        return true;
                    }, sys, comp);
                }
                catch (Exception ex) { Log("subscribe VIBRATION", ex); }

                // EXTENDED_SYS_STATE (245) 1 Hz de co vtol_state/landed_state; khong cho ack.
                Task.Run(() =>
                {
                    try
                    {
                        com.doCommand(sys, comp, MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL, 245f, 1000000f, 0f, 0f, 0f, 0f, 0f, false, null);
                    }
                    catch (Exception ex) { Log("SET_MESSAGE_INTERVAL", ex); }
                });
            }
        }

        private volatile bool down;   // da Shutdown: khong dang ky lai

        private void UnsubVibe()
        {
            try
            {
                if (vibeSub != 0 && vibeCom != null) vibeCom.UnSubscribeToPacketType(vibeSub);
            }
            catch (Exception ex) { Log("unsubscribe VIBRATION", ex); }
            vibeSub = 0;
            vibeCom = null;
            vibeSeen = false;
        }

        public void Shutdown()
        {
            try
            {
                lock (subLock) { down = true; wasLive = false; UnsubVibe(); }
                if (staleTimer != null) staleTimer.Stop();
            }
            catch (Exception ex) { Log("Shutdown", ex); }
        }

        private Snapshot BuildSnapshot(Profile prof)
        {
            var s = new Snapshot();
            s.Profile = prof;
            var com = MainV2.comPort;
            s.Connected = com != null && com.BaseStream != null && com.BaseStream.IsOpen;

            Func<string, bool> has = n => false;
            Func<string, double> get = n => double.NaN;

            if (s.Connected)
            {
                var mav = com.MAV;
                var cs = mav.cs;
                s.Fresh = (DateTime.UtcNow - mav.lastvalidpacket).TotalSeconds < 2;
                s.SysId = (byte)mav.sysid;
                s.CompId = (byte)mav.compid;
                s.Firmware = cs.firmware.ToString();
                s.Mode = cs.mode ?? "";
                s.Armed = cs.armed;
                s.VtolState = cs.vtol_state;
                s.LandedState = cs.landed_state;
                double mult = CurrentState.multiplierspeed == 0 ? 1 : CurrentState.multiplierspeed;
                s.Airspeed = cs.airspeed / mult;
                s.GpsFix = cs.gpsstatus;
                s.Sats = cs.satcount;
                s.Hdop = cs.gpshdop;
                s.BattV = cs.battery_voltage;
                s.EkfStatus = cs.ekfstatus;
                s.EkfSeen = cs.ekfflags != 0;
                s.VibeMax = Math.Max(Math.Abs(cs.vibex), Math.Max(Math.Abs(cs.vibey), Math.Abs(cs.vibez)));
                // prearm: phai da nhan SYS_STATUS, bit PREARM_CHECK duoc bat va dang khoe (chua nhan = that bai)
                s.PrearmSeen = cs.sensors_enabled.seen && cs.sensors_health.seen;
                s.PrearmOk = s.PrearmSeen && cs.sensors_enabled.prearm && cs.sensors_health.prearm;

                HandleLink(com, s.Live);
                s.VibeSeen = vibeSeen && (Environment.TickCount - vibeTick) < 5000;

                try
                {
                    foreach (var kv in MissionPlanner.ArduPilot.Common.getModesList(cs.firmware)) s.Modes.Add(kv.Value);
                }
                catch (Exception ex) { Log("getModesList", ex); }

                try
                {
                    var msgs = cs.messages.ToArray();
                    for (int i = msgs.Length - 1; i >= 0; i--)
                    {
                        var age = DateTime.Now - msgs[i].time;
                        if (age.TotalSeconds > 10) break;
                        if (msgs[i].message != null && msgs[i].message.IndexOf("assist", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            s.AssistMsg = msgs[i].message;
                            break;
                        }
                    }
                }
                catch (Exception ex) { Log("messages", ex); }

                var pl = mav.param;
                s.ParamCount = pl.Count;
                has = n => pl.ContainsKey(n);
                get = n =>
                {
                    var q = pl[n];
                    return q == null ? double.NaN : q.Value;
                };

                // cam bien airspeed: ARSPD_TYPE / ARSPD1_TYPE / AIRSPEED_TYPE (cai dau tien ton tai) != 0
                foreach (var n in new[] { "ARSPD_TYPE", "ARSPD1_TYPE", "AIRSPEED_TYPE" })
                {
                    if (has(n)) { s.HasAirspeed = get(n) != 0; break; }
                }
            }
            else
            {
                HandleLink(com, false);
            }

            // tu chon ho so
            for (int i = 0; i < profiles.Count; i++)
            {
                if (s.Live && s.ParamCount > 0 && MatchesProfile(profiles[i], s.Firmware, s.ParamCount, has, get))
                {
                    s.AutoProfileIndex = i;
                    break;
                }
            }

            if (prof == null) return s;

            if (s.Connected)
            {
                foreach (var n in prof.AirspeedMinParam)
                {
                    if (has(n)) { s.MinAirspeed = get(n); break; }
                }
                // vtol_state==0 (chua nhan EXTENDED_SYS_STATE): coi mode khong bat dau bang "Q" la FW
                bool fwLike = s.VtolState == 4 ||
                              (s.VtolState == 0 && s.Mode.Length > 0 && !s.Mode.StartsWith("Q", StringComparison.OrdinalIgnoreCase));
                s.AirspeedWarn = s.Live && s.Armed && fwLike && !double.IsNaN(s.MinAirspeed)
                                 && s.Airspeed < s.MinAirspeed + prof.AirspeedWarnMargin;
            }

            // param rows
            foreach (var kv in prof.Preset)
                s.Rows.Add(MakeRow(s, kv.Key, kv.Value, has, get));
            foreach (var kv in prof.RequireParams)
                s.ReqRows.Add(MakeRow(s, kv.Key, kv.Value, has, get));

            s.ProfileMatch = s.Live && s.ParamCount > 0 && MatchesProfile(prof, s.Firmware, s.ParamCount, has, get);

            // checklist tu dong
            s.AutoOk = new bool[prof.Items.Count];
            s.AutoDetail = new string[prof.Items.Count];
            for (int i = 0; i < prof.Items.Count; i++)
            {
                var it = prof.Items[i];
                if (!it.IsAuto) continue;
                string d;
                s.AutoOk[i] = Eval(it.Check, s, prof, out d);
                s.AutoDetail[i] = d;
            }
            // dieu kien cung, danh gia trong code ke ca khi checklist JSON bo sot
            string dd;
            s.HardOk = Eval("prearm", s, prof, out dd) && Eval("gps", s, prof, out dd) && Eval("ekf", s, prof, out dd);
            return s;
        }

        private static ParamRow MakeRow(Snapshot s, string name, double want, Func<string, bool> has, Func<string, double> get)
        {
            var row = new ParamRow { Name = name, PresetValue = want };
            if (!s.Connected) row.Status = "Chưa kết nối";
            else if (!s.Fresh) row.Status = "Dữ liệu cũ";
            else if (s.ParamCount == 0) row.Status = "Chưa tải tham số";
            else
            {
                var vn = ResolveName(name, has);
                if (vn == null) row.Status = "THIẾU trên máy bay";
                else
                {
                    row.VehicleName = vn;
                    row.VehicleValue = get(vn);
                    row.Ok = Same(want, row.VehicleValue.Value);
                    row.Status = row.Ok ? "OK" : "KHÁC";
                    if (vn != name) row.Status += " (alias " + vn + ")";
                }
            }
            return row;
        }

        // Danh gia mot check tu dong. "Chua biet" luon tinh la that bai.
        private static bool Eval(string check, Snapshot s, Profile prof, out string d)
        {
            d = "";
            bool ok;
            switch (check)
            {
                case "connected":
                    ok = s.Live; if (!ok) d = s.Connected ? "MẤT TÍN HIỆU" : "chưa kết nối"; break;
                case "profileMatch":
                    ok = s.ProfileMatch; if (!ok) d = "firmware/tham số không khớp hồ sơ (" + s.Firmware + ")"; break;
                case "paramsMatchPreset":
                    ok = s.Live && s.ParamCount > 0 && s.Rows.Count > 0 && s.Rows.All(r => r.Ok);
                    if (!ok) d = s.Rows.Count(r => !r.Ok) + " tham số chưa khớp";
                    break;
                case "gps":
                    ok = s.Live && s.GpsFix >= 3 && s.Sats >= prof.MinSats && s.Hdop <= prof.MaxHdop;
                    if (!ok) d = string.Format(CultureInfo.InvariantCulture, "fix {0:0}, sats {1:0}, hdop {2:0.0}", s.GpsFix, s.Sats, s.Hdop);
                    break;
                case "ekf":
                    ok = s.Live && s.EkfSeen && s.EkfStatus < 0.5;
                    if (!ok) d = s.EkfSeen ? string.Format(CultureInfo.InvariantCulture, "ekf {0:0.00}", s.EkfStatus) : "chưa có EKF";
                    break;
                case "battery":
                    ok = s.Live && prof.CellCount > 0 && s.BattV / prof.CellCount > prof.CellWarnV;
                    if (!ok) d = string.Format(CultureInfo.InvariantCulture, "{0:0.0} V ({1:0.00} V/cell)", s.BattV, prof.CellCount > 0 ? s.BattV / prof.CellCount : 0);
                    break;
                case "airspeedStill":
                    ok = s.Live && s.HasAirspeed && !s.Armed && Math.Abs(s.Airspeed) < 3;
                    if (!ok) d = !s.HasAirspeed ? "không có cảm biến airspeed" : (s.Armed ? "đang ARM" : string.Format(CultureInfo.InvariantCulture, "{0:0.0} m/s", s.Airspeed));
                    break;
                case "vibe":
                    ok = s.Live && s.VibeSeen && s.VibeMax < prof.MaxVibe;
                    if (!ok) d = s.VibeSeen ? string.Format(CultureInfo.InvariantCulture, "{0:0.0}", s.VibeMax) : "chưa nhận VIBRATION";
                    break;
                case "prearm":
                    ok = s.Live && s.PrearmOk;
                    if (!ok) d = s.PrearmSeen ? "PreArm chưa đạt" : "chưa nhận SYS_STATUS";
                    break;
                default:
                    ok = false; d = "chưa hỗ trợ";
                    break;
            }
            return ok;
        }

        // ---------- cap nhat UI (thread UI)

        private static string VtolText(byte v)
        {
            switch (v)
            {
                case 3: return "MC";
                case 4: return "FW";
                case 1: return "CHUYỂN SANG FW";
                case 2: return "CHUYỂN SANG MC";
                default: return "KHÔNG RÕ";
            }
        }

        private static string LandedText(byte v)
        {
            switch (v)
            {
                case 1: return "Trên mặt đất";
                case 2: return "Đang bay";
                case 3: return "Đang cất cánh";
                case 4: return "Đang hạ cánh";
                default: return "Không rõ";
            }
        }

        // snapshot cuoi cung duoc ap dung khong qua 3 s
        private bool UiFresh
        {
            get { return lastApplyTick != 0 && Environment.TickCount - lastApplyTick < 3000; }
        }

        private bool AllManualTicked()
        {
            foreach (var kv in itemControls)
            {
                var cb = kv.Value as CheckBox;
                if (cb != null && !cb.Checked) return false;
            }
            return true;
        }

        // Dieu kien ARM: khong chi dua vao JSON.
        private bool ArmReady(Snapshot s, bool ignoreBusy)
        {
            var p = current;
            if (s == null || p == null || !ReferenceEquals(s.Profile, p)) return false;
            if (!UiFresh || !s.Connected || !s.Fresh || s.Armed) return false;
            if (p.Items.Count == 0 || !s.HardOk) return false;
            if (!ignoreBusy && busy) return false;
            for (int i = 0; i < p.Items.Count; i++)
            {
                if (p.Items[i].IsAuto && (i >= s.AutoOk.Length || !s.AutoOk[i])) return false;
            }
            return AllManualTicked();
        }

        private void ApplyGating(Snapshot s)
        {
            bool ui = UiFresh;
            bool live = s != null && s.Live && ui;
            bool armReady = ArmReady(s, false);
            btnArm.Enabled = armReady;
            btnArm.BackColor = armReady ? ColOk : ColDisabled;
            btnArm.ForeColor = Color.White;
            btnDisarm.Enabled = live && s.Armed && !busy;
            btnDisarm.BackColor = btnDisarm.Enabled ? ColBad : ColDisabled;
            btnDisarm.ForeColor = Color.White;
            btnApply.Enabled = live && !s.Armed && !busy && s.ParamCount > 0 && chosen;
            foreach (var b in modeButtons)
            {
                var m = (ModeDef)b.Tag;
                bool avail = live && s.Modes.Contains(m.Mode);
                bool active = live && string.Equals(s.Mode, m.Mode, StringComparison.OrdinalIgnoreCase);
                b.Enabled = avail && !busy;
                b.BackColor = !avail ? ColDisabled : (active ? ColActive : (m.Group == "vtol" ? ColVtol : ColFw));
                b.ForeColor = active ? Color.Black : Color.White;
            }
            if (s != null && (!ui || (s.Connected && !s.Fresh)))
            {
                lblConn.Text = "MẤT TÍN HIỆU / dữ liệu cũ";
                lblConn.ForeColor = ColBad;
            }
        }

        private void UpdateUi(Snapshot s)
        {
            try
            {
                if (IsDisposed) return;
                last = s;
                lastApplyTick = Environment.TickCount == 0 ? 1 : Environment.TickCount;

                // mat ket noi -> reset
                if (lastConnected && !s.Live)
                {
                    ResetManual();
                    userOverride = false;
                    chosen = false;
                }
                lastConnected = s.Live;

                // ARMED -> DISARMED: bat tick lai tu dau
                if (lastArmed && !s.Armed) ResetManual();
                lastArmed = s.Armed;

                // tu chon ho so
                if (!userOverride)
                {
                    if (s.AutoProfileIndex >= 0 && s.AutoProfileIndex < profiles.Count)
                    {
                        chosen = true;
                        if (!ReferenceEquals(profiles[s.AutoProfileIndex], current))
                        {
                            cmbProfile.SelectedIndex = s.AutoProfileIndex;
                            ApplyProfile(profiles[s.AutoProfileIndex]);
                            return; // snapshot tiep theo se dung ho so moi
                        }
                    }
                    else
                    {
                        chosen = false;
                    }
                }
                if (!ReferenceEquals(s.Profile, current)) return;

                var p = current;
                lblConn.Text = s.Connected
                    ? "Đã kết nối - sysid " + s.SysId + " - " + s.Firmware + (p != null ? " - hồ sơ: " + p.Name : "") + (chosen ? "" : " (chưa khớp hồ sơ)")
                    : "Chưa kết nối";
                lblConn.ForeColor = s.Connected ? ColOk : ColBad;

                // VTOL
                lblVtol.Text = s.Connected ? VtolText(s.VtolState) : "KHÔNG RÕ";
                lblVtol.ForeColor = s.VtolState == 3 || s.VtolState == 2 ? ColVtol : (s.VtolState == 4 || s.VtolState == 1 ? ColFw : ColDisabled);
                lblLanded.Text = "Landed: " + (s.Connected ? LandedText(s.LandedState) : "-");
                lblMode.Text = "Mode: " + (s.Connected ? s.Mode : "-") + (s.Armed ? "   [ARMED]" : "");
                lblAirspeed.Text = "Airspeed: " + s.Airspeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s   |   Min: "
                                   + (double.IsNaN(s.MinAirspeed) ? "-" : s.MinAirspeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s");
                lblWarn.Visible = s.AirspeedWarn;
                if (s.AirspeedWarn)
                    lblWarn.Text = "CẢNH BÁO: tốc độ gió " + s.Airspeed.ToString("0.0", CultureInfo.InvariantCulture)
                                   + " m/s quá gần tốc độ tối thiểu " + s.MinAirspeed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s ở chế độ FW!";
                lblAssist.Visible = !string.IsNullOrEmpty(s.AssistMsg);
                lblAssist.Text = s.AssistMsg == null ? "" : "Q_ASSIST: " + s.AssistMsg;

                // checklist
                if (p != null)
                {
                    for (int i = 0; i < p.Items.Count; i++)
                    {
                        var it = p.Items[i];
                        Control c;
                        if (!itemControls.TryGetValue(it, out c)) continue;
                        if (it.IsAuto)
                        {
                            bool ok = i < s.AutoOk.Length && s.AutoOk[i];
                            string det = i < s.AutoDetail.Length ? s.AutoDetail[i] : null;
                            c.Text = (ok ? "✔ " : "✖ ") + it.Text + (!ok && !string.IsNullOrEmpty(det) ? "  [" + det + "]" : "");
                            c.ForeColor = ok ? ColOk : ColBad;
                        }
                        else
                        {
                            var cb = (CheckBox)c;
                            c.Text = (cb.Checked ? "✔ " : "✖ ") + it.Text;
                            c.ForeColor = cb.Checked ? ColOk : ColBad;
                        }
                    }
                }
                ApplyGating(s);

                // param list (chi dung lai khi noi dung doi)
                var sig = new StringBuilder();
                sig.Append(p == null ? "" : p.Id).Append('#').Append(s.ParamCount);
                foreach (var r in s.Rows)
                    sig.Append('|').Append(r.Name).Append(',').Append(r.VehicleValue.HasValue ? r.VehicleValue.Value.ToString("R", CultureInfo.InvariantCulture) : "-").Append(',').Append(r.Status);
                if (sig.ToString() != signature)
                {
                    signature = sig.ToString();
                    lvParams.BeginUpdate();
                    lvParams.Items.Clear();
                    foreach (var r in s.Rows)
                    {
                        var li = new ListViewItem(r.Name);
                        li.SubItems.Add(r.PresetValue.ToString("0.####", CultureInfo.InvariantCulture));
                        li.SubItems.Add(r.VehicleValue.HasValue ? r.VehicleValue.Value.ToString("0.####", CultureInfo.InvariantCulture) : "-");
                        li.SubItems.Add(r.Status);
                        li.ForeColor = r.Ok ? ColOk : ColBad;
                        lvParams.Items.Add(li);
                    }
                    lvParams.EndUpdate();
                }
            }
            catch (Exception ex)
            {
                Log("UpdateUi", ex);
            }
        }

        // ---------- hanh dong

        private static string Num(double v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private DialogResult Ask(string text, string caption, bool warn)
        {
            return MissionPlanner.MsgBox.CustomMessageBox.Show(text, caption, MessageBoxButtons.YesNo,
                warn ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
        }

        private void OnMode(ModeDef m)
        {
            var s = last;
            if (s == null || !s.Live || !UiFresh) { SetMsg("Từ chối: mất tín hiệu / dữ liệu cũ.", true); return; }
            bool danger = m.Group == "fw" && s.Armed &&
                          (s.VtolState == 3 || (s.Mode ?? "").StartsWith("Q", StringComparison.OrdinalIgnoreCase));
            if (m.Confirm || danger)
            {
                string txt = danger
                    ? "CẢNH BÁO: máy bay đang ARM ở chế độ VTOL (" + s.Mode + "). Chuyển sang " + m.Label + " (cánh bằng) có thể làm mất lực nâng. Tiếp tục?"
                    : "Chuyển sang " + m.Label + "?";
                if (Ask(txt, "Xác nhận mode", danger) != DialogResult.Yes) return;
                var s2 = last;
                if (s2 == null || !s2.Live || !UiFresh) { SetMsg("Từ chối: mất tín hiệu / dữ liệu cũ.", true); return; }
            }
            var sys = s.SysId; var comp = s.CompId; var mode = m.Mode;
            SetMsg("Đang chuyển " + m.Label + "...", false);
            Task.Run(() =>
            {
                try
                {
                    MainV2.comPort.setMode(sys, comp, mode);
                    bool ok = false;
                    for (int i = 0; i < 30 && !ok; i++)
                    {
                        System.Threading.Thread.Sleep(100);
                        ok = string.Equals(MainV2.comPort.MAV.cs.mode, mode, StringComparison.OrdinalIgnoreCase);
                    }
                    if (ok) SetMsg("Đã chuyển sang " + m.Label + ".", false);
                    else SetMsg("Đã gửi lệnh " + m.Label + " nhưng không xác nhận được mode mới.", true);
                }
                catch (Exception ex)
                {
                    Log("setMode", ex);
                    SetMsg("Chuyển mode " + m.Label + " thất bại: " + ex.Message, true);
                }
            });
        }

        private void OnArm()
        {
            var s = last;
            if (!ArmReady(s, false)) { SetMsg("Từ chối ARM: điều kiện chưa đạt.", true); return; }
            busy = true;
            ApplyGating(s);
            try
            {
                if (Ask("ARM động cơ? Đảm bảo khu vực xung quanh an toàn.", "Xác nhận ARM", true) != DialogResult.Yes)
                {
                    busy = false;
                    return;
                }
                var s2 = last;
                if (!ArmReady(s2, true))
                {
                    busy = false;
                    SetMsg("Từ chối ARM: điều kiện không còn đạt (tín hiệu/checklist thay đổi).", true);
                    return;
                }
                DoArm(s2, true);
            }
            catch
            {
                busy = false;
                throw;
            }
        }

        private void OnDisarm()
        {
            var s = last;
            if (s == null || !s.Live || !UiFresh || !s.Armed) { SetMsg("Từ chối DISARM: không có tín hiệu / không ở trạng thái ARM.", true); return; }
            busy = true;
            ApplyGating(s);
            try
            {
                bool inAir = s.LandedState == 2;
                string txt = inAir
                    ? "CẢNH BÁO NGUY HIỂM: máy bay đang BAY (landed_state = IN_AIR). DISARM sẽ TẮT ĐỘNG CƠ và máy bay có thể RƠI. Chỉ làm khi thật sự khẩn cấp. Tiếp tục DISARM?"
                    : "DISARM động cơ?";
                if (Ask(txt, "Xác nhận DISARM", inAir) != DialogResult.Yes)
                {
                    busy = false;
                    return;
                }
                var s2 = last;
                if (s2 == null || !s2.Live || !UiFresh || !s2.Armed)
                {
                    busy = false;
                    SetMsg("Từ chối DISARM: trạng thái đã thay đổi.", true);
                    return;
                }
                DoArm(s2, false);
            }
            catch
            {
                busy = false;
                throw;
            }
        }

        private void DoArm(Snapshot s, bool arm)
        {
            var sys = s.SysId; var comp = s.CompId;
            busy = true;
            SetMsg(arm ? "Đang ARM..." : "Đang DISARM...", false);
            Task.Run(() =>
            {
                try
                {
                    bool ok = MainV2.comPort.doARM(sys, comp, arm, false);   // khong bao gio force
                    SetMsg(ok ? (arm ? "ARM thành công." : "DISARM thành công.") : (arm ? "ARM thất bại." : "DISARM thất bại."), !ok);
                }
                catch (Exception ex)
                {
                    Log("doARM", ex);
                    SetMsg((arm ? "ARM" : "DISARM") + " lỗi: " + ex.Message, true);
                }
                finally { busy = false; }
            });
        }

        private string ApplyRefusal(Snapshot s, Profile p)
        {
            if (s == null || p == null || !ReferenceEquals(s.Profile, p)) return "dữ liệu chưa sẵn sàng";
            if (!s.Live || !UiFresh) return "mất tín hiệu / dữ liệu cũ";
            if (s.Armed) return "máy bay đang ARM";
            if (!chosen) return "chưa xác định được hồ sơ cho máy bay này (không tự khớp và chưa chọn tay)";
            if (string.IsNullOrEmpty(p.Firmware) || !string.Equals(p.Firmware, s.Firmware, StringComparison.OrdinalIgnoreCase))
                return "firmware máy bay (" + s.Firmware + ") không khớp hồ sơ (" + p.Firmware + ")";
            if (s.ParamCount == 0) return "chưa tải tham số";
            return null;
        }

        private void OnApplyPreset()
        {
            var s = last; var p = current;
            var refusal = ApplyRefusal(s, p);
            if (refusal != null) { SetMsg("Từ chối áp preset: " + refusal + ".", true); return; }

            bool reqOnly = !s.ProfileMatch;
            var src = reqOnly ? s.ReqRows : s.Rows;
            var todo = src.Where(r => !r.Ok && r.VehicleName != null).ToList();
            int missing = src.Count(r => !r.Ok && r.VehicleName == null);
            if (todo.Count == 0)
            {
                SetMsg("Không có tham số nào cần ghi" + (missing > 0 ? " (" + missing + " tham số thiếu trên máy bay)." : "."), missing > 0);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Máy bay: sysid " + s.SysId + ", firmware " + s.Firmware);
            sb.AppendLine("Hồ sơ: " + p + (string.IsNullOrEmpty(p.Status) ? "" : "  [" + p.Status + "]"));
            if (reqOnly)
            {
                sb.AppendLine();
                sb.AppendLine("CẢNH BÁO: máy bay CHƯA khớp hồ sơ. Chỉ ghi các tham số điều kiện của hồ sơ.");
                sb.AppendLine("Sau đó cần REBOOT máy bay, rồi bấm 'Áp preset' lần thứ hai để ghi phần còn lại.");
            }
            sb.AppendLine();
            sb.AppendLine("Ghi " + todo.Count + " tham số (cũ → mới):");
            foreach (var r in todo)
                sb.AppendLine("  " + r.VehicleName + ": " + (r.VehicleValue.HasValue ? Num(r.VehicleValue.Value) : "-") + " → " + Num(r.PresetValue));
            if (missing > 0) sb.AppendLine("(" + missing + " tham số thiếu trên máy bay sẽ bị bỏ qua)");

            busy = true;
            ApplyGating(s);
            DialogResult ans;
            try { ans = Ask(sb.ToString(), "Áp preset", true); }
            catch (Exception ex) { Log("Ask apply", ex); ans = DialogResult.No; }
            if (ans != DialogResult.Yes)
            {
                busy = false;
                return;
            }
            var s2 = last;
            var refusal2 = ApplyRefusal(s2, p);
            if (refusal2 != null || !ReferenceEquals(s2, s) && s2.ProfileMatch != s.ProfileMatch)
            {
                busy = false;
                SetMsg("Từ chối áp preset: " + (refusal2 ?? "trạng thái đã thay đổi") + ".", true);
                return;
            }

            var sys = s.SysId; var comp = s.CompId;
            SetMsg("Đang ghi " + todo.Count + " tham số...", false);
            Task.Run(() =>
            {
                int okc = 0, fail = 0;
                try
                {
                    foreach (var r in todo)
                    {
                        if (MainV2.comPort.MAV.cs.armed) { fail += 1; break; }
                        try
                        {
                            if (MainV2.comPort.setParam(sys, comp, r.VehicleName, r.PresetValue, false)) okc++; else fail++;
                        }
                        catch (Exception ex) { Log("setParam " + r.VehicleName, ex); fail++; }
                    }
                    SetMsg("Áp preset xong: " + okc + " thành công, " + fail + " thất bại."
                           + (reqOnly && fail == 0 ? " Hãy REBOOT máy bay rồi áp preset lần nữa." : ""), fail > 0);
                }
                finally
                {
                    busy = false;
                    PostUi(() => { signature = ""; });
                }
            });
        }
    }
}
#endif

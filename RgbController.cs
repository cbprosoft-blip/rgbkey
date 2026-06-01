using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

public class RgbController : Form
{
    private readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

    
    private byte RuRed   = 255;
    private byte RuGreen = 0;
    private byte RuBlue  = 0;

    private byte EnRed   = 0;
    private byte EnGreen = 0;
    private byte EnBlue  = 255;

    private byte EffectMode = 0;   
    private byte Speed      = 2;   
    private byte Brightness = 2;   

    // WinAPI  HID
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(
        IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    // WinAPI Win
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    // WinAPI foe HOOks
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // Icon
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private string _activeDevicePath = "";
    private TextBox _logBox;
    private string _lastLanguage = "en";
    private int _currentInterface = 0;
    
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _trayMenu;
    private bool _allowVisible = false; 
    private Icon _generatedIcon; // Храним иконку, чтобы корректно удалить ее при выходе

    private List<string> _devicePaths = new List<string>();
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _hookDelegate; 

    private bool _isAltPressed = false;
    private bool _isShiftPressed = false;
    private bool _isCtrlPressed = false;

    private Button _btnRuColor;
    private Button _btnEnColor;
    private NumericUpDown _numEffect;
    private NumericUpDown _numSpeed;
    private NumericUpDown _numBrightness;

    public RgbController()
    {
        this.Text = "RK R87 RGB Controller (Safe Mode)";
        this.Size = new Size(720, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;

        // gen Icon
        _generatedIcon = GenerateAppIcon();
        this.Icon = _generatedIcon;

        LoadSettings();

        Panel settingsPanel = new Panel();
        settingsPanel.Dock = DockStyle.Top;
        settingsPanel.Height = 160;
        settingsPanel.BackColor = SystemColors.Control;
        this.Controls.Add(settingsPanel);

        BuildInterfaceControls(settingsPanel);

        _logBox = new TextBox();
        _logBox.Multiline = true;
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9);
        this.Controls.Add(_logBox);
        _logBox.BringToFront(); 

        Log("=== RK R87 Safe RGB Controller ===");
        
        CreateTrayIcon();
        AddDevicePaths();
        FindWorkingInterface();
        
        if (!string.IsNullOrEmpty(_activeDevicePath))
        {
            Timer t = new Timer();
            t.Interval = 250;
            t.Tick += MonitorLayout;
            t.Start();
            
            _hookDelegate = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookDelegate, GetModuleHandle(curModule.ModuleName), 0);
            }
            Log("Мониторинг раскладки и RDP запущен.");
        }

        this.SizeChanged += RgbController_SizeChanged;
        this.FormClosing += (s, e) => {
            if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId); 
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            
            // clear memory
            if (_generatedIcon != null)
            {
                IntPtr hIcon = _generatedIcon.Handle;
                _generatedIcon.Dispose();
                DestroyIcon(hIcon);
            }
        };
    }

    // create icon
    private Icon GenerateAppIcon()
    {
        try
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    
                    // 1. Базовый темный силуэт девайса
                    using (Brush bgBrush = new SolidBrush(Color.FromArgb(35, 35, 40)))
                    {
                        g.FillRectangle(bgBrush, 2, 6, 28, 20);
                    }
                    
                    // Контур корпуса клавиатуры
                    using (Pen framePen = new Pen(Color.FromArgb(90, 90, 100), 1))
                    {
                        g.DrawRectangle(framePen, 2, 6, 28, 20);
                    }

                    // 2. Имитируем светящиеся зоны клавиш (RGB градиент-блоки)
                    g.FillRectangle(Brushes.Crimson,    5,  11, 6,  10); // R
                    g.FillRectangle(Brushes.LimeGreen,  13, 11, 6,  10); // G
                    g.FillRectangle(Brushes.DodgerBlue, 21, 11, 6,  10); // B

                    // 3. Маленький акцентный штрих снизу (пробел)
                    using (Brush spaceBrush = new SolidBrush(Color.FromArgb(150, 150, 160)))
                    {
                        g.FillRectangle(spaceBrush, 10, 22, 12, 2);
                    }
                }
                
                // get descr
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch
        {
            // not GDI
            return SystemIcons.Application;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int msg = wParam.ToInt32();

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5) _isAltPressed = true;
                if (vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1) _isShiftPressed = true;
                if (vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3) _isCtrlPressed = true;

                if ((_isAltPressed && _isShiftPressed) || (_isCtrlPressed && _isShiftPressed))
                {
                    CheckAndHandleRdpSwitch();
                }
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                if (vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5) _isAltPressed = false;
                if (vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1) _isShiftPressed = false;
                if (vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3) _isCtrlPressed = false;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void CheckAndHandleRdpSwitch()
    {
        try
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(foregroundWindow, out pid);
            
            using (Process p = Process.GetProcessById((int)pid))
            {
                if (p.ProcessName.ToLower().Contains("mstsc"))
                {
                    _lastLanguage = (_lastLanguage == "ru") ? "en" : "ru";
                    Log(string.Format("🔁 [RDP] Переключение на: '{0}'", _lastLanguage));
                    ForceUpdateCurrentLayout();
                }
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            List<string> lines = new List<string>
            {
                "RuRed=" + RuRed, "RuGreen=" + RuGreen, "RuBlue=" + RuBlue,
                "EnRed=" + EnRed, "EnGreen=" + EnGreen, "EnBlue=" + EnBlue,
                "EffectMode=" + EffectMode, "Speed=" + Speed, "Brightness=" + Brightness
            };
            File.WriteAllLines(ConfigPath, lines);
        }
        catch (Exception ex) { Log("Config save error: " + ex.Message); }
    }

    private void LoadSettings()
    {
        if (!File.Exists(ConfigPath)) return;
        try
        {
            string[] lines = File.ReadAllLines(ConfigPath);
            foreach (string line in lines)
            {
                string[] parts = line.Split('=');
                if (parts.Length != 2) continue;
                string key = parts[0].Trim();
                byte val = byte.Parse(parts[1].Trim());

                switch (key)
                {
                    case "RuRed": RuRed = val; break;
                    case "RuGreen": RuGreen = val; break;
                    case "RuBlue": RuBlue = val; break;
                    case "EnRed": EnRed = val; break;
                    case "EnGreen": EnGreen = val; break;
                    case "EnBlue": EnBlue = val; break;
                    case "EffectMode": EffectMode = val; break;
                    case "Speed": Speed = val; break;
                    case "Brightness": Brightness = val; break;
                }
            }
            
            if (EffectMode > 20) EffectMode = 0;
            if (Speed > 5) Speed = 2;
            if (Brightness > 5) Brightness = 2;
        }
        catch { }
    }

    private void BuildInterfaceControls(Panel panel)
    {
        GroupBox gbColors = new GroupBox() { Text = " Настройка цветов раскладки ", Location = new Point(15, 10), Size = new Size(340, 135) };
        Label lblRu = new Label() { Text = "Русский язык (RU):", Location = new Point(15, 32), Size = new Size(120, 20) };
        _btnRuColor = new Button() { Location = new Point(145, 27), Size = new Size(170, 30), BackColor = Color.FromArgb(RuRed, RuGreen, RuBlue), FlatStyle = FlatStyle.Flat };
        _btnRuColor.Click += ChooseRuColor;

        Label lblEn = new Label() { Text = "Английский (EN):", Location = new Point(15, 82), Size = new Size(130, 20) };
        _btnEnColor = new Button() { Location = new Point(145, 77), Size = new Size(170, 30), BackColor = Color.FromArgb(EnRed, EnGreen, EnBlue), FlatStyle = FlatStyle.Flat };
        _btnEnColor.Click += ChooseEnColor;

        gbColors.Controls.Add(lblRu); gbColors.Controls.Add(_btnRuColor);
        gbColors.Controls.Add(lblEn); gbColors.Controls.Add(_btnEnColor);

        GroupBox gbEffect = new GroupBox() { Text = " Safe params ", Location = new Point(370, 10), Size = new Size(320, 135) };
        Label lblEff = new Label() { Text = "Эффект (0-20):", Location = new Point(20, 32), Size = new Size(130, 20) };
        _numEffect = new NumericUpDown() { Location = new Point(160, 30), Size = new Size(70, 20), Minimum = 0, Maximum = 20, Value = EffectMode };
        _numEffect.ValueChanged += (s, e) => { EffectMode = (byte)_numEffect.Value; SaveSettings(); ForceUpdateCurrentLayout(); };

        Label lblSpd = new Label() { Text = "Speed (0-5):", Location = new Point(20, 67), Size = new Size(130, 20) };
        _numSpeed = new NumericUpDown() { Location = new Point(160, 65), Size = new Size(70, 20), Minimum = 0, Maximum = 5, Value = Speed };
        _numSpeed.ValueChanged += (s, e) => { Speed = (byte)_numSpeed.Value; SaveSettings(); ForceUpdateCurrentLayout(); };

        Label lblBrt = new Label() { Text = "Ligth (0-5):", Location = new Point(20, 102), Size = new Size(130, 20) };
        _numBrightness = new NumericUpDown() { Location = new Point(160, 100), Size = new Size(70, 20), Minimum = 0, Maximum = 5, Value = Brightness };
        _numBrightness.ValueChanged += (s, e) => { Brightness = (byte)_numBrightness.Value; SaveSettings(); ForceUpdateCurrentLayout(); };

        gbEffect.Controls.Add(lblEff); gbEffect.Controls.Add(_numEffect);
        gbEffect.Controls.Add(lblSpd); gbEffect.Controls.Add(_numSpeed);
        gbEffect.Controls.Add(lblBrt); gbEffect.Controls.Add(_numBrightness);

        panel.Controls.Add(gbColors); panel.Controls.Add(gbEffect);
    }

    private void ChooseRuColor(object sender, EventArgs e)
    {
        using (ColorDialog cd = new ColorDialog())
        {
            cd.Color = _btnRuColor.BackColor;
            if (cd.ShowDialog() == DialogResult.OK)
            {
                _btnRuColor.BackColor = cd.Color;
                RuRed = cd.Color.R; RuGreen = cd.Color.G; RuBlue = cd.Color.B;
                SaveSettings(); ForceUpdateCurrentLayout();
            }
        }
    }

    private void ChooseEnColor(object sender, EventArgs e)
    {
        using (ColorDialog cd = new ColorDialog())
        {
            cd.Color = _btnEnColor.BackColor;
            if (cd.ShowDialog() == DialogResult.OK)
            {
                _btnEnColor.BackColor = cd.Color;
                EnRed = cd.Color.R; EnGreen = cd.Color.G; EnBlue = cd.Color.B;
                SaveSettings(); ForceUpdateCurrentLayout();
            }
        }
    }

    private void ForceUpdateCurrentLayout()
    {
        if (_lastLanguage == "ru")
            SendRgb(RuRed, RuGreen, RuBlue);
        else
            SendRgb(EnRed, EnGreen, EnBlue);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowVisible) 
        {
            value = false;
            if (!this.IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Открыть настройки", null, OnTrayOpen);
        _trayMenu.Items.Add("Выход", null, OnTrayExit);

        _trayIcon = new NotifyIcon();
        _trayIcon.Text = "RK R87 RGB Controller";
        _trayIcon.Icon = _generatedIcon; // To tray
        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += OnTrayOpen;
    }

    private void OnTrayOpen(object sender, EventArgs e)
    {
        _allowVisible = true;
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.ShowInTaskbar = true;
    }

    private void OnTrayExit(object sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    private void RgbController_SizeChanged(object sender, EventArgs e)
    {
        if (this.WindowState == FormWindowState.Minimized)
        {
            this.Hide();
            this.ShowInTaskbar = false;
        }
    }

    private void AddDevicePaths()
    {
        _devicePaths.Add(@"\\?\hid#vid_258a&pid_00f6&mi_01&col03#8&35bd3e20&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        _devicePaths.Add(@"\\?\hid#vid_258a&pid_00f6&mi_01&col05#8&35bd3e20&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        _devicePaths.Add(@"\\?\hid#vid_258a&pid_00f6&mi_01&col02#8&35bd3e20&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        _devicePaths.Add(@"\\?\hid#vid_258a&pid_00f6&mi_01&col01#8&35bd3e20&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        _devicePaths.Add(@"\\?\hid#vid_258a&pid_00f6&mi_01&col04#8&35bd3e20&0&0003#{4d1e55b2-f16f-11cf-88cb-001111000030}\kbd");
        _devicePaths.Add(@"\\?\hid#vid_258a&pid_00f6&mi_00#8&11e6005e&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}\kbd");
    }

    private void FindWorkingInterface()
    {
        foreach (string path in _devicePaths)
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.ToInt64() == -1 || handle == IntPtr.Zero)
            {
                _currentInterface++;
                continue;
            }
            
            byte[] reportWithId = new byte[65];
            reportWithId[0] = 10; reportWithId[1] = 1; reportWithId[2] = 1;            
            reportWithId[3] = Brightness; reportWithId[4] = Speed; reportWithId[5] = EffectMode;   
            reportWithId[6] = 0; reportWithId[7] = 1; reportWithId[8] = 1;            
            reportWithId[9] = RuRed; reportWithId[10] = RuGreen; reportWithId[11] = RuBlue;      
            reportWithId[12] = 0; reportWithId[13] = 1;           
            
            bool success = HidD_SetFeature(handle, reportWithId, reportWithId.Length);
            CloseHandle(handle); 
            
            if (success)
            {
                Log(string.Format("✅ Key Init (Interface №{0}).", _currentInterface + 1));
                _activeDevicePath = path; 
                return;
            }
            _currentInterface++;
        }
    }

    private string GetActiveWindowLanguage()
    {
        try
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            uint pid;
            uint threadId = GetWindowThreadProcessId(foregroundWindow, out pid);
            
            using (Process p = Process.GetProcessById((int)pid))
            {
                if (p.ProcessName.ToLower().Contains("mstsc"))
                    return _lastLanguage; 
            }

            IntPtr layoutId = GetKeyboardLayout(threadId);
            int langId = (int)layoutId & 0xFFFF;
            return new System.Globalization.CultureInfo(langId).TwoLetterISOLanguageName.ToLower();
        }
        catch { return "en"; }
    }

    private void MonitorLayout(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_activeDevicePath)) return;
        try
        {
            string currentLayout = GetActiveWindowLanguage();
            if (currentLayout != _lastLanguage)
            {
                _lastLanguage = currentLayout;
                Log(string.Format("📢 Change layout: '{0}'", currentLayout));
                ForceUpdateCurrentLayout();
            }
        }
        catch (Exception ex) { Log(string.Format("Error: {0}", ex.Message)); }
    }

    private void SendRgb(byte r, byte g, byte b)
    {
        if (string.IsNullOrEmpty(_activeDevicePath)) return;
        
        byte safeEffect = (EffectMode > 20) ? (byte)0 : EffectMode;
        byte safeSpeed = (Speed > 5) ? (byte)2 : Speed;
        byte safeBright = (Brightness > 5) ? (byte)2 : Brightness;

        IntPtr handle = CreateFile(_activeDevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.ToInt64() == -1 || handle == IntPtr.Zero) return;
        
        try
        {
            byte[] reportWithId = new byte[65];
            reportWithId[0] = 10; reportWithId[1] = 1; reportWithId[2] = 1;            
            reportWithId[3] = safeBright; reportWithId[4] = safeSpeed; reportWithId[5] = safeEffect;   
            reportWithId[6] = 0; reportWithId[7] = 1; reportWithId[8] = 1;            
            reportWithId[9] = r; reportWithId[10] = g; reportWithId[11] = b;           
            reportWithId[12] = 0; reportWithId[13] = 1;           
            
            HidD_SetFeature(handle, reportWithId, reportWithId.Length);
        }
        finally { CloseHandle(handle); }
    }

    private void Log(string message)
    {
        if (_logBox.InvokeRequired)
        {
            _logBox.Invoke(new Action<string>(Log), message);
            return;
        }
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logBox.AppendText(string.Format("[{0}] {1}\r\n", timestamp, message));
        _logBox.ScrollToCaret();
    }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new RgbController());
    }
}

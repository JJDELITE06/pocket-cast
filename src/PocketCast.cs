using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace PocketCast
{
    public sealed class UsbNetworkAdapter
    {
        public string Id, Name, Address;
        public override string ToString() { return Name + "  (" + Address + ")"; }
        public static bool IsAppleEthernet(string description)
        {
            string value = (description ?? "").ToLowerInvariant();
            return value.Contains("apple mobile device ethernet") || value.Contains("apple usb ethernet")
                || value.Contains("appleusbethernet") || value.Contains("iphone ethernet");
        }
        public static List<UsbNetworkAdapter> Available()
        {
            var adapters = new List<UsbNetworkAdapter>();
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType != NetworkInterfaceType.Ethernet
                    || !IsAppleEthernet(network.Description)) continue;
                foreach (var address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] octets = address.Address.GetAddressBytes();
                    if (octets[0] == 0 || octets[0] == 127 || octets[0] >= 224 || (octets[0] == 169 && octets[1] == 254)) continue;
                    adapters.Add(new UsbNetworkAdapter { Id = network.Id, Name = network.Name, Address = address.Address.ToString() });
                }
            }
            return adapters;
        }
    }

    public static class ReceiverOptions
    {
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c); slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }

        public static string Arguments(string name, int quality, bool audio, int port, string dataPath, string pin)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 48 || name.Any(Char.IsControl))
                throw new ArgumentException("Use a receiver name between 1 and 48 characters.");
            if (quality < 0 || quality > 2) throw new ArgumentOutOfRangeException("quality");
            if (port < 1024 || port > 65533) throw new ArgumentOutOfRangeException("port");
            string[] dimensions = { "3840x2160@60", "1920x1080@60", "1280x720@60" };
            var args = new List<string> { "-n", name.Trim(), "-nh", "-s", dimensions[quality], "-fps", "60",
                "-vs", "d3d11videosink", "-as", audio ? "wasapisink" : "0", "-p", port.ToString(),
                "-d", "1", "-rc", Path.Combine(dataPath, "receiver.rc"), "-key", Path.Combine(dataPath, "receiver.pem") };
            if (quality == 0) args.Add("-h265");
            if (!String.IsNullOrEmpty(pin)) { args.Add("-pin"); args.Add(pin); }
            return String.Join(" ", args.Select(Quote));
        }
    }

    internal sealed class ChildJob : IDisposable
    {
        private IntPtr handle;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int info, IntPtr data, uint length);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long PerProcess, PerJob;
            public uint Flags;
            public UIntPtr MinWorking, MaxWorking;
            public uint ActiveProcess;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong A, B, C, D, E, F; }
        [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob;
        }
        public ChildJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Cannot create receiver process group.");
            var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000;
            int size = Marshal.SizeOf(limits); IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, memory, false);
                if (!SetInformationJobObject(handle, 9, memory, (uint)size)) throw new InvalidOperationException("Cannot configure receiver process group.");
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        public void Add(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle)) throw new InvalidOperationException("Cannot manage the receiver process safely.");
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }

    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Contains("--self-test")) return SelfTest();
            bool created;
            using (var single = new System.Threading.Mutex(true, "Local\\PocketCast-Desktop", out created))
            {
                if (!created) { MessageBox.Show("Pocket Cast is already open.", "Pocket Cast"); return 0; }
                var app = new Application();
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                { MessageBox.Show(e.Exception.Message, "Pocket Cast"); e.Handled = true; };
                app.Run(new MainWindow());
            }
            return 0;
        }
        static int SelfTest()
        {
            try
            {
                string args = ReceiverOptions.Arguments("My PC", 0, true, 7100, @"C:\Test Folder", "1234");
                if (!args.Contains("-h265") || !args.Contains("3840x2160@60") || !args.Contains("wasapisink") || !args.Contains("\"60\"")) return 1;
                args = ReceiverOptions.Arguments("Laptop", 1, false, 7103, @"C:\Test", null);
                if (args.Contains("-h265") || !args.Contains("1920x1080@60") || !args.Contains("\"-as\" \"0\"")) return 2;
                try { ReceiverOptions.Arguments("bad\nname", 0, true, 7100, ".", null); return 3; } catch (ArgumentException) { }
                if (ReceiverOptions.Quote("a\"b") != "\"a\\\"b\"") return 4;
                if (ReceiverOptions.Quote("C:\\") != "\"C:\\\\\"") return 5;
                if (!UsbNetworkAdapter.IsAppleEthernet("Apple Mobile Device Ethernet")) return 6;
                if (UsbNetworkAdapter.IsAppleEthernet("Qualcomm Wi-Fi") || UsbNetworkAdapter.IsAppleEthernet("Hyper-V Virtual Ethernet Adapter")) return 7;
                return 0;
            }
            catch { return 9; }
        }
    }

    public class MainWindow : Window
    {
        private TextBox nameBox, logBox;
        private ComboBox qualityBox, connectionBox, usbAdapterBox;
        private CheckBox audioBox, pinBox;
        private Button startButton, stopButton;
        private TextBlock statusText, statusDetail, pinText, networkText;
        private Border statusPill;
        private Border usbPanel;
        private Button refreshUsbButton;
        private TextBlock usbHint, stepOneTitle, stepOneDetail, stepTwoDetail;
        private readonly DispatcherTimer networkTimer = new DispatcherTimer();
        private string activeUsbAddress;
        private string lastEngineError;
        private Process receiver;
        private ChildJob job;
        private bool closing, advertised, connected;
        private int generation;
        private readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketCast");
        private readonly StringBuilder logs = new StringBuilder();
        private readonly DispatcherTimer startupTimer = new DispatcherTimer();
        private string currentPin = "";
        private DateTime started;

        public MainWindow()
        {
            Title = "Pocket Cast"; Width = 1040; Height = Math.Min(900, SystemParameters.WorkArea.Height - 40); MinWidth = 820; MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("#0C1119");
            Foreground = Brush("#EDF3FC"); FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(Path.Combine(dataPath, "receiver.rc"), "# Pocket Cast supplies explicit options.\n");
            Content = XamlReader.Parse(Xaml());
            nameBox = Find<TextBox>("ReceiverName"); qualityBox = Find<ComboBox>("Quality");
            audioBox = Find<CheckBox>("Audio"); pinBox = Find<CheckBox>("RequirePin");
            startButton = Find<Button>("Start"); stopButton = Find<Button>("Stop");
            statusText = Find<TextBlock>("Status"); statusDetail = Find<TextBlock>("StatusDetail");
            statusPill = Find<Border>("StatusPill"); pinText = Find<TextBlock>("Pin");
            networkText = Find<TextBlock>("Network"); logBox = Find<TextBox>("Log");
            connectionBox = Find<ComboBox>("Connection"); usbAdapterBox = Find<ComboBox>("UsbAdapter");
            usbPanel = Find<Border>("UsbPanel"); usbHint = Find<TextBlock>("UsbHint");
            refreshUsbButton = Find<Button>("RefreshUsb");
            stepOneTitle = Find<TextBlock>("StepOneTitle"); stepOneDetail = Find<TextBlock>("StepOneDetail");
            stepTwoDetail = Find<TextBlock>("StepTwoDetail");
            nameBox.Text = "Pocket Cast - " + Environment.MachineName;
            if (nameBox.Text.Length > 48) nameBox.Text = nameBox.Text.Substring(0, 48);
            LoadSettings();
            networkText.Text = GetNetworkText();
            connectionBox.SelectionChanged += delegate { UpdateTransport(); };
            refreshUsbButton.Click += delegate { RefreshUsbAdapters(); };
            Find<Button>("UsbSetup").Click += delegate { ShowUsbHelp(); };
            UpdateTransport();
            startButton.Click += async delegate { await StartReceiver(); };
            stopButton.Click += delegate { StopReceiver(false); };
            Find<Button>("CopyName").Click += delegate { Clipboard.SetText(nameBox.Text.Trim()); };
            Find<Button>("SaveLog").Click += delegate { SaveLog(); };
            Find<Button>("Help").Click += delegate { ShowHelp(); };
            Closing += delegate { closing = true; networkTimer.Stop(); SaveSettings(); StopReceiver(true); };
            networkTimer.Interval = TimeSpan.FromSeconds(2);
            networkTimer.Tick += delegate
            {
                if (UsbMode && receiver == null) RefreshUsbAdapters();
                if (receiver != null && activeUsbAddress != null)
                {
                    try
                    {
                        if (!UsbNetworkAdapter.Available().Any(a => a.Address == activeUsbAddress))
                            StopForUsbLoss("Reconnect the data cable and enable Personal Hotspot, then start the receiver again.");
                    }
                    catch (NetworkInformationException)
                    {
                        StopForUsbLoss("Windows could not verify the USB network connection. Reconnect the cable and start again.");
                    }
                }
            };
            networkTimer.Start();
            startupTimer.Interval = TimeSpan.FromSeconds(1);
            startupTimer.Tick += delegate
            {
                if (receiver != null && !advertised && DateTime.UtcNow.Subtract(started).TotalSeconds > 60)
                {
                    startupTimer.Stop();
                    SetStatus("Check connection", "The receiver has not confirmed discovery. Open Connection help or save the diagnostic log.", "#FCC777");
                }
            };
        }

        private T Find<T>(string name) where T : FrameworkElement { return (T)((FrameworkElement)Content).FindName(name); }
        private static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }

        private bool UsbMode { get { return connectionBox.SelectedIndex == 1; } }
        private void StopForUsbLoss(string detail)
        {
            StopReceiver(true); RefreshUsbAdapters(); pinText.Text = "PIN appears when you start";
            SetStatus("USB disconnected", detail, "#FCC777");
            Log("USB network connection unavailable; receiver stopped without switching to Wi-Fi.");
        }
        private static bool IsArm64 { get { return String.Equals(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"), "ARM64", StringComparison.OrdinalIgnoreCase) || String.Equals(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"), "ARM64", StringComparison.OrdinalIgnoreCase); } }
        private void UpdateTransport()
        {
            usbPanel.Visibility = UsbMode ? Visibility.Visible : Visibility.Collapsed;
            stepOneTitle.Text = UsbMode ? "1   Plug in your iPhone" : "1   Use the same network";
            stepOneDetail.Text = UsbMode ? "Use a data cable, unlock your iPhone, tap Trust, and enable Personal Hotspot. Windows needs an iPhone Ethernet connection." : "Connect your iPhone and PC to the same home Wi-Fi. Ethernet works for your PC too.";
            stepTwoDetail.Text = UsbMode ? "Keep the cable attached. Open Control Center on iPhone and tap Screen Mirroring." : "On iPhone, open Control Center and tap the two overlapping rectangles.";
            RefreshUsbAdapters();
            if (receiver == null) SetStatus("Receiver is off", UsbMode ? "Connect a USB data cable and enable Personal Hotspot, then start the receiver." : "Start the receiver when you're ready to share your iPhone.", "#99A8BF");
        }
        private void RefreshUsbAdapters()
        {
            if (receiver != null) return;
            try
            {
                var selected = usbAdapterBox.SelectedItem as UsbNetworkAdapter;
                var adapters = UsbNetworkAdapter.Available();
                usbAdapterBox.ItemsSource = adapters;
                usbAdapterBox.SelectedItem = selected == null ? adapters.FirstOrDefault() : adapters.FirstOrDefault(a => a.Id == selected.Id && a.Address == selected.Address) ?? adapters.FirstOrDefault();
                usbHint.Text = adapters.Count == 0 ? (IsArm64 ? "No iPhone Ethernet connection. This ARM PC needs a compatible ARM64 tethering driver; Apple Devices alone may not provide it." : "No active iPhone USB Ethernet connection found. See USB setup.") : "USB network detected. Discovery uses this cable connection; keep your iPhone disconnected from other Wi-Fi during mirroring.";
            }
            catch (Exception ex) { usbHint.Text = "Could not inspect USB networking: " + ex.Message; }
        }

        private async Task StartReceiver()
        {
            if (receiver != null) return;
            string engine = Path.Combine(root, "runtime", "uxplay.exe");
            if (!File.Exists(engine))
            {
                SetStatus("Receiver missing", "Extract the entire ZIP before opening PocketCast.exe. Keep the runtime folder beside it.", "#FCC777");
                return;
            }
            int token = ++generation;
            try
            {
                activeUsbAddress = null;
                lastEngineError = null;
                if (UsbMode)
                {
                    RefreshUsbAdapters();
                    var selected = usbAdapterBox.SelectedItem as UsbNetworkAdapter;
                    if (selected == null)
                    {
                        SetStatus("USB connection needed", "Windows has not detected an iPhone Ethernet connection. Connect a data cable, tap Trust, enable Personal Hotspot, and check USB setup.", "#FCC777");
                        return;
                    }
                    activeUsbAddress = selected.Address;
                }
                string selectedName = nameBox.Text.Trim();
                int port = FindPorts();
                currentPin = pinBox.IsChecked == true ? NewPin() : "";
                string arguments = ReceiverOptions.Arguments(selectedName, qualityBox.SelectedIndex, audioBox.IsChecked == true, port, dataPath, currentPin);
                advertised = false; connected = false;
                SetBusy(true); SaveSettings();
                pinText.Text = currentPin.Length == 0 ? "PIN is off" : "Connection PIN  " + currentPin;
                SetStatus("Starting receiver", "Preparing screen and audio playback...", "#89AFFF");
                Log("Starting Pocket Cast. Requested quality: " + ((ComboBoxItem)qualityBox.SelectedItem).Content + "; 60 fps maximum.");
                var info = new ProcessStartInfo(engine, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dataPath,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                string runtime = Path.Combine(root, "runtime");
                info.EnvironmentVariables["PATH"] = runtime + ";" + info.EnvironmentVariables["PATH"];
                info.EnvironmentVariables["GST_PLUGIN_PATH_1_0"] = Path.Combine(runtime, "lib", "gstreamer-1.0");
                info.EnvironmentVariables["GST_PLUGIN_SYSTEM_PATH_1_0"] = Path.Combine(runtime, "lib", "gstreamer-1.0");
                string scanner = Path.Combine(runtime, "gst-plugin-scanner.exe");
                if (File.Exists(scanner)) info.EnvironmentVariables["GST_PLUGIN_SCANNER_1_0"] = scanner;
                info.EnvironmentVariables["GST_REGISTRY_1_0"] = Path.Combine(dataPath, "gstreamer-registry.bin");
                info.EnvironmentVariables["HOME"] = dataPath;
                info.EnvironmentVariables["UXPLAYRC"] = Path.Combine(dataPath, "receiver.rc");
                info.EnvironmentVariables["GST_DEBUG_NO_COLOR"] = "1";
                info.EnvironmentVariables.Remove("POCKETCAST_IPV4");
                if (activeUsbAddress != null) info.EnvironmentVariables["POCKETCAST_IPV4"] = activeUsbAddress;
                Log(activeUsbAddress == null ? "Connection: local Wi-Fi / Ethernet." : "Connection: USB tethering on " + activeUsbAddress + ".");
                // Avoid driver-dependent D3D12 decode failures; prefer the stable D3D11 path.
                info.EnvironmentVariables["GST_PLUGIN_FEATURE_RANK"] = "d3d12h264dec:NONE,d3d12h265dec:NONE";
                job = new ChildJob();
                var process = new Process { StartInfo = info, EnableRaisingEvents = true };
                receiver = process;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { ReceiveLine(e.Data, token); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { ReceiveLine(e.Data, token); };
                process.Exited += delegate
                {
                    if (!closing) Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (generation != token || receiver != process) return;
                        int code = process.ExitCode;
                        StopReceiver(true);
                        SetStatus("Receiver stopped", lastEngineError ?? ("The receiver exited (code " + code + "). Open Connection help or save the diagnostic log."), "#FCC777");
                    }));
                };
                process.Start(); job.Add(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                started = DateTime.UtcNow; startupTimer.Start();
                await Task.Delay(300);
                if (token == generation && receiver != null && !receiver.HasExited)
                    Log("Receiver process started. Waiting for its AirPlay discovery confirmation.");
            }
            catch (Exception ex)
            {
                if (token != generation) return;
                StopReceiver(true); Log("Start failed: " + ex.Message);
                SetStatus("Could not start", ex.Message, "#FCC777");
            }
        }

        private void ReceiveLine(string line, int token)
        {
            if (line == null || closing) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (token != generation) return;
                Log(line);
                if (line.StartsWith("Pocket Cast:") && !line.Contains("uses selected interface")) lastEngineError = line;
                if (line.IndexOf("advertised AirPlay service", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    advertised = true; startupTimer.Stop();
                    SetStatus(activeUsbAddress == null ? "Ready to connect" : "USB ready to connect", "On your iPhone, open Control Center, tap Screen Mirroring, and choose " + nameBox.Text.Trim() + "." + (activeUsbAddress == null ? "" : " Keep the USB cable connected."), "#64DFC5");
                }
                if (line.IndexOf("raop_rtp_mirror starting", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Begin streaming", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    connected = true;
                    SetStatus("iPhone connected", "Your iPhone picture opens in a separate resizable video window. " + (audioBox.IsChecked == true ? "Audio plays through your PC's default output." : "Audio playback is disabled."), "#64DFC5");
                }
                if (connected && line.IndexOf("Open connections: 0", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    connected = false;
                    SetStatus("Ready to reconnect", "Select this PC in your iPhone's Screen Mirroring menu again.", "#64DFC5");
                }
            }));
        }

        private void StopReceiver(bool quiet)
        {
            ++generation; startupTimer.Stop();
            var oldProcess = receiver; receiver = null;
            try { if (job != null) job.Dispose(); } catch { } finally { job = null; }
            if (oldProcess != null)
            {
                try { if (!oldProcess.WaitForExit(1500)) { oldProcess.Kill(); oldProcess.WaitForExit(1500); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                oldProcess.Dispose();
            }
            advertised = false; connected = false; activeUsbAddress = null; SetBusy(false);
            if (!quiet)
            {
                pinText.Text = "PIN appears when you start";
                SetStatus("Receiver is off", "Start the receiver when you're ready to share your iPhone.", "#99A8BF");
                Log("Receiver stopped.");
            }
        }

        private void SetBusy(bool active)
        {
            startButton.IsEnabled = !active; stopButton.IsEnabled = active;
            nameBox.IsEnabled = qualityBox.IsEnabled = audioBox.IsEnabled = pinBox.IsEnabled = !active;
            connectionBox.IsEnabled = usbAdapterBox.IsEnabled = refreshUsbButton.IsEnabled = !active;
        }
        private void SetStatus(string title, string detail, string color)
        {
            statusText.Text = title; statusText.Foreground = Brush(color); statusDetail.Text = detail;
            statusPill.BorderBrush = Brush(color);
        }
        private void Log(string line)
        {
            // Pairing secrets stay on screen and never enter exported diagnostics.
            if (line.StartsWith("          ") && line.Length >= 35 && line.All(c => " 8dbPYo\".".IndexOf(c) >= 0)) return;
            if (!String.IsNullOrEmpty(currentPin)) line = line.Replace(currentPin, "[PIN]");
            if (line.IndexOf("PRIVATE KEY", StringComparison.OrdinalIgnoreCase) >= 0) return;
            logs.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (logs.Length > 80000) logs.Remove(0, logs.Length - 60000);
            logBox.Text = logs.ToString(); logBox.ScrollToEnd();
        }
        private static string NewPin()
        {
            byte[] bytes = new byte[4]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return (1 + BitConverter.ToUInt32(bytes, 0) % 9999).ToString("D4");
        }
        private static int FindPorts()
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var used = new HashSet<int>(properties.GetActiveTcpListeners().Select(p => p.Port)
                .Concat(properties.GetActiveUdpListeners().Select(p => p.Port)));
            for (int port = 7100; port <= 7197; port += 3)
            {
                if (!used.Contains(port) && !used.Contains(port + 1) && !used.Contains(port + 2)) return port;
            }
            throw new InvalidOperationException("No receiver ports are free. Close other mirroring apps and try again.");
        }
        private static string GetNetworkText()
        {
            try
            {
                var addresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
                    .Select(a => a.Address.ToString()).Distinct().ToArray();
                return addresses.Length == 0 ? "Connect your PC to Wi-Fi or Ethernet." : "PC address  " + String.Join("  /  ", addresses);
            }
            catch { return "Connect both devices to the same home network."; }
        }
        private void LoadSettings()
        {
            try
            {
                var xml = XElement.Load(Path.Combine(dataPath, "settings.xml"));
                nameBox.Text = (string)xml.Element("Name") ?? nameBox.Text;
                int quality; if (Int32.TryParse((string)xml.Element("Quality"), out quality) && quality >= 0 && quality <= 2) qualityBox.SelectedIndex = quality;
                audioBox.IsChecked = (string)xml.Element("Audio") != "false";
                pinBox.IsChecked = (string)xml.Element("Pin") != "false";
                connectionBox.SelectedIndex = (string)xml.Element("Connection") == "usb" ? 1 : 0;
            }
            catch { }
        }
        private void SaveSettings()
        {
            try { new XElement("PocketCast", new XElement("Name", nameBox.Text), new XElement("Quality", qualityBox.SelectedIndex),
                new XElement("Audio", audioBox.IsChecked == true ? "true" : "false"), new XElement("Pin", pinBox.IsChecked == true ? "true" : "false"), new XElement("Connection", UsbMode ? "usb" : "wifi"))
                .Save(Path.Combine(dataPath, "settings.xml")); } catch { }
        }
        private void SaveLog()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Text file (*.txt)|*.txt", FileName = "PocketCast-diagnostics.txt" };
            if (dialog.ShowDialog(this) == true)
            {
                try { File.WriteAllText(dialog.FileName, "Pocket Cast 1.1\r\n" + Environment.OSVersion + "\r\n" + networkText.Text + "\r\n\r\n" + logs); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save log"); }
            }
        }
        private void ShowUsbHelp()
        {
            MessageBox.Show(this,
                "USB TETHERING\nThis mode carries AirPlay screen and audio over an iPhone USB network connection.\n\n1. Install Apple Devices from Microsoft Store if Windows does not recognize your iPhone.\n2. Connect a USB cable that supports data. Unlock the phone, tap Trust, and enter its passcode on the phone.\n3. Enable Settings > Personal Hotspot > Allow Others to Join. Your cellular plan must support Personal Hotspot.\n4. Windows must show an active Apple Mobile Device Ethernet connection. Refresh the connection list here.\n5. Disconnect the iPhone from campus/home Wi-Fi while keeping Personal Hotspot active. Select USB tethering here, start the receiver, then use Control Center > Screen Mirroring on the iPhone. Keep the cable connected.\n\nARM PCs: Apple Devices may recognize the phone without providing a compatible USB Ethernet driver. USB mode cannot start without that network driver. This app does not install or replace drivers.\n\nPersonal Hotspot can share your cellular data connection with Windows; other PC traffic may use mobile data.\n\nMaximum quality and audio settings also apply to USB. AirPlay video remains compressed. Live USB mirroring has not been verified on this device.",
                "Pocket Cast - USB setup", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "CONNECT\n1. Put your iPhone and PC on the same home Wi-Fi/network. Your PC may use Ethernet.\n2. Start the receiver.\n3. On iPhone: Control Center > Screen Mirroring > your receiver name. Enter the PIN if asked.\n\nFULL QUALITY\nMaximum quality requests up to 3840 x 2160 at 60 fps and enables HEVC/H.265. iPhone, iOS, content, GPU and Wi-Fi determine the actual stream; AirPlay video is compressed. It does not guarantee native resolution, HDR, lossless video, or 120 fps. Try 1080p if 4K is unstable. Use a strong 5 GHz / 6 GHz Wi-Fi connection.\n\nNO PICTURE OR AUDIO?\nAllow the bundled runtime\\uxplay.exe through Windows Firewall on your private home network if prompted. Guest Wi-Fi, VPNs and network isolation can block discovery. Reconnect once if the first image freezes. Audio goes to the PC's default output; check its volume and the iPhone volume. Some protected streaming apps block mirroring. iPhone microphone/call audio is not guaranteed.\n\nThe iPhone video opens in its own resizable window. This app receives screen/audio; it does not remotely control your iPhone. Stop mirroring from your iPhone or press Stop receiver here.\n\nBuilt with the open-source UxPlay receiver. License and source details are included in the download.",
                "Pocket Cast - Connection help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string Xaml()
        {
            return @"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Grid.Resources>
 <Style TargetType='TextBlock'><Setter Property='Foreground' Value='#EDF3FC'/></Style>
 <Style TargetType='Button'><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='14'/><Setter Property='Foreground' Value='#EDF3FC'/><Setter Property='Background' Value='#253246'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='18,12'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='7' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='.84'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.35'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Opacity' Value='.75'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='CheckBox'><Setter Property='Foreground' Value='#D9E4F3'/><Setter Property='FontSize' Value='15'/><Setter Property='Margin' Value='0,0,0,12'/></Style>
 <Style TargetType='TextBox'><Setter Property='Background' Value='#0D141F'/><Setter Property='Foreground' Value='#EDF3FC'/><Setter Property='BorderBrush' Value='#38465D'/><Setter Property='CaretBrush' Value='#64DFC5'/><Setter Property='Padding' Value='12,10'/><Setter Property='FontSize' Value='15'/></Style>
</Grid.Resources>
<ScrollViewer VerticalScrollBarVisibility='Auto'>
<StackPanel Margin='36,28,36,28'>
 <Grid Margin='0,0,0,24'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
  <StackPanel Orientation='Horizontal'><Border Background='#64DFC5' CornerRadius='9' Width='42' Height='42' Margin='0,0,13,0'><TextBlock Text='P' FontWeight='Bold' FontSize='25' Foreground='#0C2826' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><StackPanel><TextBlock Text='Pocket Cast' FontWeight='SemiBold' FontSize='23'/><TextBlock Text='Your iPhone. A bigger screen.' FontSize='14' Foreground='#98A9C2'/></StackPanel></StackPanel>
  <Button x:Name='Help' Grid.Column='1' Content='Connection help' VerticalAlignment='Center'/>
 </Grid>
 <Border Background='#151F2D' BorderBrush='#2B3A50' BorderThickness='1' CornerRadius='14' Padding='25' Margin='0,0,0,22'>
 <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='250'/></Grid.ColumnDefinitions>
  <StackPanel><Border x:Name='StatusPill' BorderBrush='#99A8BF' BorderThickness='1' CornerRadius='12' Padding='11,4' HorizontalAlignment='Left' Margin='0,0,0,16'><TextBlock x:Name='Status' Text='Receiver is off' FontSize='14' Foreground='#99A8BF'/></Border>
   <TextBlock Text='Share every detail.' FontSize='30' FontWeight='SemiBold' Margin='0,0,15,10'/>
   <TextBlock x:Name='StatusDetail' Text='Start the receiver, then connect from your iPhone. Your screen and audio stay on your local network.' TextWrapping='Wrap' Foreground='#AFC0D7' LineHeight='24' Margin='0,0,25,18'/>
   <StackPanel Orientation='Horizontal'><Button x:Name='Start' Content='Start receiver' Background='#64DFC5' Foreground='#092925' FontWeight='SemiBold' Margin='0,0,10,0'/><Button x:Name='Stop' Content='Stop receiver' IsEnabled='False'/></StackPanel>
  </StackPanel>
  <Border Grid.Column='1' Background='#0D1521' CornerRadius='11' Padding='20' Margin='0,0,0,0' VerticalAlignment='Stretch'><StackPanel VerticalAlignment='Center'><TextBlock Text='SCREEN + AUDIO' FontSize='13' Foreground='#64DFC5' FontWeight='SemiBold' Margin='0,0,0,10'/><TextBlock Text='Up to 4K / 60 fps' FontSize='23' FontWeight='SemiBold'/><TextBlock Text='HEVC + H.264' Foreground='#91A5C2' Margin='0,7,0,12'/><TextBlock Text='Actual resolution and frame rate depend on your iPhone, content and network.' TextWrapping='Wrap' FontSize='14' Foreground='#91A5C2' LineHeight='20'/></StackPanel></Border>
 </Grid></Border>
 <StackPanel Margin='0,0,0,22'><TextBlock Text='Connection' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,8'/><ComboBox x:Name='Connection' SelectedIndex='0' FontSize='15' Padding='8' HorizontalAlignment='Left' Width='360' AutomationProperties.Name='Connection mode'><ComboBoxItem Content='Wi-Fi / Ethernet'/><ComboBoxItem Content='USB tethering - Personal Hotspot'/></ComboBox>
  <Border x:Name='UsbPanel' Visibility='Collapsed' Background='#151F2D' CornerRadius='9' Padding='17' Margin='0,12,0,0'><StackPanel><TextBlock Text='Connect a data cable and enable Personal Hotspot on iPhone.' FontWeight='SemiBold' Margin='0,0,0,7'/><TextBlock x:Name='UsbHint' TextWrapping='Wrap' Foreground='#AFC0D7' FontSize='14' Margin='0,0,0,10'/><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ComboBox x:Name='UsbAdapter' FontSize='14' Padding='8' AutomationProperties.Name='iPhone USB network connection'/><Button x:Name='RefreshUsb' Grid.Column='1' Content='Refresh' Margin='8,0,0,0'/><Button x:Name='UsbSetup' Grid.Column='2' Content='USB setup' Margin='8,0,0,0'/></Grid><TextBlock Text='Personal Hotspot may let other PC traffic use your cellular data plan.' Foreground='#91A5C2' FontSize='13' TextWrapping='Wrap' Margin='0,10,0,0'/></StackPanel></Border>
 </StackPanel>
 <Grid Margin='0,0,0,24'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='30'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
  <StackPanel><TextBlock Text='Receiver settings' FontSize='18' FontWeight='SemiBold' Margin='0,0,0,14'/><TextBlock Text='Name shown on your iPhone' Foreground='#AFC0D7' FontSize='14' Margin='0,0,0,7'/>
   <Grid Margin='0,0,0,13'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='ReceiverName' MaxLength='48' AutomationProperties.Name='Receiver name'/><Button x:Name='CopyName' Content='Copy' Grid.Column='1' Margin='8,0,0,0'/></Grid>
   <TextBlock Text='Screen quality' Foreground='#AFC0D7' FontSize='14' Margin='0,0,0,7'/><ComboBox x:Name='Quality' SelectedIndex='0' FontSize='15' Padding='8' Margin='0,0,0,8' AutomationProperties.Name='Screen quality'><ComboBoxItem Content='Maximum - up to 4K, HEVC'/><ComboBoxItem Content='High - up to 1080p, H.264'/><ComboBoxItem Content='Smooth - up to 720p, H.264'/></ComboBox>
   <TextBlock Text='All modes request up to 60 fps. Stop to change settings.' Foreground='#91A5C2' FontSize='13' TextWrapping='Wrap' Margin='0,0,0,15'/>
   <CheckBox x:Name='Audio' IsChecked='True' Content='Play iPhone audio on this PC'/><CheckBox x:Name='RequirePin' IsChecked='True' Content='Require a connection PIN'/><TextBlock x:Name='Pin' Text='PIN appears when you start' Foreground='#64DFC5' FontSize='15'/>
  </StackPanel>
  <StackPanel Grid.Column='2'><TextBlock Text='Connect in a moment' FontSize='18' FontWeight='SemiBold' Margin='0,0,0,14'/>
   <TextBlock x:Name='StepOneTitle' Text='1   Use the same network' FontWeight='SemiBold' Margin='0,0,0,5'/><TextBlock x:Name='StepOneDetail' Text='Connect your iPhone and PC to the same home Wi-Fi. Ethernet works for your PC too.' TextWrapping='Wrap' Foreground='#98ABC7' FontSize='14' LineHeight='21' Margin='23,0,0,15'/>
   <TextBlock Text='2   Open Screen Mirroring' FontWeight='SemiBold' Margin='0,0,0,5'/><TextBlock x:Name='StepTwoDetail' Text='On iPhone, open Control Center and tap the two overlapping rectangles.' TextWrapping='Wrap' Foreground='#98ABC7' FontSize='14' LineHeight='21' Margin='23,0,0,15'/>
   <TextBlock Text='3   Choose your PC' FontWeight='SemiBold' Margin='0,0,0,5'/><TextBlock Text='Select the receiver name above. Enter the PIN if asked. Your picture opens in a separate video window.' TextWrapping='Wrap' Foreground='#98ABC7' FontSize='14' LineHeight='21' Margin='23,0,0,0'/>
  </StackPanel>
 </Grid>
 <Border BorderBrush='#28374C' BorderThickness='0,1,0,0' Padding='0,15,0,0'><StackPanel><TextBlock x:Name='Network' Foreground='#8FA4C1' FontSize='13' Margin='0,0,0,10'/><Expander Header='Receiver diagnostics' Foreground='#B1C2DA' FontSize='14'><StackPanel><TextBox x:Name='Log' IsReadOnly='True' FontFamily='Consolas' FontSize='12' Height='150' TextWrapping='Wrap' VerticalScrollBarVisibility='Auto' Margin='0,10,0,10' AutomationProperties.Name='Diagnostic log'/><Button x:Name='SaveLog' Content='Save diagnostic log' HorizontalAlignment='Left'/></StackPanel></Expander><TextBlock Text='AirPlay compresses video. Maximum quality is a request, not a lossless or native-resolution guarantee.' Foreground='#8FA4C1' FontSize='13' TextWrapping='Wrap' Margin='0,13,0,0'/></StackPanel></Border>
</StackPanel></ScrollViewer></Grid>";
        }
    }
}

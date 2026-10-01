using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;

namespace WindowsGSM.Plugins
{
    public class TF2_VR : SteamCMDAgent
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.TF2_VR",
            author = "Raziel7893",
            description = "WindowsGSM plugin for supporting TF2 VR Mod Dedicated Server",
            version = "1.0",
            url = "https://github.com/Raziel7893/WindowsGSM.TF2_64bit",
            color = "#34FFeb"
        };
        // - Plugin Details
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 0;
        public dynamic QueryMethod = new GameServer.Query.A2S();
        public override bool loginAnonymous => true;
        // - Game server default values
        public string Port = "27015";
        public string QueryPort = "27015";
        public string Maxplayers = "32";

        public string FullName = "Team Fortress 2 VR MOD Dedicated Server";
        public string Defaultmap { get { return "ctf_2fort"; } }
        public string Game { get { return "tfvr"; } }
        public override string AppId { get { return "4957840"; } }
        public string Additional { get { return "-tickrate 64 +sv_lan 0 +sv_hibernate_when_empty 0 +log on +heartbeat"; } }

        public override string StartPath => "srcds_win64.exe";


        public TF2_VR(Functions.ServerConfig serverData) : base(serverData)
        {
            base.serverData = serverData;
        }

        public async Task<Process> Start()
        {
            string srcdsPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, StartPath);
            if (!File.Exists(srcdsPath))
            {
                Error = $"{StartPath} not found ({srcdsPath})";
                return null;
            }

            string configPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, Game, "cfg/server.cfg");
            if (!File.Exists(configPath))
            {
                Notice = $"server.cfg not found ({configPath})";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"-console -condebug");
            sb.Append(string.IsNullOrWhiteSpace(Game) ? string.Empty : $" -game {Game}");
            sb.Append(string.IsNullOrWhiteSpace(serverData.ServerIP) ? string.Empty : $" -ip {serverData.ServerIP}");
            sb.Append(string.IsNullOrWhiteSpace(serverData.ServerPort) ? string.Empty : $" -secure -port {serverData.ServerPort}");
            sb.Append(string.IsNullOrWhiteSpace(serverData.ServerMaxPlayer) ? string.Empty : $" -maxplayers{(AppId == "740" ? "_override" : "")} {serverData.ServerMaxPlayer}");
            sb.Append(string.IsNullOrWhiteSpace(serverData.ServerGSLT) ? string.Empty : $" +sv_setsteamaccount {serverData.ServerGSLT}");
            sb.Append(string.IsNullOrWhiteSpace(serverData.ServerParam) ? string.Empty : $" {serverData.ServerParam}");
            sb.Append(string.IsNullOrWhiteSpace(serverData.ServerMap) ? string.Empty : $" +map {serverData.ServerMap}");
            string param = sb.ToString();

            Process p;
            if (!AllowsEmbedConsole)
            {
                p = new Process
                {
                    StartInfo =
                    {
                        FileName = srcdsPath,
                        Arguments = param,
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false,
                    },
                    EnableRaisingEvents = true
                };
                p.Start();
            }
            else
            {
                p = new Process
                {
                    StartInfo =
                    {
                        FileName = srcdsPath,
                        Arguments = param,
                        WindowStyle = ProcessWindowStyle.Minimized,
                        UseShellExecute = false,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                var serverConsole = new Functions.ServerConsole(serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            return p;
        }

        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                Functions.ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "quit");
            });
        }

        public async void CreateServerCFG()
        {
            //Download server.cfg
            string configPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, Game, "cfg/server.cfg");
            if (await Functions.Github.DownloadGameServerConfig(configPath, serverData.ServerGame))
            {
                string configText = File.ReadAllText(configPath);
                configText = configText.Replace("{{hostname}}", serverData.ServerName);
                configText = configText.Replace("{{rcon_password}}", serverData.GetRCONPassword());
                File.WriteAllText(configPath, configText);
            }

            //Edit WindowsGSM.cfg
            string configFile = Functions.ServerPath.GetServersConfigs(serverData.ServerID, "WindowsGSM.cfg");
            if (File.Exists(configFile))
            {
                string configText = File.ReadAllText(configFile);
                configText = configText.Replace("{{clientport}}", (int.Parse(serverData.ServerPort) - 10).ToString());
                File.WriteAllText(configFile, configText);
            }
        }

        public bool IsInstallValid()
        {
            string installPath = Functions.ServerPath.GetServersServerFiles(serverData.ServerID, StartPath);
            Error = $"Fail to find {installPath}";
            return File.Exists(installPath);
        }

        public bool IsImportValid(string path)
        {
            string importPath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {Path.GetFileName(StartPath)}";
            return File.Exists(importPath);
        }

        public async Task<Process> Install()
        {
            var steamCMD = new Installer.SteamCMD();
            string combinedAppId = $"{AppId} validate +app_update 232250 validate +app_update 244310";
            Process p = await steamCMD.Install(serverData.ServerID, string.Empty, combinedAppId, true, loginAnonymous);
            Error = steamCMD.Error;

            return p;
        }
        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            string validateString = validate ? "validate" : "";
            string combinedAppId = $"{AppId} {validateString} +app_update 232250 {validateString} +app_update 244310";
            //we always validate. it is important that the bin\x64\engine.dll is always used from the SDK (244310). if we pass a fixed validate=true to the installer, it should always restore that engine instead of using the TF2 one
            var (p, error) = await Installer.SteamCMD.UpdateEx(serverData.ServerID, combinedAppId, true, custom: custom, loginAnonymous: loginAnonymous);

            await Task.Run(() => { p.WaitForExit(); });
            Error = error;
            return p;
        }
    }
}
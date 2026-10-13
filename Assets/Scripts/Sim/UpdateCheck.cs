using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FrcSim
{
    // 啟動時在背景檢查 GitHub 上有沒有更新的 Release;有新版時主畫面副標題提示,按 U 就「像遊戲一樣」自動更新:
    //   只下載「差異包」(delta-vX.zip,只含改過的檔案,通常幾 MB;場地/機器人模型這類大檔不會重載)→ 校驗 → 關閉程式 →
    //   背景腳本把檔案覆蓋到安裝資料夾 → 自動重新開啟。中間跳過的版本會依序套用。
    //   找不到差異包(或失敗)時退回「開啟下載頁」,讓使用者手動下載完整版。
    public class UpdateCheck : MonoBehaviour
    {
        public const string Current = "0.5.77";
        const string Repo = "frc9427liu-tech/FRC9427-SimApp";
        const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases?per_page=30";
        public const string Page = "https://github.com/" + Repo + "/releases/latest";

        public static volatile string Latest = "";
        public static volatile bool Newer;
        public static volatile bool ChainOk;          // 每個中間版本都有差異包 → 可自動更新
        public static volatile string Status = "not checked";
        public static volatile string Progress = "";  // 更新進行中的文字(給選單顯示)
        public static volatile bool Busy, ReadyToQuit, OpenPageFlag;

        class Step { public Version V; public string Tag, Url; }
        static readonly List<Step> chain = new List<Step>();

        static string installDir, dataPath;   // 主執行緒才能讀 Application.dataPath,所以啟動時先存起來
        static string InstallDir => installDir;

        void Start()
        {
            dataPath = Application.dataPath; installDir = Path.GetDirectoryName(dataPath);
            try
            {   // 上次更新套用失敗 → apply.ps1 已還原舊版並留下訊息:這次啟動告訴使用者
                string ef = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FRC9427-Sim", "update-error.txt");
                if (File.Exists(ef)) { Progress = (Loc.Lang == "en" ? "Last update failed and was rolled back: " : "上次更新失敗,已還原到舊版:") + File.ReadAllText(ef).Trim(); File.Delete(ef); }
            }
            catch (Exception) { }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") >= 0) return;   // 自動測試不連網
            if (SuperSample.IsTestRun() && Array.IndexOf(Environment.GetCommandLineArgs(), "-autoupdate") < 0) return;   // 測試啟動不查更新:否則大量測試會用光 GitHub API 每小時 60 次額度,使用者就收不到更新通知
            new Thread(Check) { IsBackground = true }.Start();
        }

        void Update()
        {
            if (ReadyToQuit) { ReadyToQuit = false; Application.Quit(); }
            if (OpenPageFlag) { OpenPageFlag = false; Application.OpenURL(Page); }
        }

        static WebClientEx NewClient()
        {
            var c = new WebClientEx();
            c.Headers.Add("User-Agent", "FRC9427-Sim/" + Current);
            return c;
        }

        class WebClientEx : System.Net.WebClient
        {
            protected override System.Net.WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                if (r != null) r.Timeout = 30000;
                return r;
            }
        }

        // 備援:github.com/<repo>/releases/latest 會 302 轉到 /releases/tag/vX.Y.Z,從 Location 取版本號(不算 API 額度)
        static void CheckViaRedirect()
        {
            try
            {
                var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(Page);
                rq.AllowAutoRedirect = false; rq.UserAgent = "FRC9427-Sim/" + Current; rq.Timeout = 20000;
                using (var rs = (System.Net.HttpWebResponse)rq.GetResponse())
                {
                    string loc = rs.Headers["Location"] ?? "";
                    var m = Regex.Match(loc, @"/tag/v?([0-9]+(?:\.[0-9]+){1,3})");
                    if (!m.Success) return;
                    var latest = new Version(m.Groups[1].Value); var cur = new Version(Current);
                    Latest = latest.ToString(); Newer = latest > cur; ChainOk = false;   // 沒有差異包清單:按 U 會開下載頁
                    Status = Newer ? "update available (redirect)" : "up to date (redirect)";
                }
            }
            catch (Exception e) { Status = Status + " / redirect failed: " + e.Message; }
        }

        static void Check()
        {
            try
            {
                string json;
                using (var c = NewClient()) json = c.DownloadString(ReleasesApi);
                var arr = JArray.Parse(json);
                var cur = new Version(Current);
                var found = new List<Step>();
                Version latest = cur;
                foreach (var r in arr)
                {
                    if ((bool?)r["draft"] == true || (bool?)r["prerelease"] == true) continue;
                    var m = Regex.Match((string)r["tag_name"] ?? "", @"^v?([0-9]+(?:\.[0-9]+){1,3})$");
                    if (!m.Success) continue;
                    var v = new Version(m.Groups[1].Value);
                    if (v <= cur) continue;
                    if (v > latest) latest = v;
                    string url = null;
                    foreach (var a in (JArray)r["assets"] ?? new JArray())
                        if ((string)a["name"] == "delta-v" + m.Groups[1].Value + ".zip") url = (string)a["browser_download_url"];
                    found.Add(new Step { V = v, Tag = m.Groups[1].Value, Url = url });
                }
                found.Sort((a, b) => a.V.CompareTo(b.V));
                chain.Clear(); chain.AddRange(found);
                Latest = latest.ToString();
                Newer = latest > cur;
                ChainOk = Newer && found.Count > 0 && found.TrueForAll(s => s.Url != null);
                if (arr.Count >= 30 && found.Count >= arr.Count - 1) ChainOk = false;   // 落後太多:API 只回最近 30 筆,中間的版本可能不在清單裡 → 不走鏈式更新,改開下載頁(避免漏檔)
                Status = Newer ? "update available" : "up to date";
                if (Newer && Array.IndexOf(Environment.GetCommandLineArgs(), "-autoupdate") >= 0) StartUpdate();   // 測試用:發現新版就直接更新
            }
            catch (Exception e) { Status = "check failed: " + e.Message; CheckViaRedirect(); }   // API 被限流/失敗:退回只看「最新版」頁面的轉址(不吃 API 額度),至少能提示有新版
        }

        // 按 U:能自動更新就自動更新,否則開下載頁
        public static void StartUpdate()
        {
            if (!Newer || Busy) return;
            if (!ChainOk) { Application.OpenURL(Page); return; }
            Busy = true;
            new Thread(DoUpdate) { IsBackground = true }.Start();
        }

        static void DoUpdate()
        {
            try
            {
                // 暫存放在 LocalAppData(不在安裝資料夾):安裝在 OneDrive 桌面時,那裡的資料夾是唯讀/雲端檔案,舊的殘留刪不掉會讓更新一直失敗
                string work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FRC9427-Sim", "update");
                string stage = Path.Combine(work, "staged");
                SafeDelete(work);
                if (Directory.Exists(work)) { work = work + "-" + DateTime.Now.Ticks; stage = Path.Combine(work, "staged"); }
                Directory.CreateDirectory(stage);

                var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // 相對路徑 → sha256(後面的版本覆蓋前面的)
                var deletes = new List<string>();
                int n = 0;
                foreach (var s in chain)
                {
                    n++;
                    Progress = Loc.Lang == "en" ? $"Downloading update v{s.Tag} ({n}/{chain.Count})…" : $"下載更新 v{s.Tag}({n}/{chain.Count})…";
                    string zipPath = Path.Combine(work, "delta-v" + s.Tag + ".zip");
                    using (var c = NewClient()) c.DownloadFile(s.Url, zipPath);

                    Progress = Loc.Lang == "en" ? $"Unpacking & verifying v{s.Tag}…" : $"解壓縮並校驗 v{s.Tag}…";
                    using (var za = ZipFile_Open(zipPath))
                    {
                        JObject man = null;
                        foreach (var e in za.Entries)
                            if (e.FullName == "_manifest.json") using (var rd = new StreamReader(e.Open(), Encoding.UTF8)) man = JObject.Parse(rd.ReadToEnd());
                        if (man == null) throw new Exception("差異包缺少 _manifest.json");
                        foreach (var e in za.Entries)
                        {
                            if (e.FullName == "_manifest.json") continue;
                            if (e.FullName == "_delete.txt")
                            {
                                using (var rd = new StreamReader(e.Open(), Encoding.UTF8))
                                    foreach (var line in rd.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) { string dl = line.Trim(); if (!SafeRel(dl.Replace('/', Path.DirectorySeparatorChar), InstallDir)) throw new Exception("不安全的刪除路徑 " + dl); deletes.Add(dl); }
                                continue;
                            }
                            if (e.FullName.EndsWith("/")) continue;
                            string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                            if (!SafeRel(rel, stage)) throw new Exception("不安全的路徑 " + e.FullName);
                            string dest = Path.Combine(stage, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            using (var src = e.Open()) using (var dst = File.Create(dest)) src.CopyTo(dst);
                        }
                        foreach (var f in (JArray)man["files"] ?? new JArray())
                            expected[((string)f["p"]).Replace('/', Path.DirectorySeparatorChar)] = ((string)f["h"]).ToLowerInvariant();
                    }
                }

                Progress = Loc.Lang == "en" ? "Verifying files…" : "校驗檔案…";
                foreach (var kv in expected)
                {
                    string p = Path.Combine(stage, kv.Key);
                    if (!File.Exists(p)) continue;   // 這個版本沒有改到它(只在 manifest 內作為完整檔案清單)
                    using (var fs = File.OpenRead(p))
                    {
                        string h = BitConverter.ToString(SHA256.Create().ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                        if (h != kv.Value) throw new Exception("校驗失敗:" + kv.Key);
                    }
                }

                File.WriteAllText(Path.Combine(work, "_delete.txt"), string.Join("\n", deletes.ToArray()), new UTF8Encoding(false));
                string ps1 = Path.Combine(work, "apply.ps1");
                File.WriteAllText(ps1, ApplyScript, new UTF8Encoding(true));
                string exe = Path.Combine(InstallDir, Path.GetFileNameWithoutExtension(dataPath.Replace("_Data", "")) + ".exe");
                if (!File.Exists(exe))
                {
                    // 保險:找安裝資料夾裡的 exe
                    foreach (var f in Directory.GetFiles(InstallDir, "*.exe")) if (!f.ToLowerInvariant().Contains("crash")) { exe = f; break; }
                }
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\" -ProcId {pid} -Src \"{stage}\" -Dst \"{InstallDir}\" -Exe \"{exe}\" -Work \"{work}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                System.Diagnostics.Process.Start(psi);
                Progress = Loc.Lang == "en" ? "Update done, restarting…" : "更新完成,重新啟動中…";
                ReadyToQuit = true;
            }
            catch (Exception e)
            {
                Progress = Loc.Lang == "en" ? "Auto-update failed (" + e.Message + "). Please download manually." : "自動更新失敗(" + e.Message + "),請手動下載";
                Debug.LogWarning("[UpdateCheck] " + e);
                Busy = false;
                OpenPageFlag = true;
            }
        }

        // 刪資料夾:先清唯讀屬性,不行就用 cmd rd(OneDrive 資料夾 Directory.Delete 常常 Access denied)
        static void SafeDelete(string dir)
        {
            if (!Directory.Exists(dir)) return;
            try { foreach (var f in Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories)) { try { File.SetAttributes(f, FileAttributes.Normal); } catch { } } Directory.Delete(dir, true); }
            catch
            {
                try
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c rd /s /q \"" + dir + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    p.WaitForExit(15000);
                }
                catch { }
            }
        }

        static ZipArchive ZipFile_Open(string path) => new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read), ZipArchiveMode.Read);

        // 套用腳本(v2):覆蓋前先備份「會被蓋掉的舊檔」;robocopy 失敗就還原備份、寫 update-error.txt(下次啟動提示)、仍啟動舊版;成功才刪暫存。
        // 注意:這段是 C# 逐字字串,腳本裡只能用單引號(雙引號要寫成兩個)。
        const string ApplyScript = @"param($ProcId,$Src,$Dst,$Exe,$Work)
try { Wait-Process -Id $ProcId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Seconds 1
$errFile = Join-Path (Split-Path $Work -Parent) 'update-error.txt'
$bak = Join-Path $Work 'backup'
New-Item -ItemType Directory -Force $bak | Out-Null
$srcFull = (Resolve-Path $Src).Path.TrimEnd('\')
$added = @()
Get-ChildItem $Src -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($srcFull.Length + 1)
    $t = Join-Path $Dst $rel
    if (Test-Path $t) { $b = Join-Path $bak $rel; New-Item -ItemType Directory -Force (Split-Path $b -Parent) | Out-Null; try { Copy-Item $t $b -Force } catch {} } else { $added += $t }
}
$ok = $false
for ($i = 0; $i -lt 5 -and -not $ok; $i++) {
    robocopy $Src $Dst /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -lt 8) { $ok = $true } else { Start-Sleep -Seconds 2 }
}
if (-not $ok) {
    robocopy $bak $Dst /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    foreach ($f in $added) { try { if (Test-Path $f) { [System.IO.File]::Delete($f) } } catch {} }
    'file copy failed (locked by antivirus or another program?); rolled back to the previous version.' | Set-Content $errFile -Encoding UTF8
    try { [System.IO.Directory]::Delete($Work, $true) } catch {}
    Start-Process -FilePath $Exe -WorkingDirectory $Dst
    exit 1
}
$del = Join-Path $Work '_delete.txt'
if (Test-Path $del) {
    foreach ($line in (Get-Content $del)) {
        if ($line -and -not $line.Contains('..') -and -not [System.IO.Path]::IsPathRooted($line) -and -not $line.Contains(':')) {
            $t = Join-Path $Dst $line
            try { if (Test-Path $t) { [System.IO.File]::Delete($t) } } catch {}
        }
    }
}
if (Test-Path $errFile) { try { Remove-Item $errFile -Force } catch {} }
try { [System.IO.Directory]::Delete($Work, $true) } catch {}
Start-Process -FilePath $Exe -WorkingDirectory $Dst
";

        // 解壓路徑安全檢查:只允許安裝資料夾內的相對路徑(擋 .. / 絕對路徑 / 磁碟機代號 / NTFS 資料流 / 開頭分隔符)
        static bool SafeRel(string rel, string root)
        {
            if (string.IsNullOrEmpty(rel) || rel.Contains("..") || rel.Contains(":") || Path.IsPathRooted(rel) || rel.StartsWith("\\") || rel.StartsWith("/")) return false;
            string full = Path.GetFullPath(Path.Combine(root, rel)), r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
    }
}
























































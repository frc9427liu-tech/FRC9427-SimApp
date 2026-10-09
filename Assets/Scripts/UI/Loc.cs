using System.Collections.Generic;
using UnityEngine;

namespace FrcSim
{
    // 簡易多語言:Loc.T("key")。語言存在 PlayerPrefs("lang"):"zh" 或 "en"
    public static class Loc
    {
        public static string Lang
        {
            get => PlayerPrefs.GetString("lang", "zh");
            set { PlayerPrefs.SetString("lang", value); PlayerPrefs.Save(); }
        }
        public static bool HasChosen => PlayerPrefs.HasKey("lang");

        static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>
        {
            // key           = { 中文, English }
            { "app.title",     new[]{ "FRC 9427 模擬器", "FRC 9427 SIMULATOR" } },
            { "app.sub",       new[]{ "機器人模擬訓練平台", "Robot Training Platform" } },
            { "menu.start",    new[]{ "開始遊戲", "Start" } },
            { "menu.settings", new[]{ "設定", "Settings" } },
            { "menu.controls", new[]{ "操作說明", "Controls" } },
            { "menu.quit",     new[]{ "離開", "Quit" } },
            { "menu.back",     new[]{ "返回", "Back" } },
            { "mode.title",    new[]{ "選擇模式", "Select Mode" } },
            { "mode.free",     new[]{ "自由練習", "Free Practice" } },
            { "mode.match",    new[]{ "比賽模式(即將推出)", "Match Mode (coming soon)" } },
            { "mode.auto",     new[]{ "自動階段練習(即將推出)", "Autonomous Practice (coming soon)" } },
            { "set.title",     new[]{ "設定", "Settings" } },
            { "set.lang",      new[]{ "語言", "Language" } },
            { "set.fps",       new[]{ "FPS 上限", "FPS limit" } },
            { "set.vsync",     new[]{ "垂直同步", "VSync" } },
            { "set.full",      new[]{ "全螢幕", "Fullscreen" } },
            { "set.model",     new[]{ "機器人模型", "Robot model" } },
            { "model.builtin", new[]{ "內建方塊", "Built-in blocks" } },
            { "setup.title",   new[]{ "機器人設定", "Robot Setup" } },
            { "setup.sub",     new[]{ "進入遊戲前先選好", "Choose before you start" } },
            { "setup.import",  new[]{ "匯入模型檔 (.glb)…", "Import model (.glb)…" } },
            { "setup.yaw",     new[]{ "模型方向", "Model heading" } },
            { "setup.code",    new[]{ "機器人程式", "Robot code" } },
            { "setup.none",    new[]{ "自動偵測(點選可改)", "auto (click to change)" } },
            { "setup.second",  new[]{ "第二台機器人(紅方)", "Second robot (red)" } },
            { "setup.clock",   new[]{ "比賽計時與 HUB 輪替(官方 160 秒)", "Match clock & HUB shifts (official 160 s)" } },
            { "setup.real",   new[]{ "執行真實機器人程式(實驗中)", "Run real robot code (experimental)" } },
            { "setup.ctl",     new[]{ "操控方式", "Drive style" } },
            { "ctl.swerve",    new[]{ "全向(左搖桿移動)", "Swerve (left stick)" } },
            { "ctl.tank",      new[]{ "LEO 坦克(左右搖桿)", "LEO tank (two sticks)" } },
            { "setup.start",   new[]{ "開始", "Start" } },
            { "set.unlimited", new[]{ "不限", "Unlimited" } },
            { "on",            new[]{ "開", "On" } },
            { "off",           new[]{ "關", "Off" } },
            { "lang.name",     new[]{ "繁體中文", "English" } },
            { "lang.pick",     new[]{ "選擇語言 / Select Language", "選擇語言 / Select Language" } },
            { "ctl.title",     new[]{ "操作說明", "Controls" } },
            { "ctl.kb",        new[]{ "鍵盤", "Keyboard" } },
            { "ctl.pad",       new[]{ "Xbox 手把", "Xbox Controller" } },
            { "ctl.kb.body",   new[]{
                "W A S D    移動\nQ / E    逆時針 / 順時針旋轉\nShift    慢速\nI    放下 / 收起 Intake\n空白鍵 / 滑鼠左鍵    發射(按住,自動瞄準 HUB)\nF    場地座標 / 車頭座標\nC    切換視角\nR    重置位置\nEsc    暫停選單",
                "W A S D    Move\nQ / E    Rotate CCW / CW\nShift    Slow\nI    Intake down / up\nSpace / Left Click    Shoot (hold, auto-aim HUB)\nF    Field / Robot centric\nC    Change camera\nR    Reset pose\nEsc    Pause menu" } },
            { "ctl.pad.body",  new[]{
                "左搖桿    移動\nLB / RB    逆時針 / 順時針旋轉\nA    放下 / 收起 Intake\nB    發射(按住)\nStart    重置位置\n【LEO 坦克模式】左/右搖桿    左/右側輪\nA(按住)    放下 Intake\nRT    發射",
                "Left stick    Move\nLB / RB    Rotate CCW / CW\nA    Intake down / up\nB    Shoot (hold)\nStart    Reset pose\n[LEO tank] Left/Right stick    left/right side\nA (hold)    Intake down\nRT    Shoot" } },
            { "pause.title",   new[]{ "暫停", "Paused" } },
            { "pause.resume",  new[]{ "繼續", "Resume" } },
            { "pause.reset",   new[]{ "重置位置", "Reset Pose" } },
            { "pause.menu",    new[]{ "回到主畫面", "Main Menu" } },
            { "hint.nav",      new[]{ "↑↓ 選擇    Enter / A 確認    Esc / B 返回", "↑↓ Select    Enter / A Confirm    Esc / B Back" } },
            { "splash.made",   new[]{ "MADE IN UNITY", "MADE IN UNITY" } },
        };

        public static string T(string key)
        {
            if (!D.TryGetValue(key, out var v)) return key;
            return v[Lang == "en" ? 1 : 0];
        }
    }
}

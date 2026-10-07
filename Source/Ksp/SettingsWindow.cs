using System;
using UnityEngine;
using KSP.UI.Screens;
using VolumetricExplosionFX.Core;
using VolumetricExplosionFX.Rendering;
namespace VolumetricExplosionFX.Ksp
{
    [KSPAddon(KSPAddon.Startup.Flight,false)] public sealed class SettingsWindowFlight : SettingsWindow { }
    [KSPAddon(KSPAddon.Startup.SpaceCentre,false)] public sealed class SettingsWindowSpaceCenter : SettingsWindow { }
    public abstract class SettingsWindow : MonoBehaviour
    {
        const string LockId="VolumetricExplosionFX_settings";
        ApplicationLauncherButton button;
        Texture2D icon;
        SettingsPanel panel;
        UiText text;
        Font fallbackFont;
        bool locked, dirty, saveFailed; float saveAt, savedAt=-100, testedAt=-100;
        string testNote;
        static int lastPage;
        protected void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnUnreadifying);
            if(ApplicationLauncher.Ready) AddButton();
        }
        void AddButton()
        {
            try
            {
                if(button!=null||ApplicationLauncher.Instance==null) return;
                if(icon==null) icon=UiArt.Icon(64);
                button=ApplicationLauncher.Instance.AddModApplication(Open,Hide,null,null,null,null,
                    ApplicationLauncher.AppScenes.FLIGHT|ApplicationLauncher.AppScenes.MAPVIEW|ApplicationLauncher.AppScenes.SPACECENTER,icon);
            }
            catch(Exception ex) { Debug.LogWarning("[VEFX] Settings button: "+ex.GetType().Name+": "+ex.Message); }
        }
        void OnUnreadifying(GameScenes scene) { Hide(); RemoveButton(); }
        void RemoveButton()
        {
            if(button!=null&&ApplicationLauncher.Instance!=null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button=null;
        }
        void Open()
        {
            try
            {
                SyncLanguage();
                if(panel==null) panel=new SettingsPanel(Model(),Font(),null,Scale());
                panel.Page=lastPage; panel.Visible=true;
            }
            catch(Exception ex) { Debug.LogError("[VEFX] Settings window: "+ex); }
        }
        void Hide()
        {
            if(panel!=null) panel.Visible=false;
            if(dirty) SaveNow();
            Unlock();
        }
        void CloseFromWindow() { if(button!=null) button.SetFalse(true); else Hide(); }
        void SyncLanguage()
        {
            UiText selected=GameText.Current;
            if(ReferenceEquals(text,selected)) return;
            text=selected;
            if(panel==null) return;
            bool visible=panel.Visible; lastPage=panel.Page;
            panel.Dispose(); panel=null; Unlock();
            panel=new SettingsPanel(Model(),Font(),null,Scale());
            panel.Page=lastPage; panel.Visible=visible;
        }
        UiModel Model()
        {
            FxSettings s=SettingsStore.Settings;
            return SettingsModel.Build(s,new SettingsModel.Hooks {
                Version=FlightController.Version,
                Text=text,
                Changed=()=>{ SettingsStore.Touch(); dirty=true; saveFailed=false; saveAt=Time.unscaledTime+1; },
                Close=CloseFromWindow,
                Reset=()=>{ SettingsStore.Reset(); dirty=true; saveFailed=false; saveAt=Time.unscaledTime+0.2f; },
                CanTest=()=>FlightController.CanTest,
                Test=kind=>{ bool ok=FlightController.Test(kind); testedAt=Time.unscaledTime;
                    testNote=ok?"PreviewStarted":"PreviewUnavailable"; return ok; },
                Status=Status });
        }
        string Status()
        {
            float now=Time.unscaledTime;
            if(now-testedAt<3&&testNote!=null) return testNote=="PreviewStarted"?
                text.Get("PreviewStarted","Preview started"):text.Get("PreviewUnavailable","Preview unavailable here");
            if(saveFailed) return text.Get("SaveFailed","Settings applied; unable to save");
            if(dirty) return text.Get("Saving","Saving…");
            if(now-savedAt<3) return text.Get("Saved","Settings saved");
            return text.Get("NextEffects","Applied to new effects");
        }
        Font Font()
        {
            Font f=HighLogic.Skin!=null?HighLogic.Skin.font:null;
            if(f==null) try { f=UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch(Exception) { }
            if(f==null) f=GUI.skin!=null?GUI.skin.font:null;
            string sample=text.Get("General","General")+text.Get("Enabled","Enable effects");
            if(HasGlyphs(f,sample)) return f;
            try
            {
                if(fallbackFont==null) fallbackFont=UnityEngine.Font.CreateDynamicFontFromOSFont(new[]{
                    "Microsoft YaHei","Meiryo","Yu Gothic","PingFang SC","Hiragino Sans",
                    "Noto Sans CJK SC","Noto Sans CJK JP","Arial Unicode MS","DejaVu Sans","Arial"},14);
                if(HasGlyphs(fallbackFont,sample)) return fallbackFont;
            }
            catch(Exception) { }
            return f;
        }
        static bool HasGlyphs(Font font,string sample)
        {
            if(font==null) return false;
            if(font.dynamic) font.RequestCharactersInTexture(sample,14,FontStyle.Normal);
            foreach(char c in sample) if(!char.IsWhiteSpace(c)&&!font.HasCharacter(c)) return false;
            return true;
        }
        static float Scale() { return Mathf.Clamp(GameSettings.UI_SCALE,0.5f,2.5f); }
        protected void Update()
        {
            if(panel==null) return;
            try
            {
                SyncLanguage();
                if(panel==null) return;
                if(panel.Visible&&Input.GetKeyDown(KeyCode.Escape)) { CloseFromWindow(); return; }
                panel.SetScale(Scale());
                panel.Tick(Time.unscaledDeltaTime);
                lastPage=panel.Page;
                bool over=panel.Contains(Input.mousePosition);
                if(!over&&Input.GetMouseButtonDown(0)) panel.ReleaseFocus();
                bool interacting=over||panel.HasKeyboardFocus;
                if(interacting&&!locked) { InputLockManager.SetControlLock(ControlTypes.ALL_SHIP_CONTROLS|ControlTypes.CAMERACONTROLS|ControlTypes.KSC_FACILITIES,LockId); locked=true; }
                else if(!interacting&&locked) Unlock();
                if(dirty&&!saveFailed&&Time.unscaledTime>=saveAt) SaveNow();
            }
            catch(Exception ex) { Debug.LogError("[VEFX] Settings window: "+ex); if(panel!=null) { panel.ReleaseFocus(); panel.Visible=false; } Unlock(); }
        }
        void SaveNow()
        {
            if(SettingsStore.Save()) { dirty=false; saveFailed=false; savedAt=Time.unscaledTime; }
            else saveFailed=true; // Keep changes dirty; a new change or closing the window retries.
        }
        void Unlock() { if(locked) { InputLockManager.RemoveControlLock(LockId); locked=false; } }
        protected void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnUnreadifying);
            RemoveButton();
            if(dirty) SaveNow();
            Unlock();
            if(panel!=null) { panel.Dispose(); panel=null; }
            if(fallbackFont!=null) { Destroy(fallbackFont); fallbackFont=null; }
            if(icon!=null) { Destroy(icon); icon=null; }
            UiArt.Release();
        }
    }
}

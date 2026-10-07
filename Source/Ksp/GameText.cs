using KSP.Localization;
using VolumetricExplosionFX.Core;
namespace VolumetricExplosionFX.Ksp
{
    internal static class GameText
    {
        static string language;
        static UiText current;
        internal static UiText Current
        {
            get
            {
                string selected=Localizer.CurrentLanguage;
                if(current==null||language!=selected)
                {
                    language=selected;
                    current=new UiText(selected,tag=>{
                        string value;
                        return Localizer.TryGetStringByTag(tag,out value)?value:null;
                    });
                }
                return current;
            }
        }
    }
}

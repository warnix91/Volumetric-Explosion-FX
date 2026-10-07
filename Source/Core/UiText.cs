using System;
using System.Collections.Generic;
using System.Globalization;
namespace VolumetricExplosionFX.Core
{
    public sealed class UiText
    {
        readonly Func<string,string> lookup;
        readonly Dictionary<string,string> cache=new Dictionary<string,string>(StringComparer.Ordinal);
        public string Language { get; private set; }
        public CultureInfo Culture { get; private set; }
        public UiText(string language,Func<string,string> lookup=null)
        {
            Language=Normalize(language); this.lookup=lookup;
            string culture=Language=="ja"?"ja-JP":Language=="ru"?"ru-RU":Language;
            try { Culture=CultureInfo.GetCultureInfo(culture); }
            catch(CultureNotFoundException) { Culture=CultureInfo.InvariantCulture; }
        }
        public static string Normalize(string language)
        {
            string code=(language??"").Trim().Replace('_','-').ToLowerInvariant();
            switch(code.Split('-')[0])
            {
                case "fr": return "fr-fr";
                case "de": return "de-de";
                case "es": return "es-es";
                case "it": return "it-it";
                case "pt": return "pt-br";
                case "ja": return "ja";
                case "ru": return "ru";
                case "zh": return "zh-cn";
                default: return "en-us";
            }
        }
        public string Get(string key,string fallback)
        {
            string value;
            if(cache.TryGetValue(key,out value)) return value;
            value=lookup!=null?lookup("#VEFX_"+key):null;
            if(string.IsNullOrEmpty(value)||value.StartsWith("#VEFX_",StringComparison.Ordinal)) value=fallback;
            cache[key]=value;
            return value;
        }
        public string Format(string key,string fallback,params object[] args)
        {
            string template=Get(key,fallback);
            for(int i=0;i<args.Length;i++) template=template.Replace("<<"+(i+1)+">>","{"+i+"}");
            try { return string.Format(Culture,template,args); }
            catch(FormatException) { return string.Format(Culture,fallback,args); }
        }
    }
}

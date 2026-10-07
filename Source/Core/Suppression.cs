using System;
using System.Collections.Generic;
namespace VolumetricExplosionFX.Core
{
    public sealed class Suppression<T> where T:class
    {
        readonly List<T> items;
        public Suppression(int capacity=8) { items=new List<T>(capacity); }
        public int Count { get { return items.Count; } }
        public bool Hide(T item,bool on)
        {
            if(item==null||!on) return false;
            if(!items.Contains(item)) items.Add(item);
            return true;
        }
        public bool Holds(T item) { return item!=null&&items.Contains(item); }
        public int Restore(Action<T> restore)
        {
            int n=items.Count;
            for(int i=0;i<n;i++) restore(items[i]);
            items.Clear();
            return n;
        }
        public void Clear() { items.Clear(); }
    }
    public static class StockNames
    {
        public static bool IsInstanceOf(string name,string prefab)
        {
            if(string.IsNullOrEmpty(name)||string.IsNullOrEmpty(prefab)) return false;
            if(name==prefab) return true;
            return name.Length==prefab.Length+7&&name.StartsWith(prefab,StringComparison.Ordinal)&&name.EndsWith("(Clone)",StringComparison.Ordinal);
        }
    }
}

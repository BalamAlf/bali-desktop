using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace BaliDesktop
{
    sealed class VisualElement
    {
        public string Name, Kind, Source, Runtime, AutomationId, State;
        public Rect Bounds;
        public bool Enabled;
        public override string ToString()
        {return Kind+" · "+Name+" · "+Source+" · "+Bounds.X.ToString("0")+", "+Bounds.Y.ToString("0")+(String.IsNullOrEmpty(State)?"":" · "+State);}
    }
    static class VisualResolver
    {
        public static VisualElement[] Resolve(IEnumerable<VisualElement> candidates,string target,string kind=null)
        {
            var items=candidates.Where(x=>x.Enabled && (kind==null || x.Kind==kind) && !x.Bounds.IsEmpty)
                .OrderBy(x=>Math.Round(x.Bounds.Top/16)).ThenBy(x=>x.Bounds.Left).ToArray();
            string phrase=DesktopCatalog.Normalize(target);
            phrase=Regex.Replace(phrase,@"^(?:la|el)\s+","");
            phrase=Regex.Replace(phrase,@"^(?:pestana|boton|opcion|elemento|cuadro)\s*","");
            phrase=Regex.Replace(phrase,@"^(?:que dice|llamad[oa]|de)\s+","");
            if(phrase.Length==0)return new VisualElement[0];
            var ordinals=new Dictionary<string,int>{{"primero",0},{"primera",0},{"primer",0},{"segundo",1},{"segunda",1},{"tercero",2},{"tercera",2}};
            int index;
            string word=phrase.Split(' ')[0];
            if(ordinals.TryGetValue(word,out index))return index<items.Length?new[]{items[index]}:new VisualElement[0];
            if(phrase=="arriba" || phrase=="abajo" || phrase=="izquierda" || phrase=="derecha")
            {
                Func<VisualElement,double> score=x=>phrase=="arriba"?x.Bounds.Top:phrase=="abajo"?-x.Bounds.Bottom:
                    phrase=="izquierda"?x.Bounds.Left:-x.Bounds.Right;
                var ranked=items.OrderBy(score).ToArray();
                if(ranked.Length==0)return ranked;
                return ranked.Where(x=>Math.Abs(score(x)-score(ranked[0]))<12).ToArray();
            }
            return items.Where(x=>DesktopCatalog.Normalize(x.Name)==phrase).ToArray();
        }
    }
}

using UnityEngine;
using static Core.PauseMenu.PauseUiKit;

namespace Core.BarracksSystem
{
    /// <summary>Shared class symbols for recruitment, assignment and the hero inventory.</summary>
    public static class HeroClassIcons
    {
        private static readonly System.Collections.Generic.Dictionary<ClasseHeros, Texture2D> Textures = new();

        public static Texture2D Get(ClasseHeros type)
        {
            if (!Textures.TryGetValue(type, out var texture))
            {
                texture = Resources.Load<Texture2D>("HeroClasses/" + type);
                Textures[type] = texture;
            }
            return texture;
        }

        private static Color Tint(ClasseHeros type) => type switch
        {
            ClasseHeros.Tank => new Color(.45f, .68f, .95f),
            ClasseHeros.Degats => new Color(.95f, .43f, .38f),
            ClasseHeros.Invocateur => new Color(.74f, .54f, .95f),
            ClasseHeros.ChamanFeu => new Color(1f, .50f, .25f),
            ClasseHeros.ChamanGlace => new Color(.40f, .80f, 1f),
            ClasseHeros.ChamanVie => new Color(.42f, .86f, .57f),
            _ => new Color(.95f, .75f, .35f)
        };

        public static void Apply(Transform parent, ClasseHeros type, float size = 48f, float inset = 30f)
        {
            var badge = parent.Find("HeroClassBadge") as RectTransform;
            if (badge == null)
            {
                var background = NewImage("HeroClassBadge", parent, Color.clear, 10);
                background.raycastTarget = false;
                badge = background.rectTransform;
                badge.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
                var symbol = NewRect("Symbol", badge);
                Stretch(symbol, 7f);
                symbol.gameObject.AddComponent<UnityEngine.UI.RawImage>().raycastTarget = false;
            }
            Anchor(badge, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(inset, 0), new Vector2(size, size));
            badge.pivot = new Vector2(0, .5f);
            var tint = Tint(type);
            badge.GetComponent<UnityEngine.UI.Image>().color = new Color(tint.r, tint.g, tint.b, .22f);
            var image = badge.GetComponentInChildren<UnityEngine.UI.RawImage>();
            image.texture = Get(type);
            image.color = Color.white;
        }

        public static void Decorate(Core.PauseMenu.PauseButton button, ClasseHeros type)
        {
            Apply(button.transform, type);
            var label = button.Label;
            label.rectTransform.offsetMin = new Vector2(90f, label.rectTransform.offsetMin.y);
            if (!label.enableAutoSizing)
            {
                label.fontSizeMin = 18f;
                label.fontSizeMax = label.fontSize;
                label.enableAutoSizing = true;
            }
        }
    }
}

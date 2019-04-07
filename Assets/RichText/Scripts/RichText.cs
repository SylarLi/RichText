using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using UnityEngine.EventSystems;
using System;

namespace UnityEngine.UI
{
    /// <summary>
    /// 图片<icon name=??? w=1 h=1 align=top/center/bottom event=*** args=***/> 图片name和sprite需要通过调用AddSprite方法设置
    /// 阴影<material=shadow c=#000000 x=1 y=-1>blablabla...</material>
    /// 描边<material=outline c=#000000 x=1 y=-1>blablabla...</material>
    /// 渐变<material=gradient from=#FFFFFF to=#000000 x=0 y=-1>blablabla...</material>
    /// 下划线<material=underline c=#FFFFFF h=1.5 event=*** args=***>blablabla...</material>
    /// 只有下划线会受到Text.color的影响
    /// 支持Unity RichText标签: color, size, b, i
    /// 标签可嵌套
    /// unity会忽略material对排版的影响，这里全部使用material标签以简化标签检测
    /// </summary>
    [ExecuteInEditMode]
    public class RichText : Text, IPointerClickHandler
    {
        private FontData fontData;

        private UIVertex[] tempVerts;

        private TextInterpreter textInterpreter = new TextInterpreter();

        private Action<string, string> clickHandler = delegate { };

        // --------------- Icon ---------------- //

        private static readonly Regex IconReg = new Regex(@"<icon name=([^>\s]+)([^>]*)/>");
        private static readonly Regex ItemReg = new Regex(@"(\w+)=([^\s]+)");

        private const char ReplaceChar = ' ';

        [System.Serializable]
        public struct SpriteName
        {
            public string name;
            public Sprite sprite;
        }

        private class IconInfo
        {
            public string name;
            public bool still;
            public Color color;
            public Vector2 position;
            public Vector2 size;
            public int vertice;
            public int vlength;
            public string e;
            public string args;
            public string align;
        }

        public SpriteName[] inspectorSpriteList;

        private Dictionary<string, Sprite> spriteList = new Dictionary<string, Sprite>();

        private List<Image> imagePool = new List<Image>();

        private List<IconInfo> icons = new List<IconInfo>();

        private bool iconLayoutDirty = true;

        // --------------- Event -------------- //

        private class Event
        {
            public Rect rect;
            public string name;
            public string args;
        }

        private List<Event> eventList = new List<Event>();

        protected RichText()
        {
            fontData = typeof(Text).GetField("m_FontData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(this) as FontData;
            tempVerts = typeof(Text).GetField("m_TempVerts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(this) as UIVertex[];
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            UpdateSpriteList();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ClearSprite();
            imagePool = null;
            inspectorSpriteList = null;
            spriteList = null;
            clickHandler = null;
        }

        public void UpdateSpriteList()
        {
            spriteList.Clear();
            if (inspectorSpriteList != null && inspectorSpriteList.Length > 0)
            {
                foreach (SpriteName icon in inspectorSpriteList)
                {
                    spriteList[icon.name] = icon.sprite;
                }
            }
        }

        public void AddSprite(string name, Sprite sprite)
        {
            List<SpriteName> list = new List<SpriteName>(inspectorSpriteList);
            list.RemoveAll((SpriteName each) => each.name == name);
            list.Add(new SpriteName() { name = name, sprite = sprite });
            inspectorSpriteList = list.ToArray();
            spriteList[name] = sprite;
        }

        public void ClearSprite()
        {
            foreach (Image image in imagePool)
            {
                if (image) image.sprite = null;
            }
            imagePool.Clear();
            spriteList.Clear();
            inspectorSpriteList = null;
        }

        public void AddListener(Action<string, string> callBack)
        {
            clickHandler += callBack;
        }

        public void RemoveListener(Action<string, string> callback)
        {
            clickHandler -= callback;
        }

        public void RemoveAllListeners()
        {
            clickHandler = delegate { };
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            if (font == null)
                return;

            // We don't care if we the font Texture changes while we are doing our Update.
            // The end result of cachedTextGenerator will be valid for this instance.
            // Otherwise we can get issues like Case 619238.
            m_DisableFontTextureRebuiltCallback = true;

            string richText = text;
            IList<UIVertex> verts = null;

            eventList.Clear();

            // Caculate layout
            try
            {
                richText = CalculateLayoutWithImage(richText, out verts);
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.ToString());
                return;
            }

            // Last 4 verts are always a new line...
            int vertCount = verts.Count;
            for (int i = 0; i < 4; i++)
            {
                if (vertCount > 0)
                {
                    verts.RemoveAt(vertCount - 1);
                    vertCount -= 1;
                }
            }

            // Parse color tag
            List<Tag> tags = null;
            textInterpreter.Parse(richText, out tags);

            // Apply tag effect
            if (tags.Count > 0)
            {
                List<UIVertex> vertexs = verts as List<UIVertex>;
                if (vertexs != null)
                {
                    int capacity = 0;
                    for (int i = 0; i < tags.Count; i++)
                    {
                        Tag tag = tags[i];
                        switch (tag.type)
                        {
                            case TagType.Shadow:
                                {
                                    capacity += (tag.end - tag.start) * 4;
                                    break;
                                }
                            case TagType.Outline:
                                {
                                    capacity += (tag.end - tag.start) * 4 * 5;
                                    break;
                                }
                        }
                    }
                    if (capacity > 0)
                    {
                        capacity = Mathf.Max(capacity, 16);
                        vertexs.Capacity += capacity;
                    }
                }
                for (int i = 0; i < tags.Count; i++)
                {
                    Tag tag = tags[i];
                    try
                    {
                        switch (tag.type)
                        {
                            case TagType.Shadow:
                                {
                                    ApplyShadowEffect(tag as Shadow, verts);
                                    break;
                                }
                            case TagType.Outline:
                                {
                                    ApplyOutlineEffect(tag as Outline, verts);
                                    break;
                                }
                            case TagType.Gradient:
                                {
                                    ApplyGradientEffect(tag as GradientL, verts);
                                    break;
                                }
                            case TagType.Underline:
                                {
                                    ApplyUnderlineEffect(tag as Underline, verts);
                                    break;
                                }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(e.ToString());
                        return;
                    }
                }
            }

            vertCount = verts.Count;

            float unitsPerPixel = 1 / pixelsPerUnit;

            Rect inputRect = rectTransform.rect;

            // get the text alignment anchor point for the text in local space
            Vector2 textAnchorPivot = GetTextAnchorPivot(fontData.alignment);
            Vector2 refPoint = Vector2.zero;
            refPoint.x = (textAnchorPivot.x == 1 ? inputRect.xMax : inputRect.xMin);
            refPoint.y = (textAnchorPivot.y == 0 ? inputRect.yMin : inputRect.yMax);

            // Determine fraction of pixel to offset text mesh.
            Vector2 roundingOffset = PixelAdjustPoint(refPoint) - refPoint;

            toFill.Clear();
            if (roundingOffset != Vector2.zero)
            {
                for (int i = 0; i < vertCount; ++i)
                {
                    int tempVertsIndex = i & 3;
                    tempVerts[tempVertsIndex] = verts[i];
                    tempVerts[tempVertsIndex].position *= unitsPerPixel;
                    tempVerts[tempVertsIndex].position.x += roundingOffset.x;
                    tempVerts[tempVertsIndex].position.y += roundingOffset.y;
                    if (tempVertsIndex == 3)
                        toFill.AddUIVertexQuad(tempVerts);
                }
            }
            else
            {
                for (int i = 0; i < vertCount; ++i)
                {
                    int tempVertsIndex = i & 3;
                    tempVerts[tempVertsIndex] = verts[i];
                    tempVerts[tempVertsIndex].position *= unitsPerPixel;
                    if (tempVertsIndex == 3)
                        toFill.AddUIVertexQuad(tempVerts);
                }
            }
            m_DisableFontTextureRebuiltCallback = false;
        }

        protected string CalculateLayoutWithImage(string richText, out IList<UIVertex> verts)
        {
            Vector2 extents = rectTransform.rect.size;
            var settings = GetGenerationSettings(extents);

            float unitsPerPixel = 1 / pixelsPerUnit;

            float spaceWidth = cachedTextGenerator.GetPreferredWidth(ReplaceChar.ToString(), settings) * unitsPerPixel;

            float fontSize2 = fontSize * 0.5f;

            // Image replace
            icons.Clear();
            Match match = null;
            StringBuilder builder = new StringBuilder();
            while ((match = IconReg.Match(richText)).Success)
            {
                IconInfo iconInfo = new IconInfo();
                iconInfo.name = match.Groups[1].Value;
                iconInfo.size = new Vector2(fontSize2, fontSize2);
                float w = 1, h = 1;
                string e = null, args = null, align = "center";
                string vars = match.Groups[2].Value;
                if (!string.IsNullOrEmpty(vars))
                {
                    Match itemMatch = ItemReg.Match(vars);
                    while (itemMatch.Success)
                    {
                        string name = itemMatch.Groups[1].Value;
                        string value = itemMatch.Groups[2].Value;
                        switch (name)
                        {
                            case "w":
                                {
                                    float.TryParse(value, out w);
                                    break;
                                }
                            case "h":
                                {
                                    float.TryParse(value, out h);
                                    break;
                                }
                            case "event":
                                {
                                    e = value;
                                    break;
                                }
                            case "args":
                                {
                                    args = value;
                                    break;
                                }
                            case "align":
                                {
                                    align = value;
                                    break;
                                }
                        }
                        itemMatch = itemMatch.NextMatch();
                    }
                }
                if (spriteList.ContainsKey(iconInfo.name))
                {
                    Sprite sprite = spriteList[iconInfo.name];
                    if (sprite != null)
                    {
                        iconInfo.size = new Vector2(sprite.rect.width * w, sprite.rect.height * h);
                    }
                }
                iconInfo.e = e;
                iconInfo.args = args;
                iconInfo.align = align;
                iconInfo.vertice = match.Index * 4;
                int holderLen = Mathf.CeilToInt(iconInfo.size.x / spaceWidth);
                iconInfo.vlength = holderLen * 4;
                icons.Add(iconInfo);
                builder.Length = 0;
                builder.Append(richText, 0, match.Index);
                builder.Append(ReplaceChar, holderLen);
                builder.Append(richText, match.Index + match.Length, richText.Length - match.Index - match.Length);
                richText = builder.ToString();
            }

            // Populate charaters
            cachedTextGenerator.Populate(richText, settings);
            verts = cachedTextGenerator.verts;
            // Last 4 verts are always a new line...
            int vertCount = verts.Count - 4;

            // Image wrap check
            for (int i = 0; i < icons.Count; i++)
            {
                IconInfo iconInfo = icons[i];
                int vertice = iconInfo.vertice;
                int vlength = iconInfo.vlength;
                int maxVertice = Mathf.Min(vertice + vlength, vertCount);
                if (verts[maxVertice - 2].position.x * unitsPerPixel > rectTransform.rect.xMax)
                {
                    // New line
                    richText = richText.Insert(vertice / 4, "\r\n");
                    for (int j = i; j < icons.Count; j++)
                    {
                        icons[j].vertice += 8;
                    }
                    cachedTextGenerator.Populate(richText, settings);
                    verts = cachedTextGenerator.verts;
                    vertCount = verts.Count - 4;
                }
            }

            // Image position calculation
            for (int i = icons.Count - 1; i >= 0; i--)
            {
                IconInfo iconInfo = icons[i];
                int vertice = iconInfo.vertice;
                if (vertice < vertCount)
                {
                    var offset = 0f;
                    switch (iconInfo.align)
                    {
                        case "top":
                            offset = (fontSize2 - iconInfo.size.y) * 0.5f + fontSize2;
                            break;
                        case "bottom":
                            offset = (iconInfo.size.y - fontSize2) * 0.5f;
                            break;
                        default:
                            offset = fontSize2 * 0.5f;
                            break;
                    }
                    UIVertex vert = verts[vertice];
                    Vector2 vertex = vert.position;
                    vertex *= unitsPerPixel;
                    vertex.x += rectTransform.sizeDelta.x * (rectTransform.pivot.x - 0.5f) + iconInfo.size.x * 0.5f;
                    vertex.y += rectTransform.sizeDelta.y * (rectTransform.pivot.y - 0.5f) + offset;
                    iconInfo.position = vertex;
                    iconInfo.color = Color.white;

                    if (!string.IsNullOrEmpty(iconInfo.e))
                    {
                        Event e = new Event();
                        e.name = iconInfo.e;
                        e.args = iconInfo.args;
                        e.rect = new Rect(
                            vert.position.x * unitsPerPixel,
                            vert.position.y * unitsPerPixel + offset - iconInfo.size.y * 0.5f,
                            iconInfo.size.x,
                            iconInfo.size.y
                        );
                        eventList.Add(e);
                    }
                }
                else
                {
                    icons.RemoveAt(i);
                }
            }

            // Mark need re-layout image
            iconLayoutDirty = true;

            return richText;
        }

        private void Update()
        {
            if (iconLayoutDirty)
            {
                iconLayoutDirty = false;
                RebuildIconLayout();
            }
        }

        private void RebuildIconLayout()
        {
            imagePool.RemoveAll(image => image == null);
            if (imagePool.Count == 0) GetComponentsInChildren(true, imagePool);
            for (int i = imagePool.Count; i < icons.Count; i++)
            {
                imagePool.Add(NewImage());
            }
            for (int i = 0; i < icons.Count; i++)
            {
                var spriteName = icons[i].name;
                var position = icons[i].position;
                var size = icons[i].size;
                var still = icons[i].still;
                var color = icons[i].color;
                var img = imagePool[i];
                var exist = !string.IsNullOrEmpty(spriteName) && spriteList.ContainsKey(spriteName);
                img.sprite = exist ? spriteList[spriteName] : null;
                img.enabled = exist || still;
                img.color = color;
                img.rectTransform.anchoredPosition = position;
                img.rectTransform.sizeDelta = size;
            }
            for (int i = icons.Count; i < imagePool.Count; i++)
            {
                imagePool[i].sprite = null;
                imagePool[i].enabled = false;
            }
        }

        private Image NewImage()
        {
            var resources = new DefaultControls.Resources();
            var go = DefaultControls.CreateImage(resources);
            if (Application.isPlaying) GameObject.DontDestroyOnLoad(go);
            go.layer = gameObject.layer;
            var rt = go.transform as RectTransform;
            if (rt)
            {
                rt.SetParent(rectTransform);
                rt.localPosition = Vector3.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private void ApplyShadowEffect(Shadow tag, IList<UIVertex> verts)
        {
            int start = tag.start * 4;
            int end = Mathf.Min(tag.end * 4 + 4, verts.Count);
            UIVertex vt;
            for (int i = start; i < end; i++)
            {
                vt = verts[i];
                verts.Add(vt);
                Vector3 v = vt.position;
                v.x += tag.x;
                v.y += tag.y;
                vt.position = v;
                var newColor = tag.c;
                newColor.a = (newColor.a * verts[i].color.a) / 255;
                vt.color = newColor;
                verts[i] = vt;
            }
        }

        private void ApplyOutlineEffect(Outline tag, IList<UIVertex> verts)
        {
            int start = tag.start * 4;
            int end = Mathf.Min(tag.end * 4 + 4, verts.Count);
            UIVertex vt;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int i = start; i < end; i++)
                    {
                        vt = verts[i];
                        Vector3 v = vt.position;
                        v.x += tag.x * x;
                        v.y += tag.y * y;
                        vt.position = v;
                        var newColor = tag.c;
                        newColor.a = (newColor.a * verts[i].color.a) / 255;
                        vt.color = newColor;
                        verts.Add(vt);
                    }
                }
            }
            for (int i = start; i < end; i++)
            {
                verts.Add(verts[i]);
            }
        }

        private void ApplyGradientEffect(GradientL tag, IList<UIVertex> verts)
        {
            int start = tag.start * 4;
            int end = Mathf.Min(tag.end * 4 + 4, verts.Count);
            float min = float.MaxValue;
            float max = float.MinValue;
            Vector2 dir = new Vector2(tag.x, tag.y);
            for (int i = start; i < end; i++)
            {
                float dot = Vector3.Dot(verts[i].position, dir);
                if (dot > max) max = dot;
                else if (dot < min) min = dot;
            }
            float h = max - min;
            UIVertex vt;
            for (int i = start; i < end; i++)
            {
                vt = verts[i];
                vt.color = Color32.Lerp(tag.from, tag.to, (Vector3.Dot(vt.position, dir) - min) / h);
                verts[i] = vt;
            }
        }

        private void ApplyUnderlineEffect(Underline tag, IList<UIVertex> verts)
        {
            float fontSize2 = fontSize * 0.5f;
            float unitsPerPixel = 1 / pixelsPerUnit;

            int start = tag.start * 4;
            int end = Mathf.Min(tag.end * 4 + 4, verts.Count);
            UIVertex vt1 = verts[start + 3];
            UIVertex vt2;
            float minY = vt1.position.y;
            float maxY = verts[start].position.y;
            for (int i = start + 2; i <= end - 2; i += 4)
            {
                vt2 = verts[i];
                bool newline = Mathf.Abs(vt2.position.y - vt1.position.y) > fontSize2;
                if (newline || i == end - 2)
                {
                    IconInfo iconInfo = new IconInfo();
                    iconInfo.still = true;
                    int tailIndex = !newline && i == end - 2 ? i : i - 4;
                    vt2 = verts[tailIndex];
                    minY = Mathf.Min(minY, vt2.position.y);
                    maxY = Mathf.Max(maxY, verts[tailIndex - 1].position.y);
                    iconInfo.size = new Vector2((vt2.position.x - vt1.position.x) * unitsPerPixel, tag.h);
                    Vector2 vertex = new Vector2(vt1.position.x, minY);
                    vertex *= unitsPerPixel;
                    vertex += new Vector2(iconInfo.size.x * 0.5f, -tag.h * 0.5f);
                    vertex += new Vector2(rectTransform.sizeDelta.x * (rectTransform.pivot.x - 0.5f), rectTransform.sizeDelta.y * (rectTransform.pivot.y - 0.5f));
                    iconInfo.position = vertex;
                    iconInfo.color = tag.c == Color.white ? color : tag.c;
                    icons.Add(iconInfo);

                    if (!string.IsNullOrEmpty(tag.e))
                    {
                        Event e = new Event();
                        e.name = tag.e;
                        e.args = tag.args;
                        e.rect = new Rect(
                            vt1.position.x * unitsPerPixel,
                            minY * unitsPerPixel,
                            iconInfo.size.x,
                            (maxY - minY) * unitsPerPixel
                        );
                        eventList.Add(e);
                    }

                    vt1 = verts[i + 1];
                    minY = vt1.position.y;
                    if (newline && i == end - 2) i -= 4;
                }
                else
                {
                    minY = Mathf.Min(minY, vt2.position.y);
                    maxY = Mathf.Max(maxY, verts[i - 1].position.y);
                }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Vector2 lp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out lp);
            for (int i = eventList.Count - 1; i >= 0; i--)
            {
                Event e = eventList[i];
                if (e.rect.Contains(lp))
                {
                    clickHandler.Invoke(e.name, e.args);
                    break;
                }
            }
        }

        private class TextInterpreter
        {
            private static readonly Regex TagReg = new Regex(@"</*material[^>]*>");
            private const string TagSuffix = "</material>";

            private List<Tag> close;
            private Stack<InterpretInfo> open;

            public TextInterpreter()
            {
                close = new List<Tag>();
                open = new Stack<InterpretInfo>();
            }

            public void Parse(string richText, out List<Tag> tags)
            {
                close.Clear();
                open.Clear();
                Match match = TagReg.Match(richText);
                while (match.Success)
                {
                    if (match.Value == TagSuffix)
                    {
                        if (open.Count > 0)
                        {
                            InterpretInfo iinfo = open.Pop();
                            iinfo.end = match.Index - 1;
                            if (iinfo.end >= iinfo.start)
                            {
                                Tag tag = iinfo.ToTag();
                                if (tag != null) close.Add(tag);
                            }
                        }
                    }
                    else
                    {
                        InterpretInfo iinfo = new InterpretInfo();
                        iinfo.str = match.Value;
                        iinfo.start = match.Index + match.Length;
                        open.Push(iinfo);
                    }
                    match = match.NextMatch();
                }
                tags = close;
            }
        }

        private class InterpretInfo
        {
            private static readonly Regex TagReg = new Regex(@"<material=([^>\s]+)([^>]*)>");
            private static readonly Regex ItemReg = new Regex(@"(\w+)=([^\s]+)");
            public string str;
            public int start;
            public int end;
            public Tag ToTag()
            {
                Tag tag = null;
                Match match = TagReg.Match(str);
                if (match.Success)
                {
                    string type = match.Groups[1].Value;
                    if (!type.StartsWith("#"))
                    {
                        var values = ItemReg.Matches(match.Groups[2].Value);
                        switch (type)
                        {
                            case "shadow":
                                {
                                    tag = new Shadow();
                                    break;
                                }
                            case "outline":
                                {
                                    tag = new Outline();
                                    break;
                                }
                            case "gradient":
                                {
                                    tag = new GradientL();
                                    break;
                                }
                            case "underline":
                                {
                                    tag = new Underline();
                                    break;
                                }
                        }
                        if (tag != null)
                        {
                            tag.start = start;
                            tag.end = end;
                            for (int i = 0; i < values.Count; i++)
                            {
                                string name = values[i].Groups[1].Value;
                                string value = values[i].Groups[2].Value;
                                tag.SetValue(name, value);
                            }
                        }
                    }
                }
                return tag;
            }
        }

        private enum TagType
        {
            None,
            Shadow,
            Outline,
            Gradient,
            Underline,
        }

        private abstract class Tag
        {
            public int start;
            public int end;
            public virtual TagType type
            {
                get
                {
                    return TagType.None;
                }
            }
            public virtual void SetValue(string name, string value)
            {

            }
        }

        private class Shadow : Tag
        {
            public Color c = Color.black;
            public float x = 1;
            public float y = -1;
            public override TagType type
            {
                get
                {
                    return TagType.Shadow;
                }
            }
            public override void SetValue(string name, string value)
            {
                base.SetValue(name, value);
                switch (name)
                {
                    case "c":
                        {
                            ColorUtility.TryParseHtmlString(value, out c);
                            break;
                        }
                    case "x":
                        {
                            float.TryParse(value, out x);
                            break;
                        }
                    case "y":
                        {
                            float.TryParse(value, out y);
                            break;
                        }
                }
            }
        }

        private class Outline : Shadow
        {
            public override TagType type
            {
                get
                {
                    return TagType.Outline;
                }
            }
        }

        private class GradientL : Tag
        {
            public Color from = Color.white;
            public Color to = Color.black;
            public float x = 0;
            public float y = -1;
            public override TagType type
            {
                get
                {
                    return TagType.Gradient;
                }
            }
            public override void SetValue(string name, string value)
            {
                base.SetValue(name, value);
                switch (name)
                {
                    case "from":
                        {
                            ColorUtility.TryParseHtmlString(value, out from);
                            break;
                        }
                    case "to":
                        {
                            ColorUtility.TryParseHtmlString(value, out to);
                            break;
                        }
                    case "x":
                        {
                            float.TryParse(value, out x);
                            break;
                        }
                    case "y":
                        {
                            float.TryParse(value, out y);
                            break;
                        }
                }
            }
        }

        private class Underline : Tag
        {
            public Color c = Color.white;
            public float h = 1.5f;
            public string e;
            public string args;
            public override TagType type
            {
                get
                {
                    return TagType.Underline;
                }
            }
            public override void SetValue(string name, string value)
            {
                base.SetValue(name, value);
                switch (name)
                {
                    case "c":
                        {
                            ColorUtility.TryParseHtmlString(value, out c);
                            break;
                        }
                    case "h":
                        {
                            float.TryParse(value, out h);
                            break;
                        }
                    case "event":
                        {
                            e = value;
                            break;
                        }
                    case "args":
                        {
                            args = value;
                            break;
                        }
                }
            }
        }
    }
}

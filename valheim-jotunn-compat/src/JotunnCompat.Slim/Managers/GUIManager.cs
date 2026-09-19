using System;
using UnityEngine;
using UnityEngine.UI;

namespace Jotunn.Managers
{
    public sealed class GUIManager
    {
        private static GUIManager instance;
        public static GUIManager Instance => instance ??= new GUIManager();

        public static event Action OnCustomGUIAvailable;
        public static GameObject CustomGUIFront { get; private set; }

        public Color ValheimOrange =
            new Color(1f, 0.631f, 0.235f, 1f);

        public ColorBlock ValheimToggleColorBlock = new ColorBlock
        {
            normalColor = new Color(0.61f, 0.61f, 0.61f, 1f),
            highlightedColor = Color.white,
            pressedColor = new Color(0.784f, 0.784f, 0.784f, 1f),
            selectedColor = Color.white,
            disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.502f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        public ColorBlock ValheimButtonColorBlock = new ColorBlock
        {
            normalColor = new Color(0.824f, 0.824f, 0.824f, 1f),
            highlightedColor = Color.white,
            pressedColor = new Color(0.537f, 0.556f, 0.556f, 1f),
            selectedColor = new Color(0.824f, 0.824f, 0.824f, 1f),
            disabledColor = new Color(0.566f, 0.566f, 0.566f, 0.502f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        private static bool inputBlocked;
        private static int inputBlockRequests;

        private GUIManager() { }

        internal void EnsureGUI()
        {
            if (CustomGUIFront)
            {
                return;
            }

            var root = new GameObject(
                "JotunnSlimGUI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            root.transform.SetParent(Main.RootObject.transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            CustomGUIFront = root;
            OnCustomGUIAvailable?.Invoke();
        }

        public Sprite GetSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            return PrefabManager.Cache.GetPrefab<Sprite>(spriteName);
        }

        public static void BlockInput(bool block)
        {
            if (block)
            {
                inputBlockRequests++;
            }
            else
            {
                inputBlockRequests = Math.Max(0, inputBlockRequests - 1);
            }

            inputBlocked = inputBlockRequests > 0;

            if (GameCamera.instance)
            {
                GameCamera.instance.m_mouseCapture = !inputBlocked;
                GameCamera.instance.UpdateMouseCapture();
            }
        }

        public void ApplyTextStyle(Text text, int fontSize = 16)
        {
            if (!text)
            {
                return;
            }

            text.fontSize = fontSize;
            text.color = ValheimOrange;
            text.alignment = TextAnchor.MiddleCenter;
        }

        public void ApplyButtonStyle(Button button, int fontSize = 16)
        {
            if (!button)
            {
                return;
            }

            button.colors = ValheimButtonColorBlock;
            var label = button.GetComponentInChildren<Text>(true);
            if (label)
            {
                ApplyTextStyle(label, fontSize);
            }
        }

        public void ApplyDropdownStyle(Dropdown dropdown, int fontSize = 16)
        {
            if (!dropdown)
            {
                return;
            }

            dropdown.colors = ValheimButtonColorBlock;
            if (dropdown.captionText)
            {
                ApplyTextStyle(dropdown.captionText, fontSize);
            }

            if (dropdown.itemText)
            {
                ApplyTextStyle(dropdown.itemText, fontSize);
            }
        }

        public void ApplyInputFieldStyle(InputField input, int fontSize = 16)
        {
            if (!input)
            {
                return;
            }

            input.colors = ValheimButtonColorBlock;
            if (input.textComponent)
            {
                ApplyTextStyle(input.textComponent, fontSize);
                input.textComponent.color = Color.white;
                input.textComponent.alignment = TextAnchor.MiddleLeft;
            }

            if (input.placeholder is Text placeholder)
            {
                ApplyTextStyle(placeholder, fontSize);
                placeholder.color = new Color(1f, 1f, 1f, 0.5f);
                placeholder.alignment = TextAnchor.MiddleLeft;
            }
        }

        public void ApplyToogleStyle(Toggle toggle)
        {
            if (!toggle)
            {
                return;
            }

            toggle.colors = ValheimToggleColorBlock;
        }

        public GameObject CreateButton(
            string text,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            float width = 0f,
            float height = 0f)
        {
            EnsureGUI();
            var buttonObject = DefaultControls.CreateButton(ControlResources());
            buttonObject.transform.SetParent(
                parent ? parent : CustomGUIFront.transform,
                false);

            var button = buttonObject.GetComponent<Button>();
            ApplyButtonStyle(button);

            var label = buttonObject.GetComponentInChildren<Text>(true);
            if (label)
            {
                label.text = text ?? string.Empty;
            }

            ApplyRect(
                buttonObject.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                position,
                width,
                height);

            return buttonObject;
        }

        public GameObject CreateInputField(
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            InputField.ContentType contentType = InputField.ContentType.Standard,
            string placeholderText = null,
            int fontSize = 16,
            float width = 0f,
            float height = 0f)
        {
            EnsureGUI();
            var inputObject =
                DefaultControls.CreateInputField(ControlResources());
            inputObject.transform.SetParent(
                parent ? parent : CustomGUIFront.transform,
                false);

            var input = inputObject.GetComponent<InputField>();
            input.contentType = contentType;
            ApplyInputFieldStyle(input, fontSize);

            if (input.placeholder is Text placeholder &&
                !string.IsNullOrEmpty(placeholderText))
            {
                placeholder.text = placeholderText;
            }

            ApplyRect(
                inputObject.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                position,
                width,
                height);

            return inputObject;
        }

        public GameObject CreateWoodpanel(
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            float width = 0f,
            float height = 0f)
        {
            return CreateWoodpanel(
                parent,
                anchorMin,
                anchorMax,
                position,
                width,
                height,
                true);
        }

        public GameObject CreateWoodpanel(
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            float width = 0f,
            float height = 0f,
            bool draggable = true)
        {
            EnsureGUI();
            var panel = DefaultControls.CreatePanel(ControlResources());
            panel.name = "Woodpanel";
            panel.transform.SetParent(
                parent ? parent : CustomGUIFront.transform,
                false);

            var image = panel.GetComponent<Image>();
            if (image)
            {
                image.color = new Color(0.12f, 0.09f, 0.06f, 0.96f);
                image.sprite = GetSprite("woodpanel");
                image.type = image.sprite ? Image.Type.Sliced : Image.Type.Simple;
            }

            ApplyRect(
                panel.GetComponent<RectTransform>(),
                anchorMin,
                anchorMax,
                position,
                width,
                height);

            return panel;
        }

        public GameObject CreateScrollView(
            Transform parent,
            bool showHorizontalScrollbar,
            bool showVerticalScrollbar,
            float handleSize,
            float handleDistanceToBorder,
            ColorBlock handleColors,
            Color slidingAreaBackgroundColor,
            float width,
            float height)
        {
            EnsureGUI();

            var wrapper = new GameObject("Canvas", typeof(RectTransform));
            wrapper.transform.SetParent(
                parent ? parent : CustomGUIFront.transform,
                false);
            ApplyRect(
                wrapper.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                width,
                height);

            var scrollView =
                DefaultControls.CreateScrollView(ControlResources());
            scrollView.name = "Scroll View";
            scrollView.transform.SetParent(wrapper.transform, false);
            ApplyRect(
                scrollView.GetComponent<RectTransform>(),
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                width,
                height);

            var scroll = scrollView.GetComponent<ScrollRect>();
            if (scroll)
            {
                scroll.horizontal = showHorizontalScrollbar;
                scroll.vertical = showVerticalScrollbar;
                scroll.scrollSensitivity = 35f;
            }

            var content = scrollView.transform.Find("Viewport/Content");
            if (content && !content.GetComponent<VerticalLayoutGroup>())
            {
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 4f;

                var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            foreach (var scrollbar in
                scrollView.GetComponentsInChildren<Scrollbar>(true))
            {
                scrollbar.size = Mathf.Clamp01(handleSize / 100f);
                scrollbar.colors = handleColors;
            }

            return wrapper;
        }

        public void CreateColorPicker(
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            Color original,
            string message,
            Jotunn.GUI.ColorPicker.ColorEvent onColorChanged,
            Jotunn.GUI.ColorPicker.ColorEvent onColorSelected,
            bool useAlpha = false)
        {
            EnsureGUI();
            Jotunn.GUI.ColorPicker.Open(
                original,
                message,
                onColorChanged,
                onColorSelected,
                useAlpha);
        }

        private DefaultControls.Resources ControlResources()
        {
            return new DefaultControls.Resources
            {
                standard = GetSprite("woodpanel"),
                background = GetSprite("woodpanel"),
                inputField = GetSprite("woodpanel"),
                knob = GetSprite("button"),
                checkmark = GetSprite("checkmark"),
                dropdown = GetSprite("button"),
                mask = GetSprite("woodpanel")
            };
        }

        private static void ApplyRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            float width,
            float height)
        {
            if (!rect)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;

            if (width > 0f)
            {
                rect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    width);
            }

            if (height > 0f)
            {
                rect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    height);
            }
        }
    }
}

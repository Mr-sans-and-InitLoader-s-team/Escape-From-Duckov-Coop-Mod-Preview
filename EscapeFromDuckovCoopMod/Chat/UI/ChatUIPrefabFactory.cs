using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace EscapeFromDuckovCoopMod.Chat.UI
{
    /// <summary>
    /// 聊天UI预制体工厂类
    /// </summary>
    public static class ChatUIPrefabFactory
    {
        /// <summary>
        /// 创建聊天输入覆盖层预制体
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <returns>覆盖层GameObject</returns>
        public static GameObject CreateChatInputOverlay(Transform parent = null)
        {
            // 创建根对象
            var overlayRoot = new GameObject("ChatInputOverlay");
            if (parent != null)
            {
                overlayRoot.transform.SetParent(parent, false);
            }

            // 添加Canvas组件（如果父对象没有Canvas）
            var canvas = overlayRoot.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = overlayRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000; // 确保在最上层
                
                var canvasScaler = overlayRoot.AddComponent<CanvasScaler>();
                canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasScaler.referenceResolution = new Vector2(1920, 1080);
                
                overlayRoot.AddComponent<GraphicRaycaster>();
            }

            // 创建覆盖层面板
            var overlayPanel = new GameObject("OverlayPanel");
            overlayPanel.transform.SetParent(overlayRoot.transform, false);

            var overlayRect = overlayPanel.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            overlayRect.anchoredPosition = Vector2.zero;

            // 创建背景遮罩
            var backgroundMask = overlayPanel.AddComponent<Image>();
            backgroundMask.color = new Color(0, 0, 0, 0.7f);

            // 创建输入对话框
            var inputDialog = CreateInputDialog(overlayPanel.transform);

            // 添加ChatInputOverlay组件
            var overlayComponent = overlayRoot.AddComponent<ChatInputOverlay>();

            // 通过反射设置私有字段（因为是SerializeField）
            var overlayType = typeof(ChatInputOverlay);
            var overlayPanelField = overlayType.GetField("overlayPanel", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var backgroundMaskField = overlayType.GetField("backgroundMask", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var inputDialogField = overlayType.GetField("inputDialog", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var inputFieldField = overlayType.GetField("inputField", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var sendButtonField = overlayType.GetField("sendButton", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var titleTextField = overlayType.GetField("titleText", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            overlayPanelField?.SetValue(overlayComponent, overlayPanel);
            backgroundMaskField?.SetValue(overlayComponent, backgroundMask);
            inputDialogField?.SetValue(overlayComponent, inputDialog);
            inputFieldField?.SetValue(overlayComponent, inputDialog.transform.Find("InputField")?.GetComponent<TMP_InputField>());
            sendButtonField?.SetValue(overlayComponent, inputDialog.transform.Find("SendButton")?.GetComponent<Button>());
            titleTextField?.SetValue(overlayComponent, inputDialog.transform.Find("Title")?.GetComponent<TextMeshProUGUI>());

            Debug.Log("聊天输入覆盖层预制体创建完成");
            return overlayRoot;
        }

        /// <summary>
        /// 创建输入对话框
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <returns>对话框GameObject</returns>
        private static GameObject CreateInputDialog(Transform parent)
        {
            var dialog = new GameObject("InputDialog");
            dialog.transform.SetParent(parent, false);

            var dialogRect = dialog.AddComponent<RectTransform>();
            dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRect.sizeDelta = new Vector2(600, 200);
            dialogRect.anchoredPosition = Vector2.zero;

            // 对话框背景
            var dialogBg = dialog.AddComponent<Image>();
            dialogBg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            // 添加圆角效果（如果需要）
            var outline = dialog.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(2, 2);

            // 创建标题
            CreateTitle(dialog.transform);

            // 创建输入框
            CreateInputField(dialog.transform);

            // 创建发送按钮
            CreateSendButton(dialog.transform);

            return dialog;
        }

        /// <summary>
        /// 创建标题文本
        /// </summary>
        /// <param name="parent">父对象</param>
        private static void CreateTitle(Transform parent)
        {
            var title = new GameObject("Title");
            title.transform.SetParent(parent, false);

            var titleRect = title.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.7f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.sizeDelta = Vector2.zero;
            titleRect.anchoredPosition = Vector2.zero;

            var titleText = title.AddComponent<TextMeshProUGUI>();
            titleText.text = "聊天输入";
            titleText.fontSize = 24;
            titleText.color = Color.white;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// 创建输入框
        /// </summary>
        /// <param name="parent">父对象</param>
        private static void CreateInputField(Transform parent)
        {
            var inputFieldObj = new GameObject("InputField");
            inputFieldObj.transform.SetParent(parent, false);

            var inputRect = inputFieldObj.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.1f, 0.3f);
            inputRect.anchorMax = new Vector2(0.7f, 0.6f);
            inputRect.sizeDelta = Vector2.zero;
            inputRect.anchoredPosition = Vector2.zero;

            var inputBg = inputFieldObj.AddComponent<Image>();
            inputBg.color = Color.white;

            var inputField = inputFieldObj.AddComponent<TMP_InputField>();
            inputField.characterLimit = 200;

            // 创建输入框文本区域
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(inputFieldObj.transform, false);

            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.sizeDelta = Vector2.zero;
            textAreaRect.anchoredPosition = Vector2.zero;

            var textAreaMask = textArea.AddComponent<RectMask2D>();

            // 创建占位符文本
            var placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textArea.transform, false);

            var placeholderRect = placeholder.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.sizeDelta = Vector2.zero;
            placeholderRect.anchoredPosition = Vector2.zero;

            var placeholderText = placeholder.AddComponent<TextMeshProUGUI>();
            placeholderText.text = "输入聊天消息...";
            placeholderText.fontSize = 18;
            placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            placeholderText.margin = new Vector4(10, 5, 10, 5);

            // 创建输入文本
            var text = new GameObject("Text");
            text.transform.SetParent(textArea.transform, false);

            var textRect = text.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            var inputText = text.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 18;
            inputText.color = Color.black;
            inputText.margin = new Vector4(10, 5, 10, 5);

            // 设置输入框引用
            inputField.textViewport = textAreaRect;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
        }

        /// <summary>
        /// 创建发送按钮
        /// </summary>
        /// <param name="parent">父对象</param>
        private static void CreateSendButton(Transform parent)
        {
            var button = new GameObject("SendButton");
            button.transform.SetParent(parent, false);

            var buttonRect = button.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.75f, 0.3f);
            buttonRect.anchorMax = new Vector2(0.9f, 0.6f);
            buttonRect.sizeDelta = Vector2.zero;
            buttonRect.anchoredPosition = Vector2.zero;

            var buttonImage = button.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.6f, 1f, 1f);

            var buttonComponent = button.AddComponent<Button>();
            buttonComponent.targetGraphic = buttonImage;

            // 按钮文本
            var buttonText = new GameObject("Text");
            buttonText.transform.SetParent(button.transform, false);

            var textRect = buttonText.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            var text = buttonText.AddComponent<TextMeshProUGUI>();
            text.text = "发送";
            text.fontSize = 16;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
        }

        /// <summary>
        /// 创建聊天面板预制体
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <returns>聊天面板GameObject</returns>
        public static GameObject CreateChatPanel(Transform parent = null)
        {
            var panel = new GameObject("ChatPanel");
            if (parent != null)
            {
                panel.transform.SetParent(parent, false);
            }

            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(400, 300);

            // 面板背景
            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0, 0, 0, 0.3f);

            // 创建滚动视图
            var scrollView = CreateScrollView(panel.transform);

            // 创建提示文本
            var promptText = CreatePromptText(panel.transform);

            // 添加ChatPanel组件
            var chatPanelComponent = panel.AddComponent<ChatPanel>();

            // 设置组件引用（通过反射）
            var panelType = typeof(ChatPanel);
            var scrollRectField = panelType.GetField("scrollRect", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var messageContainerField = panelType.GetField("messageContainer", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var promptTextField = panelType.GetField("promptText", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            scrollRectField?.SetValue(chatPanelComponent, scrollView.GetComponent<ScrollRect>());
            messageContainerField?.SetValue(chatPanelComponent, scrollView.transform.Find("Viewport/Content"));
            promptTextField?.SetValue(chatPanelComponent, promptText.GetComponent<TextMeshProUGUI>());

            Debug.Log("聊天面板预制体创建完成");
            return panel;
        }

        /// <summary>
        /// 创建滚动视图
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <returns>滚动视图GameObject</returns>
        private static GameObject CreateScrollView(Transform parent)
        {
            var scrollView = new GameObject("ScrollView");
            scrollView.transform.SetParent(parent, false);

            var scrollRect = scrollView.AddComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = new Vector2(1, 0.9f);
            scrollRect.sizeDelta = Vector2.zero;
            scrollRect.anchoredPosition = Vector2.zero;

            var scrollComponent = scrollView.AddComponent<ScrollRect>();
            scrollComponent.horizontal = false;
            scrollComponent.vertical = true;

            // 创建Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollView.transform, false);

            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.anchoredPosition = Vector2.zero;

            viewport.AddComponent<RectMask2D>();

            // 创建Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);

            var contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.sizeDelta = new Vector2(0, 0);
            contentRect.anchoredPosition = Vector2.zero;

            var contentSizeFitter = content.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var verticalLayoutGroup = content.AddComponent<VerticalLayoutGroup>();
            verticalLayoutGroup.childControlHeight = false;
            verticalLayoutGroup.childControlWidth = true;
            verticalLayoutGroup.childForceExpandHeight = false;
            verticalLayoutGroup.childForceExpandWidth = true;
            verticalLayoutGroup.spacing = 5;
            verticalLayoutGroup.padding = new RectOffset(10, 10, 10, 10);

            // 设置滚动组件引用
            scrollComponent.viewport = viewportRect;
            scrollComponent.content = contentRect;

            return scrollView;
        }

        /// <summary>
        /// 创建提示文本
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <returns>提示文本GameObject</returns>
        private static GameObject CreatePromptText(Transform parent)
        {
            var prompt = new GameObject("PromptText");
            prompt.transform.SetParent(parent, false);

            var promptRect = prompt.AddComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0, 0.9f);
            promptRect.anchorMax = Vector2.one;
            promptRect.sizeDelta = Vector2.zero;
            promptRect.anchoredPosition = Vector2.zero;

            var promptText = prompt.AddComponent<TextMeshProUGUI>();
            promptText.text = "按 Enter 键开始聊天...";
            promptText.fontSize = 14;
            promptText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            promptText.alignment = TextAlignmentOptions.Center;

            return prompt;
        }

        /// <summary>
        /// 创建消息项预制体
        /// </summary>
        /// <returns>消息项GameObject</returns>
        public static GameObject CreateMessageItemPrefab()
        {
            var messageItem = new GameObject("MessageItem");

            var itemRect = messageItem.AddComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0, 30);

            var itemBg = messageItem.AddComponent<Image>();
            itemBg.color = new Color(1, 1, 1, 0.1f);

            var layoutElement = messageItem.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 30;
            layoutElement.flexibleHeight = 0;

            // 创建用户名文本
            var userName = new GameObject("UserName");
            userName.transform.SetParent(messageItem.transform, false);

            var userNameRect = userName.AddComponent<RectTransform>();
            userNameRect.anchorMin = new Vector2(0, 0);
            userNameRect.anchorMax = new Vector2(0.3f, 1);
            userNameRect.sizeDelta = Vector2.zero;
            userNameRect.anchoredPosition = Vector2.zero;

            var userNameText = userName.AddComponent<TextMeshProUGUI>();
            userNameText.fontSize = 14;
            userNameText.color = Color.white;
            userNameText.alignment = TextAlignmentOptions.Left;
            userNameText.margin = new Vector4(5, 0, 5, 0);

            // 创建消息文本
            var message = new GameObject("Message");
            message.transform.SetParent(messageItem.transform, false);

            var messageRect = message.AddComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0.3f, 0);
            messageRect.anchorMax = new Vector2(0.8f, 1);
            messageRect.sizeDelta = Vector2.zero;
            messageRect.anchoredPosition = Vector2.zero;

            var messageText = message.AddComponent<TextMeshProUGUI>();
            messageText.fontSize = 14;
            messageText.color = Color.white;
            messageText.alignment = TextAlignmentOptions.Left;
            messageText.margin = new Vector4(5, 0, 5, 0);

            // 创建时间戳文本
            var timestamp = new GameObject("Timestamp");
            timestamp.transform.SetParent(messageItem.transform, false);

            var timestampRect = timestamp.AddComponent<RectTransform>();
            timestampRect.anchorMin = new Vector2(0.8f, 0);
            timestampRect.anchorMax = new Vector2(1, 1);
            timestampRect.sizeDelta = Vector2.zero;
            timestampRect.anchoredPosition = Vector2.zero;

            var timestampText = timestamp.AddComponent<TextMeshProUGUI>();
            timestampText.fontSize = 12;
            timestampText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            timestampText.alignment = TextAlignmentOptions.Right;
            timestampText.margin = new Vector4(5, 0, 5, 0);

            // 添加ChatMessageItem组件
            messageItem.AddComponent<ChatMessageItem>();

            Debug.Log("消息项预制体创建完成");
            return messageItem;
        }
    }
}